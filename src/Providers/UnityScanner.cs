using Vmmsharp;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

public sealed class UnityScanner
{
    private readonly Vmm _vmm;
    private readonly uint _pid;
    private nint _unityPlayerBase;

    public UnityScanner(Vmm vmm, uint pid)
    {
        _vmm = vmm;
        _pid = pid;
        // Map_GetModuleFromName is a common alternative if ProcessGetModuleBase is missing
        _unityPlayerBase = (nint)_vmm.ProcessGetModuleBase(_pid, "UnityPlayer.dll");
    }

    public ulong FindGameObjectManager()
    {
        // Use standard Vmmsharp Search logic
        string pattern = "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 40 ?? 48 85 C0";
        // SearchScan might be an extension or have a different signature, using Search as alternative
        var results = _vmm.SearchScan(_pid, pattern, (ulong)_unityPlayerBase, 0x10000000);

        if (results == null || results.Length == 0) return 0;

        var instrAddr = results[0];
        uint cbRead;
        byte[] buffer = _vmm.MemRead(_pid, (nint)(instrAddr + 3), 4, out cbRead, Vmm.FLAG_NOCACHE);
        
        if (cbRead != 4) return 0;
        uint relativeOffset = BitConverter.ToUInt32(buffer, 0);
        return instrAddr + 7 + relativeOffset;
    }

    public ulong FindObjectByName(string name)
    {
        ulong gomAddr = FindGameObjectManager();
        if (gomAddr == 0) return 0;

        uint cbRead;
        byte[] buffer = _vmm.MemRead(_pid, (nint)(gomAddr + 0x10), 8, out cbRead, Vmm.FLAG_NOCACHE);
        if (cbRead != 8) return 0;
        
        ulong currentNode = BitConverter.ToUInt64(buffer, 0);
        int limit = 1024;
        
        while (currentNode != 0 && limit-- > 0)
        {
            byte[] nodeBuffer = _vmm.MemRead(_pid, (nint)(currentNode + 0x10), 8, out cbRead, Vmm.FLAG_NOCACHE);
            ulong gameObject = cbRead == 8 ? BitConverter.ToUInt64(nodeBuffer, 0) : 0;
            
            if (gameObject != 0)
            {
                byte[] namePtrBuffer = _vmm.MemRead(_pid, (nint)(gameObject + 0x30), 8, out cbRead, Vmm.FLAG_NOCACHE);
                ulong namePtr = cbRead == 8 ? BitConverter.ToUInt64(namePtrBuffer, 0) : 0;
                
                if (namePtr != 0)
                {
                    // MemReadStringU for UTF8/ASCII string reading
                    string objName = _vmm.MemReadStringU(_pid, (nint)namePtr, 64);
                    if (!string.IsNullOrEmpty(objName) && objName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return gameObject;
                    }
                }
            }
            
            byte[] nextBuffer = _vmm.MemRead(_pid, (nint)(currentNode + 0x8), 8, out cbRead, Vmm.FLAG_NOCACHE);
            currentNode = cbRead == 8 ? BitConverter.ToUInt64(nextBuffer, 0) : 0;
        }

        return 0;
    }
}
