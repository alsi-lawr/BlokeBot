using System.Collections.Immutable;
using System.Reflection;

namespace BlokeBot.Plugins.Contracts.Testing;

public sealed record PluginAuthoringSemanticMember(string Kind, string CanonicalName, string Shape);

public sealed record PluginAuthoringSemanticOmission(string CanonicalName);

public static class PluginAuthoringSemanticCoverage
{
    public static ImmutableArray<PluginAuthoringSemanticMember> Members(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        var members = ImmutableArray.CreateBuilder<PluginAuthoringSemanticMember>();
        members.Add(new("type", TypeName(contractType), Properties(contractType)));
        if (contractType.IsEnum)
        {
            foreach (var name in Enum.GetNames(contractType))
            {
                members.Add(new("value", $"{TypeName(contractType)}.{name}", string.Empty));
            }
        }
        else
        {
            foreach (
                var variant in contractType
                    .GetNestedTypes(BindingFlags.Public)
                    .Where(contractType.IsAssignableFrom)
                    .OrderBy(TypeName, StringComparer.Ordinal)
            )
            {
                members.Add(new("outcome", TypeName(variant), Properties(variant)));
                foreach (
                    var property in variant
                        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .OrderBy(property => property.Name, StringComparer.Ordinal)
                )
                {
                    members.Add(
                        new(
                            "field",
                            $"{TypeName(variant)}.{property.Name}",
                            FriendlyName(property.PropertyType)
                        )
                    );
                }
            }

            foreach (
                var property in contractType
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
            )
            {
                members.Add(
                    new(
                        "field",
                        $"{TypeName(contractType)}.{property.Name}",
                        FriendlyName(property.PropertyType)
                    )
                );
            }
        }

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
            .SemanticSurfaces.SelectMany(surface => Members(surface.ContractType))
            .Where(member =>
                !reference.Contains($"`{member.CanonicalName}`", StringComparison.Ordinal)
            )
            .Select(member => new PluginAuthoringSemanticOmission(member.CanonicalName))
            .ToImmutableArray();
    }

    private static string Properties(Type type) =>
        string.Join(
            ", ",
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}: {FriendlyName(property.PropertyType)}")
        );

    private static string FriendlyName(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return $"{FriendlyName(nullable)}?";
        }

        if (!type.IsGenericType)
        {
            return TypeName(type);
        }

        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}>";
    }

    private static string TypeName(Type type) =>
        type.DeclaringType is null ? type.Name : $"{TypeName(type.DeclaringType)}.{type.Name}";
}
