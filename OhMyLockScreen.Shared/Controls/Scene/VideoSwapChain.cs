using System.Globalization;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Device = SharpDX.Direct3D11.Device;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>Owns the native rendering resources for one video background.</summary>
internal sealed class VideoSwapChain : IDisposable
{
    // VLC's WinRT output reads its render dimensions from these DXGI private-data keys.
    private static readonly Guid WidthKey = new("f1b59347-1643-411a-ad6b-c780177a06b6");
    private static readonly Guid HeightKey = new("6ea976a0-9d60-4bb7-a5a9-7dd1187fc9bd");

    private Device? _device;
    private DeviceContext? _context;
    private SwapChain1? _swapChain;

    internal SwapChain1 SwapChain => _swapChain ?? throw new ObjectDisposedException(nameof(VideoSwapChain));

    internal string[] Options =>
    [
        string.Create(CultureInfo.InvariantCulture, $"--winrt-d3dcontext=0x{_context!.NativePointer:x}"),
        string.Create(CultureInfo.InvariantCulture, $"--winrt-swapchain=0x{SwapChain.NativePointer:x}")
    ];

    internal VideoSwapChain(int width, int height, double scaleX, double scaleY)
    {
        try
        {
            _device = CreateDevice();
            _context = _device.ImmediateContext;
            using var factory = new Factory2();
            using (var dxgiDevice = _device.QueryInterface<SharpDX.DXGI.Device1>())
            {
                dxgiDevice.MaximumFrameLatency = 1;
            }

            var description = new SwapChainDescription1
            {
                Width = width,
                Height = height,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = Usage.RenderTargetOutput,
                BufferCount = 2,
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Unspecified
            };
            _swapChain = new SwapChain1(factory, _device, ref description);
            UpdateSize(width, height, scaleX, scaleY);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static Device CreateDevice()
    {
        try
        {
            return new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport);
        }
        catch (SharpDXException)
        {
            try
            {
                return new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            }
            catch (SharpDXException)
            {
                return new Device(DriverType.Warp, DeviceCreationFlags.BgraSupport);
            }
        }
    }

    internal void UpdateSize(int width, int height, double scaleX, double scaleY)
    {
        using (var chain = SwapChain.QueryInterface<SwapChain2>())
        {
            chain.MatrixTransform = new RawMatrix3x2 { M11 = (float)(1 / scaleX), M22 = (float)(1 / scaleY) };
        }

        var value = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(value, width);
            SwapChain.SetPrivateData(WidthKey, sizeof(int), value);
            Marshal.WriteInt32(value, height);
            SwapChain.SetPrivateData(HeightKey, sizeof(int), value);
        }
        finally
        {
            Marshal.FreeHGlobal(value);
        }
    }

    internal void Trim()
    {
        if (_device != null)
        {
            using var device = _device.QueryInterface<SharpDX.DXGI.Device3>();
            device.Trim();
        }
    }

    public void Dispose()
    {
        // The caller must stop VLC and detach the panel before releasing the swapchain.
        _swapChain?.Dispose();
        _swapChain = null;
        try
        {
            // Release bound textures and submit deferred destruction before disposing the device.
            _context?.ClearState();
            _context?.Flush();
        }
        finally
        {
            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
        }
    }
}
