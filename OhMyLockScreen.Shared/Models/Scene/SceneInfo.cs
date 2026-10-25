namespace OhMyLockScreen.Shared.Models.Scene;
/// <summary>与视图无关的场景摘要。每个视图单独加载自己的可编辑模型。</summary>
public sealed record SceneInfo(Guid Id, string Name);
public sealed class SceneChangedEventArgs(Guid sceneId, Exception? error = null) : EventArgs
{
    public Guid SceneId { get; } = sceneId;
    public Exception? Error { get; } = error;
}