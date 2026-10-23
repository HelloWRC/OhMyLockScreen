using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using OhMyLockScreen.Shared.Attributes.Serialization;

namespace OhMyLockScreen.Shared.Helpers.Serialization;

public static class AutoJsonPolymorphicRegistrationHelper
{
    private static HashSet<Type> DiscoveredBaseTypes { get; } = [];

    private static ConcurrentDictionary<Type, ConcurrentDictionary<string, Type>> TypeAlias { get; } = [];

    private static HashSet<Type> GetAllBaseTypes(Type type)
    {
        var result = new HashSet<Type>();
        var baseType = type.BaseType;
        while (baseType != null)
        {
            result.Add(baseType);
            baseType = baseType.BaseType;
        }

        return result;
    }

    internal static DefaultJsonTypeInfoResolver CreateJsonTypeInfoResolver()
    {
        return new DefaultJsonTypeInfoResolver()
        {
            Modifiers = { JsonTypeInfoResolverModifier }
        };
    }

    private static void JsonTypeInfoResolverModifier(JsonTypeInfo info)
    {
        if (!TypeAlias.TryGetValue(info.Type, out var alias))
        {
            return;
        }
        
        info.PolymorphismOptions ??= new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$type"
        };

        foreach (var (name, type) in alias)
        {
            info.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(type, name));
        }
    }

    public static void DiscoverAndRegisterForAssembly(Assembly assembly)
    {
        foreach (var type in assembly.DefinedTypes.Where(x => x.GetCustomAttribute<AutoJsonPolymorphicAttribute>() != null))
        {
            DiscoveredBaseTypes.Add(type);
        }

        var targetTypes = assembly.DefinedTypes
            .Where(x => GetAllBaseTypes(x).Any(DiscoveredBaseTypes.Contains))
            .ToDictionary(x => x.GetCustomAttribute<AutoJsonTypeNameAttribute>()?.Name ?? $"{x.FullName};assembly={x.Assembly.GetName().Name}");

        foreach (var (name, type) in targetTypes)
        {
            var baseType = GetAllBaseTypes(type).FirstOrDefault(DiscoveredBaseTypes.Contains);
            if (baseType == null)
            {
                continue;
            }

            var alias = TypeAlias.TryGetValue(baseType, out var a) ? a : [];
            alias.TryAdd(name, type);
            TypeAlias.TryAdd(baseType, alias);
        }

        ConfigureFileHelper.InvalidatesSerializerOptions();
    }
}