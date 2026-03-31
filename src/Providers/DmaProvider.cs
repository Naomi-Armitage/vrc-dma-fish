#pragma warning disable CA1416
using Vmmsharp;
using VrcDmaFish.Models;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

public sealed class DmaProvider : IFishSignalSource, IDisposable
{
    private Vmm? _vmm;
    private uint _pid;
    private ulong _targetObjectAddr;
    private readonly string _processName;

    public DmaProvider(string processName) {
        _processName = processName;
        Initialize();
    }

    private void Initialize() {
        try {
            _vmm = new Vmm("-device", "fpga");
            // 修正函数名为 ProcessGetPidFromName
            if (_vmm.ProcessGetPidFromName(_processName, out _pid)) {
                var scanner = new UnityScanner(_vmm, _pid);
                _targetObjectAddr = scanner.FindObjectByName("FishingLogic");
            }
        } catch { }
    }

    public FishContext Read() {
        if (_vmm == null || _targetObjectAddr == 0) return new FishContext();

        try {
            byte[] buffer = new byte[4];
            uint cbRead;
            // 修正 MemRead 使用方式，先分配缓冲区
            bool success = _vmm.MemRead(_pid, (nint)(_targetObjectAddr + 0x40), buffer, out cbRead, Vmm.FLAG_NOCACHE);
            
            if (!success || cbRead != 4) return new FishContext();

            float tension = BitConverter.ToSingle(buffer, 0);
            return new FishContext {
                IsHooked = tension > 0.05f,
                Tension = tension
            };
        } catch {
            return new FishContext();
        }
    }

    public void ResetCycle() { }
    public void Dispose() => _vmm?.Dispose();
}
