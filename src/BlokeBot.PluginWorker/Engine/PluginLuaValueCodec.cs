using System.Collections.Immutable;
using System.Text;
using BlokeBot.Plugins.Contracts;
using KeraLua;

namespace BlokeBot.PluginWorker;

internal abstract record PluginLuaValueReadOutcome
{
    private PluginLuaValueReadOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Mapped, TResult> mapped,
        Func<Rejected, TResult> rejected
    );

    internal sealed record Mapped(PluginValue Value) : PluginLuaValueReadOutcome
    {
        internal override TResult Match<TResult>(
            Func<Mapped, TResult> mapped,
            Func<Rejected, TResult> rejected
        ) => mapped(this);
    }

    internal sealed record Rejected(PluginWorkerFailure Failure) : PluginLuaValueReadOutcome
    {
        internal override TResult Match<TResult>(
            Func<Mapped, TResult> mapped,
            Func<Rejected, TResult> rejected
        ) => rejected(this);
    }
}

internal static class PluginLuaValueCodec
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    internal static PluginLuaValueReadOutcome Read(Lua lua, int index)
    {
        var nodes = 0;
        var outcome = Read(lua, index, depth: 0, ref nodes);
        return
            outcome is PluginLuaValueReadOutcome.Mapped mapped
            && PluginValueValidator.Validate(mapped.Value) is PluginValueValidationOutcome.Invalid
            ? Rejected("Lua returned a value outside the plugin value bounds.")
            : outcome;
    }

    internal static void Push(Lua lua, PluginValue value)
    {
        if (PluginValueValidator.Validate(value) is PluginValueValidationOutcome.Invalid)
        {
            throw new ArgumentException(
                "Plugin value is outside the contract bounds.",
                nameof(value)
            );
        }

        PushValidated(lua, value);
    }

    private static PluginLuaValueReadOutcome Read(Lua lua, int index, int depth, ref int nodes)
    {
        if (depth > PluginContractLimits.MaximumPluginValueDepth)
        {
            return Rejected("Lua value depth exceeds the plugin value limit.");
        }

        nodes++;
        return nodes > PluginContractLimits.MaximumPluginValueNodes
            ? Rejected("Lua value node count exceeds the plugin value limit.")
            : lua.Type(index) switch
            {
                LuaType.Nil => new PluginLuaValueReadOutcome.Mapped(new PluginValue.Nil()),
                LuaType.Boolean => new PluginLuaValueReadOutcome.Mapped(
                    new PluginValue.Boolean(lua.ToBoolean(index))
                ),
                LuaType.Number => ReadNumber(lua, index),
                LuaType.String => ReadString(lua, index),
                LuaType.Table => ReadTable(lua, index, depth, ref nodes),
                _ => Rejected("Lua returned a value kind outside the plugin value contract."),
            };
    }

    private static PluginLuaValueReadOutcome ReadNumber(Lua lua, int index)
    {
        var value = lua.ToNumber(index);
        return double.IsFinite(value)
            ? new PluginLuaValueReadOutcome.Mapped(new PluginValue.Number(value))
            : Rejected("Lua returned a non-finite number.");
    }

    private static PluginLuaValueReadOutcome ReadString(Lua lua, int index)
    {
        try
        {
            return new PluginLuaValueReadOutcome.Mapped(
                new PluginValue.String(_strictUtf8.GetString(lua.ToBuffer(index)))
            );
        }
        catch (DecoderFallbackException)
        {
            return Rejected("Lua returned a string that is not valid UTF-8.");
        }
    }

    private static PluginLuaValueReadOutcome ReadTable(Lua lua, int index, int depth, ref int nodes)
    {
        var table = lua.AbsIndex(index);
        var array = new SortedDictionary<long, PluginValue>();
        var map = new List<PluginValueProperty>();
        lua.PushNil();
        while (lua.Next(table))
        {
            var value = Read(lua, -1, depth + 1, ref nodes);
            if (value is PluginLuaValueReadOutcome.Rejected rejected)
            {
                lua.Pop(2);
                return rejected;
            }

            var mapped = ((PluginLuaValueReadOutcome.Mapped)value).Value;
            if (lua.IsInteger(-2))
            {
                var key = lua.ToInteger(-2);
                if (key <= 0 || !array.TryAdd(key, mapped))
                {
                    lua.Pop(2);
                    return Rejected("Lua returned a table with unsupported numeric keys.");
                }
            }
            else if (lua.IsString(-2))
            {
                var key = ReadString(lua, -2);
                if (key is not PluginLuaValueReadOutcome.Mapped { Value: PluginValue.String name })
                {
                    lua.Pop(2);
                    return Rejected("Lua returned an invalid map key.");
                }

                map.Add(new(name.Value, mapped));
            }
            else
            {
                lua.Pop(2);
                return Rejected("Lua returned a table with unsupported key kinds.");
            }

            lua.Pop(1);
        }

        if (array.Count > 0 && map.Count > 0)
        {
            return Rejected("Lua returned a mixed array and map table.");
        }

        if (array.Count > 0)
        {
            var expected = 1L;
            foreach (var key in array.Keys)
            {
                if (key != expected++)
                {
                    return Rejected("Lua returned a sparse array.");
                }
            }

            return new PluginLuaValueReadOutcome.Mapped(
                new PluginValue.Array(array.Values.ToImmutableArray())
            );
        }

        return new PluginLuaValueReadOutcome.Mapped(new PluginValue.Map(map.ToImmutableArray()));
    }

    private static void PushValidated(Lua lua, PluginValue value)
    {
        switch (value)
        {
            case PluginValue.Nil:
                lua.PushNil();
                break;
            case PluginValue.Boolean boolean:
                lua.PushBoolean(boolean.Value);
                break;
            case PluginValue.Number number:
                lua.PushNumber(number.Value);
                break;
            case PluginValue.String text:
                lua.PushBuffer(Encoding.UTF8.GetBytes(text.Value));
                break;
            case PluginValue.Array array:
                lua.CreateTable(array.Items.Length, 0);
                var arrayIndex = lua.AbsIndex(-1);
                for (var index = 0; index < array.Items.Length; index++)
                {
                    PushValidated(lua, array.Items[index]);
                    lua.RawSetInteger(arrayIndex, index + 1);
                }

                break;
            case PluginValue.Map map:
                lua.CreateTable(0, map.Properties.Length);
                var mapIndex = lua.AbsIndex(-1);
                foreach (var property in map.Properties)
                {
                    lua.PushString(property.Name);
                    PushValidated(lua, property.Value);
                    lua.RawSet(mapIndex);
                }

                break;
        }
    }

    private static PluginLuaValueReadOutcome.Rejected Rejected(string message) =>
        new(new(PluginWorkerFailureCode.InvalidValue, message));
}
