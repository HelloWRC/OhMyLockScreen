using System;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.LockScreen;

namespace OhMyLockScreen
{
    internal static unsafe partial class LockScreenHostAccess
    {
        private static partial LockApplicationHost? CreateThroughBroker()
        {
            const string className = "lockframework.LockFrameworkBroker";
            var interfaceId = new Guid("28FE486D-6853-4C62-81AB-4BDC6B9C1A9D");
            nint runtimeClass = 0;
            nint factory = 0;
            nint host = 0;
            try
            {
                fixed (char* classNamePointer = className)
                {
                    ThrowIfFailed(WindowsCreateString(classNamePointer, (uint)className.Length, &runtimeClass),
                        "WindowsCreateString");
                }

                ThrowIfFailed(RoGetActivationFactory(runtimeClass, &interfaceId, &factory),
                    "激活 LockFrameworkBroker");
                if (factory == 0)
                {
                    throw new COMException("LockFrameworkBroker 返回空工厂。", unchecked((int)0x80004005));
                }

                // ABI checked in Windows.ApplicationModel.LockScreen.dll 10.0.26100.9549.
                // ILockFrameworkBrokerStatics derives from IInspectable: slot 6
                // is CreateLockApplicationHost(out ILockApplicationHost*).
                // Querying the exact IID prevents using a different interface ABI.
                var createHost = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)factory)[6];
                ThrowIfFailed(createHost(factory, &host), "LockFrameworkBroker.CreateLockApplicationHost");
                return host == 0 ? null : WinRT.MarshalInspectable<LockApplicationHost>.FromAbi(host);
            }
            finally
            {
                Release(host);
                Release(factory);
                if (runtimeClass != 0)
                {
                    WindowsDeleteString(runtimeClass);
                }
            }
        }

        private static void ThrowIfFailed(int result, string operation)
        {
            if (result < 0)
            {
                throw new COMException($"{operation} 失败。", result);
            }
        }

        private static void Release(nint instance)
        {
            if (instance != 0)
            {
                var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2];
                release(instance);
            }
        }

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsCreateString(char* source, uint length, nint* value);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsDeleteString(nint value);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoGetActivationFactory(nint className, Guid* interfaceId, nint* factory);
    }
}
