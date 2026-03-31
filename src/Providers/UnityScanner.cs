#pragma warning disable CA1416
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
        // 修正为 Map_GetModuleFromName
        _unityPlayerBase = (nint)_vmm.ProcessGetModuleBase(_pid, "UnityPlayer.dll");
    }

    public ulong FindGameObjectManager()
    {
        string pattern = "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 40 ?? 48 85 C0";
        // 修正为 Search
        var results = _vmm.Search(_pid, pattern, (ulong)_unityPlayerBase, 0x10000000);

        if (results == null || results.Length == 0) return 0;

        var instrAddr = results[0];
        byte[] buffer = new byte[4];
        uint cbRead;
        bool success = _vmm.MemRead(_pid, (nint)(instrAddr + 3), buffer, out cbRead, Vmm.FLAG_NOCACHE);
        
        if (!success || cbRead != 4) return 0;
        uint relativeOffset = BitConverter.ToUInt32(buffer, 0);
        return instrAddr + 7 + relativeOffset;
    }

    public ulong FindObjectByName(string name)
    {
        ulong gomAddr = FindGameObjectManager();
        if (gomAddr == 0) return 0;

        byte[] buffer = new byte[8];
        uint cbRead;
        if (!_vmm.MemRead(_pid, (nint)(gomAddr + 0x10), buffer, out cbRead, Vmm.FLAG_NOCACHE)) return 0;
        
        ulong currentNode = BitConverter.ToUInt64(buffer, 0);
        int limit = 1024;
        
        while (currentNode != 0 && limit-- > 0)
        {
            byte[] nodeBuffer = new byte[8];
            if (_vmm.MemRead(_pid, (nint)(currentNode + 0x10), nodeBuffer, out cbRead, Vmm.FLAG_NOCACHE))
            {
                ulong gameObject = BitConverter.ToUInt64(nodeBuffer, 0);
                if (gameObject != 0)
                {
                    byte[] namePtrBuffer = new byte[8];
                    if (_vmm.MemRead(_pid, (nint)(gameObject + 0x30), namePtrBuffer, out cbRead, Vmm.FLAG_NOCACHE))
                    {
                        ulong namePtr = BitConverter.ToUInt64(namePtrBuffer, 0);
                        if (namePtr != 0)
                        {
                            // 修正为 MemReadString
                            string objName = _vmm.MemReadString(_pid, (nint)namePtr, 64);
                            if (!string.IsNullOrEmpty(objName) && objName.Contains(name, StringComparison.OrdinalIgnoreCase))
                            {
                                return gameObject;
                            }
                        }
                    }
                }
            }
            
            byte[] nextBuffer = new byte[8];
            if (!_vmm.MemRead(_pid, (nint)(currentNode + 0x8), nextBuffer, out cbRead, Vmm.FLAG_NOCACHE)) break;
            currentNode = BitConverter.ToUInt64(nextBuffer, 0);
        }

        return 0;
    }
}
