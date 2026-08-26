using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BlokeBot.Plugins.Contracts.Testing;

public sealed record PluginAuthoringSemanticMember(string Kind, string CanonicalName, string Shape);

public sealed record PluginAuthoringSemanticOmission(string CanonicalName);

public static class PluginAuthoringSemanticCoverage
{
    private const BindingFlags _declaredPublic =
        BindingFlags.DeclaredOnly
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.Public;

    public static ImmutableArray<PluginAuthoringSemanticMember> Members(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        var members = ImmutableArray.CreateBuilder<PluginAuthoringSemanticMember>();
        members.Add(new("type", TypeName(contractType), TypeShape(contractType)));

        if (contractType.IsEnum)
        {
            foreach (var name in Enum.GetNames(contractType))
            {
                members.Add(
                    new(
                        "enum value",
                        $"{TypeName(contractType)}.{name}",
                        Convert.ToString(
                            Enum.Parse(contractType, name),
                            CultureInfo.InvariantCulture
                        )!
                    )
                );
            }
            return members.ToImmutable();
        }

        AddMembers(
            members,
            contractType.GetConstructors(_declaredPublic),
            "constructor",
            ConstructorName,
            ConstructorShape
        );
        AddMembers(
            members,
            contractType.GetFields(_declaredPublic).Where(IsCanonical),
            "field",
            FieldName,
            FieldShape
        );
        AddMembers(
            members,
            contractType.GetProperties(_declaredPublic).Where(IsCanonical),
            "property",
            PropertyName,
            PropertyShape
        );
        AddMembers(
            members,
            contractType.GetEvents(_declaredPublic).Where(IsCanonical),
            "event",
            EventName,
            EventShape
        );
        AddMembers(
            members,
            contractType
                .GetMethods(_declaredPublic)
                .Where(method => !Accessor(method) && IsCanonical(method)),
            "method",
            MethodName,
            MethodShape
        );
        return members.ToImmutable();
    }

    public static ImmutableArray<PluginAuthoringSemanticOmission> FindOmissions(
        string reference,
        PluginAuthoringContract? contract = null
    )
    {
        ArgumentNullException.ThrowIfNull(reference);
        var source = contract ?? PluginAuthoringContract.Current;
        return source
            .PublicContractTypes.SelectMany(type => Members(type))
            .Where(member => !reference.Contains(MarkdownRow(member), StringComparison.Ordinal))
            .Select(member => new PluginAuthoringSemanticOmission(member.CanonicalName))
            .Distinct()
            .ToImmutableArray();
    }

    internal static string MarkdownRow(PluginAuthoringSemanticMember member) =>
        $"| {member.Kind} | `{member.CanonicalName}` | {member.Shape} |";

    private static void AddMembers<TMember>(
        ImmutableArray<PluginAuthoringSemanticMember>.Builder destination,
        IEnumerable<TMember> members,
        string kind,
        Func<TMember, string> name,
        Func<TMember, string> shape
    )
    {
        foreach (var member in members.OrderBy(name, StringComparer.Ordinal))
        {
            destination.Add(new(kind, name(member), shape(member)));
        }
    }

    private static string ConstructorName(ConstructorInfo constructor) =>
        $"{TypeName(constructor.DeclaringType!)}({ParameterTypes(constructor.GetParameters())})";

    private static string ConstructorShape(ConstructorInfo constructor) =>
        Parameters(constructor.GetParameters());

    private static string FieldName(FieldInfo field) =>
        $"{TypeName(field.DeclaringType!)}.{field.Name}";

    private static string FieldShape(FieldInfo field)
    {
        var modifiers =
            field.IsLiteral ? "const "
            : field.IsStatic ? "static "
            : string.Empty;
        var value = field.IsLiteral
            ? $" = {Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}"
            : string.Empty;
        return $"{modifiers}{FriendlyName(field.FieldType)}{value}";
    }

