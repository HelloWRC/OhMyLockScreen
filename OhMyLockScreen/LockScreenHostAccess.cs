using Windows.ApplicationModel.LockScreen;

namespace OhMyLockScreen
{
    internal static partial class LockScreenHostAccess
    {
        internal static LockApplicationHost? GetForCurrentView()
        {
            var host = LockApplicationHost.GetForCurrentView();
            if (host != null)
            {
                return host;
            }

            // The public factory can cache an unavailable private broker and then
            // return S_OK/null indefinitely. Request the same broker directly.
            // Its CreateLockApplicationHost still validates AppContainer identity
            // and whether the calling CoreWindow belongs to the lock host.
            return CreateThroughBroker();
        }

        private static partial LockApplicationHost? CreateThroughBroker();
    }
}
