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
            if (_vmm.PidGetFromName(_processName, out _pid)) {
                var scanner = new UnityScanner(_vmm, _pid);
                _targetObjectAddr = scanner.FindObjectByName("FishingLogic");
            }
        } catch { }
    }

    public FishContext Read() {
        if (_vmm == null || _targetObjectAddr == 0) return new FishContext();

        try {
            uint cbRead;
            byte[] buffer = _vmm.MemRead(_pid, (nint)(_targetObjectAddr + 0x40), 4, out cbRead, Vmm.FLAG_NOCACHE);
            if (cbRead != 4) return new FishContext();

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
