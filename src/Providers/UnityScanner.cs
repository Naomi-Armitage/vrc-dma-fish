using System.Runtime.Versioning;
using System.Text;
using Vmmsharp;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

[SupportedOSPlatform("windows")]
public sealed class UnityScanner
{
    private const string DefaultGameObjectManagerPattern = "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 40 ?? 48 85 C0";
    private readonly VmmProcess _process;

    public UnityScanner(VmmProcess process)
    {
        _process = process;
    }

    public ulong FindGameObjectManager(string? pattern = null)
    {
        var unityPlayerBase = _process.GetModuleBase("UnityPlayer.dll");
        if (unityPlayerBase == 0)
        {
            Logger.Warn("扫描", "目标进程中未找到 UnityPlayer.dll。");
            return 0;
        }

        try
        {
            var activePattern = string.IsNullOrWhiteSpace(pattern) ? DefaultGameObjectManagerPattern : pattern.Trim();
            var (patternBytes, wildcardMask) = ParsePattern(activePattern);
            using var search = _process.Search(unityPlayerBase, unityPlayerBase + 0x10000000, 8, Vmm.FLAG_NOCACHE);
            search.AddSearch(patternBytes, wildcardMask, 1);
            search.Start();

            var result = search.Result();
            if (!result.isCompletedSuccess || result.result is null || result.result.Count == 0)
            {
                Logger.Warn("扫描", $"GameObjectManager 特征扫描未返回任何匹配项。当前特征码: {activePattern}");
                return 0;
            }

            var instructionAddress = result.result[0].address;
            var relativeOffsetBytes = _process.MemRead(instructionAddress + 3, 4, Vmm.FLAG_NOCACHE);
            if (relativeOffsetBytes.Length != 4)
            {
                Logger.Warn("扫描", "读取 GameObjectManager RIP 相对偏移失败。");
                return 0;
            }

            var relativeOffset = BitConverter.ToInt32(relativeOffsetBytes, 0);
            return (ulong)((long)instructionAddress + 7 + relativeOffset);
        }
        catch (Exception ex)
        {
            Logger.Warn("扫描", $"Unity 自动扫描失败: {ex.Message}");
            return 0;
        }
    }

    public ulong FindObjectByName(string name)
    {
        return FindObjectByName(name, 0, null);
    }

    public ulong FindObjectByName(string name, ulong gameObjectManagerAddress)
    {
        return FindObjectByName(name, gameObjectManagerAddress, null);
    }

    public ulong FindObjectByName(string name, ulong gameObjectManagerAddress, string? gameObjectManagerPattern)
    {
        if (gameObjectManagerAddress == 0)
        {
            gameObjectManagerAddress = FindGameObjectManager(gameObjectManagerPattern);
        }

        if (gameObjectManagerAddress == 0)
        {
            return 0;
        }

        var currentNode = ReadUInt64(gameObjectManagerAddress + 0x10);
        var remainingNodes = 1024;

        while (currentNode != 0 && remainingNodes-- > 0)
        {
            var gameObject = ReadUInt64(currentNode + 0x10);
            if (gameObject != 0)
            {
                var namePtr = ReadUInt64(gameObject + 0x30);
                var objectName = ReadString(namePtr);
                if (!string.IsNullOrWhiteSpace(objectName) &&
                    objectName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("扫描", $"找到对象 '{objectName}'，地址 0x{gameObject:X}。");
                    return gameObject;
                }
            }

            currentNode = ReadUInt64(currentNode + 0x8);
        }

        Logger.Warn("扫描", $"未找到 GameObject '{name}'。");
        return 0;
    }

    private static (byte[] PatternBytes, byte[] WildcardMask) ParsePattern(string pattern)
    {
        var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var patternBytes = new byte[tokens.Length];
        var wildcardMask = new byte[tokens.Length];

        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "??")
            {
                wildcardMask[i] = 0xFF;
                continue;
            }

            patternBytes[i] = Convert.ToByte(tokens[i], 16);
        }

        return (patternBytes, wildcardMask);
    }

    private ulong ReadUInt64(ulong address)
    {
        var bytes = _process.MemRead(address, 8, Vmm.FLAG_NOCACHE);
        return bytes.Length == 8 ? BitConverter.ToUInt64(bytes, 0) : 0;
    }

    private string ReadString(ulong address)
    {
        if (address == 0)
        {
            return string.Empty;
        }

        return _process.MemReadString(Encoding.UTF8, address, 64, Vmm.FLAG_NOCACHE, true).TrimEnd('\0');
    }
}
