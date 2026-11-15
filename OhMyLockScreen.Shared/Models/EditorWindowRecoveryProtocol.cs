namespace OhMyLockScreen.Shared.Models;

internal static class EditorWindowRecoveryProtocol
{
    internal const string DirectoryName = "EditorWindowRecovery";
    internal const string RequestExtension = ".request";
    internal const string LaunchRequestName = "relaunch.requested";
    internal const string LaunchReadyName = "relaunch.ready";
    internal static readonly TimeSpan LaunchRequestLifetime = TimeSpan.FromSeconds(30);
}
