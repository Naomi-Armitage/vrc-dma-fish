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
                Logger.Info("DMA", $"Connected to {_processName} (PID: {_pid})");
                var scanner = new UnityScanner(_vmm, _pid);
                _targetObjectAddr = scanner.FindObjectByName("FishingLogic");
            }
        } catch (Exception ex) {
            Logger.Error("DMA", $"Init failed: {ex.Message}");
        }
    }

    public FishContext Read() {
        if (_vmm == null || _targetObjectAddr == 0) return new FishContext();

        // 此处需要主人根据 Il2CppDumper 得到的偏移来填空喵
        // 目前先实现一个“如果雷达搜到对象，就尝试读取”的逻辑
        try {
            // 假设张力在 UdonBehaviour 偏移 0x40 处 (示例)
            byte[] buffer = _vmm.MemRead(_pid, _targetObjectAddr + 0x40, 4, Vmm.FLAG_NOCACHE);
            float tension = BitConverter.ToSingle(buffer, 0);

            return new FishContext {
                IsHooked = tension > 0.01f, // 只要有张力就视为上钩 (示例逻辑)
                Tension = tension
            };
        } catch {
            return new FishContext();
        }
    }

    public void ResetCycle() { }
    public void Dispose() => _vmm?.Dispose();
}
