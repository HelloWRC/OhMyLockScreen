namespace OhMyLockScreen.Shared.Attributes.Serialization;

[AttributeUsage(AttributeTargets.Class)]
public class AutoJsonTypeNameAttribute(string name): Attribute
{
    public string Name { get; } = name;
}