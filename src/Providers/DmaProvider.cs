using vmmsharp;
using VrcDmaFish.Models;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

public sealed class DmaProvider : IFishSignalSource, IDisposable
{
    private Vmm? _vmm;
    private uint _pid;
    private readonly string _processName;

    // --- 这里的 Offset 主人以后直接填在这里喵！ ---
    private const ulong OFFSET_GAMEOBJECT_MANAGER = 0x0; // 填 CE 找出来的 Offset 喵
    
    public DmaProvider(string processName)
    {
        _processName = processName;
        try {
            // 初始化 DMA 硬件 (主人记得插好硬件喵！)
            _vmm = new Vmm("-device", "fpga");
            if (_vmm.PidGetFromName(_processName, out _pid)) {
                Logger.Info("DMA", $"已成功连接到 {_processName} (PID: {_pid}) (∠・ω< )⌒★");
            } else {
                Logger.Warn("DMA", $"等待进程 {_processName} 启动中... 记得先开 VRChat 喵！");
            }
        } catch (Exception ex) {
            Logger.Error("DMA", $"驱动初始化失败: {ex.Message}。主人确认一下副机有没有装好 vmm.dll 喵！");
        }
    }

    public FishContext Read()
    {
        if (_vmm == null || _pid == 0) return new FishContext();

        // --- 核心读取逻辑 (主人以后在这里填空喵！) ---
        // 示例用法: 
        // var assemblyBase = _vmm.ProcessGetModuleBase(_pid, "GameAssembly.dll");
        // byte[] buffer = _vmm.MemRead(_pid, assemblyBase + 0x123456, 4);

        return new FishContext {
            IsHooked = false, // 填入判断逻辑喵
            Tension = 0.0f    // 填入数值逻辑喵
        };
    }

    public void ResetCycle() { }

    public void Dispose() => _vmm?.Dispose();
}
