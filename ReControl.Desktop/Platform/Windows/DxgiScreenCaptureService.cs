using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Windows screen capture via DXGI Desktop Duplication (Phase 42.1 Fix A).
///
/// The legacy <see cref="WindowsScreenCaptureService"/> uses GDI BitBlt, which forces a
/// full GPU→CPU readback every frame (~29ms on real hardware per 42.1-FINDINGS, even at
/// ~5% CPU — it blocks on DWM, not compute). Desktop Duplication acquires the already-
/// composited desktop surface on the GPU and only copies a CPU-readable staging texture,
/// which is typically several times faster on real hardware.
///
/// NOTE: Desktop Duplication is unavailable on most headless/VM display adapters
/// (DuplicateOutput throws). Construction failure is expected there — the DI factory
/// falls back to GDI. This class never weakens the existing path; it is purely additive.
///
/// Output is B8G8R8A8 (BGRA), matching the contract of <see cref="IScreenCaptureService"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DxgiScreenCaptureService : IScreenCaptureService
{
    private readonly LogService _log;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutput1 _output1;
    private IDXGIOutputDuplication _duplication;
    private ID3D11Texture2D _staging;

    // Last good frame, so CaptureFrame can return current screen contents on a
    // no-change AcquireNextFrame timeout (matching GDI BitBlt semantics, which
    // always returns the full current screen).
    private readonly byte[] _cache;
    private bool _hasCache;
    private bool _disposed;

    private const int AcquireTimeoutMs = 15;

    public int Width { get; }
    public int Height { get; }

    public DxgiScreenCaptureService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        FeatureLevel[] levels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        Result res = D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            levels,
            out _device!,
            out _context!);
        if (res.Failure || _device is null || _context is null)
            throw new InvalidOperationException($"DXGI: D3D11CreateDevice failed ({res})");

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        Result eo = adapter.EnumOutputs(0, out IDXGIOutput? output);
        if (eo.Failure || output is null)
            throw new InvalidOperationException($"DXGI: EnumOutputs(0) failed ({eo})");
        using (output)
        {
            _output1 = output.QueryInterface<IDXGIOutput1>();
        }

        var bounds = _output1.Description.DesktopCoordinates;
        Width = bounds.Right - bounds.Left;
        Height = bounds.Bottom - bounds.Top;
        if (Width <= 0 || Height <= 0)
            throw new InvalidOperationException($"DXGI: invalid output bounds {Width}x{Height}");

        _duplication = _output1.DuplicateOutput(_device);
        _staging = CreateStaging();
        _cache = new byte[Width * Height * 4];

        _log.Info($"DxgiScreenCapture: display {Width}x{Height} (Desktop Duplication)");
    }

    private ID3D11Texture2D CreateStaging()
    {
        var desc = new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };
        return _device.CreateTexture2D(desc);
    }

    public bool CaptureFrame(byte[] buffer)
    {
        if (_disposed) return false;
        int size = Width * Height * 4;
        if (buffer.Length < size) return false;

        Result acq = _duplication.AcquireNextFrame(AcquireTimeoutMs, out _, out IDXGIResource? desktopResource);

        if (acq == Vortice.DXGI.ResultCode.WaitTimeout)
        {
            // No screen change within the timeout — return the last good frame so
            // callers always see current screen contents (GDI parity).
            desktopResource?.Dispose();
            if (_hasCache) { Buffer.BlockCopy(_cache, 0, buffer, 0, size); return true; }
            return false;
        }

        if (acq.Failure || desktopResource is null)
        {
            desktopResource?.Dispose();
            if (acq == Vortice.DXGI.ResultCode.AccessLost) TryReinitDuplication();
            return _hasCache && CopyCache(buffer, size);
        }

        try
        {
            using (desktopResource)
            using (var tex = desktopResource.QueryInterface<ID3D11Texture2D>())
            {
                _context.CopyResource(_staging, tex);
            }
        }
        finally
        {
            _duplication.ReleaseFrame();
        }

        MappedSubresource map = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowBytes = Width * 4;
            for (int y = 0; y < Height; y++)
            {
                IntPtr srcRow = IntPtr.Add(map.DataPointer, y * (int)map.RowPitch);
                Marshal.Copy(srcRow, buffer, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }

        Buffer.BlockCopy(buffer, 0, _cache, 0, size);
        _hasCache = true;
        return true;
    }

    private bool CopyCache(byte[] buffer, int size)
    {
        Buffer.BlockCopy(_cache, 0, buffer, 0, size);
        return true;
    }

    private void TryReinitDuplication()
    {
        try
        {
            _duplication?.Dispose();
            _duplication = _output1.DuplicateOutput(_device);
            _log.Info("DxgiScreenCapture: duplication reinitialized after access loss");
        }
        catch (Exception ex)
        {
            _log.Info($"DxgiScreenCapture: reinit failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _staging?.Dispose();
        _duplication?.Dispose();
        _output1?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }
}
