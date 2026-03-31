using vmmsharp;
using VrcDmaFish.Models;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

public sealed class DmaProvider : IFishSignalSource, IDisposable
{
    private Vmm? _vmm;
    private uint _pid;
    private ulong _targetObjectAddr;
    private readonly string _processName;

    public DmaProvider(string processName)
    {
        _processName = processName;
        Initialize();
    }

    private void Initialize()
    {
        try {
            _vmm = new Vmm("-device", "fpga");
            if (_vmm.PidGetFromName(_processName, out _pid)) {
                Logger.Info("DMA", $"已连接到 {_processName} (PID: {_pid})");
                
                // 启动自动导航雷达！
                var scanner = new UnityScanner(_vmm, _pid);
                _targetObjectAddr = scanner.FindObjectByName("FishingLogic"); // 默认找钓鱼逻辑对象
                
                if (_targetObjectAddr == 0) {
                    Logger.Warn("DMA", "未能自动定位到钓鱼对象，可能需要进入钓鱼区域喵！");
                }
            }
        } catch (Exception ex) {
            Logger.Error("DMA", $"初始化失败: {ex.Message}");
        }
    }

    public FishContext Read()
    {
        if (_vmm == null || _pid == 0 || _targetObjectAddr == 0) return new FishContext();

        // 到这一步，主人只需要根据 _targetObjectAddr 往里偏移去读 Udon 变量就行了喵！
        // 比如: [targetObjectAddr + ComponentOffset] -> [UdonBehavior] -> [PublicVariables]
        
        return new FishContext {
            IsHooked = false, // 待主人补全读取逻辑喵
            Tension = 0.0f
        };
    }

    public void ResetCycle() { }
    public void Dispose() => _vmm?.Dispose();
}
