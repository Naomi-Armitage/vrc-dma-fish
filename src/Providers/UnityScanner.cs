using vmmsharp;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

public sealed class UnityScanner
{
    private readonly Vmm _vmm;
    private readonly uint _pid;
    private ulong _unityPlayerBase;

    public UnityScanner(Vmm vmm, uint pid)
    {
        _vmm = vmm;
        _pid = pid;
        _unityPlayerBase = _vmm.ProcessGetModuleBase(_pid, "UnityPlayer.dll");
    }

    /// <summary>
    /// 通过特征码定位 Unity 的 GameObjectManager (GOM)
    /// </summary>
    public ulong FindGameObjectManager()
    {
        // 借鉴自 Unity 常用特征码：48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 40 ?? 48 85 C0
        // 这段指令通常在 UnityPlayer.dll 里用来获取全局 GOM 实例喵
        string pattern = "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 40 ?? 48 85 C0";
        var results = _vmm.SearchScan(_pid, pattern, _unityPlayerBase, 0x10000000); // 扫描前 256MB

        if (results.Length == 0) return 0;

        // 解析 RIP 相对地址 (Instruction + Offset)
        var instrAddr = results[0];
        uint relativeOffset = BitConverter.ToUInt32(_vmm.MemRead(_pid, instrAddr + 3, 4, Vmm.FLAG_NOCACHE), 0);
        return instrAddr + 7 + relativeOffset;
    }

    /// <summary>
    /// 在内存里爬树，按名字找 GameObject 基址
    /// </summary>
    public ulong FindObjectByName(string name)
    {
        ulong gomAddr = FindGameObjectManager();
        if (gomAddr == 0) return 0;

        Logger.Info("SCAN", $"找到 GOM 基址: 0x{gomAddr:X}");

        // Unity GOM 结构：
        // [GOM + 0x8] -> LastActiveNode
        // [GOM + 0x10] -> ActiveNodes (链表头)
        ulong currentNode = BitConverter.ToUInt64(_vmm.MemRead(_pid, gomAddr + 0x10, 8, Vmm.FLAG_NOCACHE), 0);
        
        int limit = 2048; // 防止死循环，扫描前 2048 个对象
        while (currentNode != 0 && limit-- > 0)
        {
            // [Node + 0x10] -> GameObject 指针
            ulong gameObject = BitConverter.ToUInt64(_vmm.MemRead(_pid, currentNode + 0x10, 8, Vmm.FLAG_NOCACHE), 0);
            if (gameObject != 0)
            {
                // [GameObject + 0x30] -> Name 指针 (Unity 默认偏移)
                ulong namePtr = BitConverter.ToUInt64(_vmm.MemRead(_pid, gameObject + 0x30, 8, Vmm.FLAG_NOCACHE), 0);
                string objName = _vmm.MemReadString(_pid, namePtr, 64);

                if (objName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("SCAN", $"[★] 发现匹配对象: {objName} @ 0x{gameObject:X}");
                    return gameObject;
                }
            }
            // [Node + 0x8] -> NextNode 指针
            currentNode = BitConverter.ToUInt64(_vmm.MemRead(_pid, currentNode + 0x8, 8, Vmm.FLAG_NOCACHE), 0);
        }

        return 0;
    }
}
