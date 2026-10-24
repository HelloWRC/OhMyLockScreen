using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using OhMyLockScreen.Shared.Attributes.Serialization;

namespace OhMyLockScreen.Shared.Helpers.Serialization;

public static class AutoJsonPolymorphicRegistrationHelper
{
    private static readonly object Sync = new();

    private static HashSet<Type> DiscoveredBaseTypes { get; } = [];

    private static ConcurrentDictionary<Type, ConcurrentDictionary<string, Type>> TypeAlias { get; } = [];

    private static HashSet<Type> GetAllBaseTypes(Type type)
    {
        var result = new HashSet<Type>(type.GetInterfaces());
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
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Sync)
        {
            foreach (var type in assembly.DefinedTypes.Where(x => x.GetCustomAttribute<AutoJsonPolymorphicAttribute>() != null))
            {
                DiscoveredBaseTypes.Add(type);
            }

            var targetTypes = assembly.DefinedTypes
                .Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters);

            foreach (var type in targetTypes)
            {
                var name = type.GetCustomAttribute<AutoJsonTypeNameAttribute>()?.Name
                    ?? $"{type.FullName};assembly={type.Assembly.GetName().Name}";
                foreach (var baseType in GetAllBaseTypes(type).Where(DiscoveredBaseTypes.Contains))
                {
                    var aliases = TypeAlias.GetOrAdd(baseType, _ => new ConcurrentDictionary<string, Type>());
                    var registeredType = aliases.GetOrAdd(name, type);
                    if (registeredType != type)
                    {
                        throw new InvalidOperationException($"类型别名 {name} 已用于 {registeredType.FullName}。");
                    }
                }
            }

            ConfigureFileHelper.InvalidatesSerializerOptions();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DiscoverAndRegisterForCurrentAssembly()
    {
        DiscoverAndRegisterForAssembly(Assembly.GetCallingAssembly());
    }
}
