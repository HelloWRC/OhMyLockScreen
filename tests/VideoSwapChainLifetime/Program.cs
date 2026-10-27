using System.Globalization;
using System.Runtime.InteropServices;
using OhMyLockScreen.Shared.Controls.Scene;
using SharpDX;
using SharpDX.Diagnostics;

Configuration.EnableObjectTracking = true;
var baseline = ObjectTracker.FindActiveObjects().Count;
// Protocol constants from VLC modules/video_output/win32/direct3d11.c; keep independent of production strings.
var widthKey = new Guid(0xf1b59347, 0x1643, 0x411a, 0xad, 0x6b, 0xc7, 0x80, 0x17, 0x7a, 0x06, 0xb6);
var heightKey = new Guid(0x6ea976a0, 0x9d60, 0x4bb7, 0xa5, 0xa9, 0x7d, 0xd1, 0x18, 0x7f, 0xc9, 0xbd);

for (var cycle = 0; cycle < 50; cycle++)
{
    var surface = new VideoSwapChain(1920, 1080, 1.5, 1.5);
    var options = surface.Options;
    var contextPointer = ReadPointer(options[0]);
    Check(ReadPointer(options[1]) == surface.SwapChain.NativePointer, "VLC received the wrong swapchain pointer.");
    var devicePointer = GetDevice(contextPointer);
    try
    {
        Check(ReadSize(surface, widthKey) == 1920 && ReadSize(surface, heightKey) == 1080,
            "VLC cannot read the initial render dimensions using its native protocol keys.");
        BindRenderTarget(surface, devicePointer, contextPointer);
        surface.UpdateSize(1600, 900, 1.25, 1.25);
        Check(ReadSize(surface, widthKey) == 1600,
            "VLC cannot read the updated render width.");
        Check(ReadSize(surface, heightKey) == 900,
            "VLC cannot read the updated render height.");
        Check(surface.Options.SequenceEqual(options), "Reading options or resizing replaced native resources.");
        surface.Trim();
    }
    finally
    {
        surface.Dispose();
        surface.Dispose();
    }

    // GetDevice holds one diagnostic reference. No swapchain/context reference may survive disposal.
    var remaining = Marshal.Release(devicePointer);
    Check(remaining == 0, $"Cycle {cycle}: D3D device still has {remaining} references after disposal.");
    Check(ObjectTracker.FindActiveObjects().Count == baseline,
        $"Cycle {cycle}: SharpDX wrappers survived disposal.");
}

Console.WriteLine("PASS: 50 native D3D11 swapchain cycles released all device references and SharpDX wrappers.");

static IntPtr ReadPointer(string option)
    => (IntPtr)long.Parse(option[(option.IndexOf("0x", StringComparison.Ordinal) + 2)..], NumberStyles.HexNumber,
        CultureInfo.InvariantCulture);

static unsafe IntPtr GetDevice(IntPtr context)
{
    // ID3D11DeviceChild::GetDevice is the first method after IUnknown.
    var table = *(void***)context;
    var getDevice = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)table[3];
    IntPtr device;
    getDevice(context, &device);
    return device;
}

static void BindRenderTarget(VideoSwapChain surface, IntPtr devicePointer, IntPtr contextPointer)
{
    // Each wrapper owns an extra reference; the diagnostic device reference remains with the caller.
    Marshal.AddRef(devicePointer);
    using var device = new SharpDX.Direct3D11.Device(devicePointer);
    Marshal.AddRef(contextPointer);
    using var context = new SharpDX.Direct3D11.DeviceContext(contextPointer);
    using var texture = surface.SwapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0);
    using var target = new SharpDX.Direct3D11.RenderTargetView(device, texture);
    context.OutputMerger.SetRenderTargets(target);
    context.ClearRenderTargetView(target, new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 1));
    // The context still holds the render target when these local wrappers are disposed.
}

static int ReadSize(VideoSwapChain surface, Guid key)
{
    var value = Marshal.AllocHGlobal(sizeof(int));
    try
    {
        var length = sizeof(int);
        surface.SwapChain.GetPrivateData(key, ref length, value);
        return Marshal.ReadInt32(value);
    }
    finally
    {
        Marshal.FreeHGlobal(value);
    }
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
