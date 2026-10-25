using OhMyLockScreen.Shared.Models.UI;

namespace OhMyLockScreen.Helpers.UI;

/// <summary>按场景持有保存失败提示，恢复时只关闭对应场景的消息。</summary>
internal sealed class SceneSaveToastTracker
{
    private readonly Dictionary<Guid, ToastMessage> _messages = [];

    internal void Replace(Guid sceneId, ToastMessage message)
    {
        Close(sceneId);
        _messages[sceneId] = message;
    }

    internal void Close(Guid sceneId)
    {
        if (_messages.Remove(sceneId, out var message))
        {
            message.Close();
        }
    }

    internal void Clear()
    {
        foreach (var message in _messages.Values)
        {
            message.Close();
        }

        _messages.Clear();
    }
}
