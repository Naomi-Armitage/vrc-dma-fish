using System.Runtime.Versioning;
using Vmmsharp;
using VrcDmaFish.Models;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

[SupportedOSPlatform("windows")]
public sealed class DmaProvider : IFishSignalSource, IDisposable
{
    private readonly SignalSourceConfig _config;
    private Vmm? _vmm;
    private VmmProcess? _process;
    private ulong _targetObjectAddr;
    private SignalOffsets _offsets;

    public DmaProvider(SignalSourceConfig config)
    {
        _config = config;
        Initialize();
    }

    public bool IsReady { get; private set; }

    private void Initialize()
    {
        try
        {
            var nativeLibraryPath = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(nativeLibraryPath, "vmm.dll")))
            {
                Vmm.LoadNativeLibrary(nativeLibraryPath);
            }

            _vmm = new Vmm("-device", "fpga");
            _process = _vmm.GetProcessByName(_config.ProcessName);

            if (_process is null || !_process.IsValid)
            {
                Logger.Warn("DMA", $"Could not find process '{_config.ProcessName}'.");
                return;
            }

            Logger.Info("DMA", $"Connected to {_process.Name} (PID: {_process.PID}).");

            if (!_config.TryGetTargetObjectAddress(out _targetObjectAddr))
            {
                var scanner = new UnityScanner(_process);
                _targetObjectAddr = scanner.FindObjectByName(_config.TargetObjectName);
            }

            if (_targetObjectAddr == 0)
            {
                Logger.Warn("DMA", "Target object address is missing. Set SignalSource.TargetObjectAddress to enable DMA reads.");
                return;
            }

            if (!_config.TryGetSignalOffsets(out _offsets))
            {
                Logger.Warn("DMA", "Signal offsets are missing. Set HookedOffset, CatchCompletedOffset, and TensionOffset to enable DMA reads.");
                return;
            }

            IsReady = true;
        }
        catch (Exception ex)
        {
            Logger.Error("DMA", $"Initialization failed: {ex.Message}");
        }
    }

    public FishContext Read()
    {
        if (!IsReady || _process is null)
        {
            return new FishContext();
        }

        try
        {
            return new FishContext
            {
                IsHooked = ReadValue<byte>(_targetObjectAddr + _offsets.HookedOffset) != 0,
                CatchCompleted = ReadValue<byte>(_targetObjectAddr + _offsets.CatchCompletedOffset) != 0,
                Tension = Math.Clamp(ReadValue<float>(_targetObjectAddr + _offsets.TensionOffset), 0f, 1f),
            };
        }
        catch (Exception ex)
        {
            Logger.Error("DMA", $"Read failed: {ex.Message}");
            IsReady = false;
            return new FishContext();
        }
    }

    public void ResetCycle()
    {
    }

    public void Dispose()
    {
        IsReady = false;
        _vmm?.Dispose();
    }

    private T ReadValue<T>(ulong address) where T : unmanaged
    {
        if (_process is null)
        {
            throw new InvalidOperationException("DMA process is not initialized.");
        }

        return _process.MemReadAs<T>(address, Vmm.FLAG_NOCACHE)
            ?? throw new InvalidOperationException($"Failed to read {typeof(T).Name} at 0x{address:X}.");
    }
}
