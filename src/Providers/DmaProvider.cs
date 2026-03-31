using vmmsharp;
using VrcDmaFish.Models;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

public sealed class DmaProvider : IFishSignalSource, IDisposable
{
    private Vmm? _vmm;
    private uint _pid;
    private readonly string _processName;

    // 预设一些常见的 VRChat 偏移路径模板喵
    private const ulong OFFSET_UDON_MANAGER = 0x1234567; // 示例偏移
    
    public DmaProvider(string processName)
    {
        _processName = processName;
        try {
            // 初始化 DMA 硬件 (默认使用 FPGA 模式)
            _vmm = new Vmm("-device", "fpga");
            if (_vmm.PidGetFromName(_processName, out _pid)) {
                Logger.Info("DMA", $"已成功连接到 {_processName} (PID: {_pid}) 喵！");
            } else {
                Logger.Warn("DMA", $"等待进程 {_processName} 启动中...");
            }
        } catch (Exception ex) {
            Logger.Error("DMA", $"驱动初始化失败: {ex.Message}。主人记得在副机上放好 vmm.dll 喵！");
        }
    }

    public FishContext Read()
    {
        if (_vmm == null || _pid == 0) return new FishContext();

        // 这里的逻辑是供主人以后填写的“黄金代码”喵：
        // var baseAddr = _vmm.ProcessGetModuleBase(_pid, "GameAssembly.dll");
        // byte[] buffer = _vmm.MemRead(_pid, baseAddr + OFFSET_UDON_MANAGER, 4);
        
        return new FishContext {
            IsHooked = false, // 主人在这里读内存判断喵
            Tension = 0.0f    // 主人在这里读内存拿张力喵
        };
    }

    public void ResetCycle() { }

    public void Dispose()
    {
        _vmm?.Dispose();
    }
}