    private static string PropertyName(PropertyInfo property)
    {
        var index = property.GetIndexParameters();
        return index.Length == 0
            ? $"{TypeName(property.DeclaringType!)}.{property.Name}"
            : $"{TypeName(property.DeclaringType!)}.{property.Name}[{ParameterTypes(index)}]";
    }

    private static string PropertyShape(PropertyInfo property)
    {
        var accessors = string.Join(
            " ",
            new[]
            {
                property.GetMethod is null ? null : "get;",
                property.SetMethod is null ? null : "set;",
            }.OfType<string>()
        );
        return $"{FriendlyName(property.PropertyType)} {{ {accessors} }}";
    }

    private static string EventName(EventInfo eventInfo) =>
        $"{TypeName(eventInfo.DeclaringType!)}.{eventInfo.Name}";

    private static string EventShape(EventInfo eventInfo) =>
        FriendlyName(eventInfo.EventHandlerType!);

    private static string MethodName(MethodInfo method)
    {
        var generic = method.IsGenericMethodDefinition
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;
        return $"{TypeName(method.DeclaringType!)}.{method.Name}{generic}({ParameterTypes(method.GetParameters())})";
    }

    private static string MethodShape(MethodInfo method) =>
        $"{FriendlyName(method.ReturnType)} ({Parameters(method.GetParameters())})";

    private static string ParameterTypes(IEnumerable<ParameterInfo> parameters) =>
        string.Join(", ", parameters.Select(ParameterType));

    private static string Parameters(IEnumerable<ParameterInfo> parameters) =>
        string.Join(
            ", ",
            parameters.Select(parameter => $"{ParameterType(parameter)} {parameter.Name}")
        );

    private static string ParameterType(ParameterInfo parameter)
    {
        var modifier =
            parameter.IsOut ? "out "
            : parameter.ParameterType.IsByRef ? "ref "
            : parameter.GetCustomAttribute<ParamArrayAttribute>() is not null ? "params "
            : string.Empty;
        var type = parameter.ParameterType.IsByRef
            ? parameter.ParameterType.GetElementType()!
            : parameter.ParameterType;
        return $"{modifier}{FriendlyName(type)}";
    }

    private static string TypeShape(Type type)
    {
        var kind =
            type.IsInterface ? "interface"
            : type.IsEnum ? "enum"
            : type.IsValueType ? "struct"
            : type.IsAbstract && type.IsSealed ? "static class"
            : type.IsAbstract ? "abstract class"
            : type.IsSealed ? "sealed class"
            : "class";
        var bases = type.GetInterfaces()
            .Where(candidate => candidate.IsPublic || candidate.IsNestedPublic)
            .Select(FriendlyName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return bases.Length == 0 ? kind : $"{kind}; implements {string.Join(", ", bases)}";
    }

    private static bool IsCanonical(MemberInfo member) =>
        member.GetCustomAttribute<CompilerGeneratedAttribute>() is null;

    private static bool Accessor(MethodInfo method) =>
        method.IsSpecialName
        && (
            method.Name.StartsWith("get_", StringComparison.Ordinal)
            || method.Name.StartsWith("set_", StringComparison.Ordinal)
            || method.Name.StartsWith("add_", StringComparison.Ordinal)
            || method.Name.StartsWith("remove_", StringComparison.Ordinal)
        );

    private static string FriendlyName(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return $"{FriendlyName(nullable)}?";
        }

        if (type.IsArray)
        {
            return $"{FriendlyName(type.GetElementType()!)}[]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (!type.IsGenericType)
        {
            return TypeName(type);
        }

        var name = TypeName(type.GetGenericTypeDefinition());
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}>";
    }

    private static string TypeName(Type type)
    {
        var ownName = type.Name.Split('`', 2)[0];
        return type.DeclaringType is not null ? $"{TypeName(type.DeclaringType)}.{ownName}"
            : string.IsNullOrWhiteSpace(type.Namespace) ? ownName
            : $"{type.Namespace}.{ownName}";
    }
}
