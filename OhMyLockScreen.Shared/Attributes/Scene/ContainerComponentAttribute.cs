namespace OhMyLockScreen.Shared.Attributes.Scene;

/// <summary>标记可以通过自己的设置承载子组件的组件。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ContainerComponentAttribute : Attribute;
