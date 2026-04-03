using System.Runtime.Versioning;
using System.Text;
using Vmmsharp;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

[SupportedOSPlatform("windows")]
public sealed class UnityScanner
{
    private const int SearchAlignment = 8;
    private const int SearchResultLimit = 64;
    private const int MaxNodeProbeCount = 8;
    private const int MaxModuleScanSize = 0x10000000;
    private static readonly string[] ModuleCandidates =
    {
        "UnityPlayer.dll",
        "GameAssembly.dll",
    };

    private static readonly GameObjectManagerPatternSpec[] DefaultPatterns =
    {
        new(
            "LegacyMovRax",
            "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 40 ?? 48 85 C0"),
        new(
            "FindGameObjectsWithTagMovRcx",
            "48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? 41 B9 ?? ?? ?? ?? 4C 8D 05 ?? ?? ?? ??"),
        new(
            "FindMainCameraMovRbx",
            "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 41 B8 ?? ?? ?? ??"),
        new(
            "FallbackLeaRcx",
            "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B D8"),
        new(
            "FallbackLeaRsi",
            "48 8D 35 ?? ?? ?? ?? 81 FA"),
    };

    private readonly VmmProcess _process;

    public UnityScanner(VmmProcess process)
    {
        _process = process;
    }

    public ulong FindGameObjectManager(IReadOnlyList<string>? configuredPatterns = null)
    {
        var modules = ResolveModules();
        if (modules.Count == 0)
        {
            Logger.Warn("扫描", "目标进程中未找到 UnityPlayer.dll 或 GameAssembly.dll。");
            return 0;
        }

        var patternSpecs = BuildPatternSpecs(configuredPatterns);
        var bestCandidate = default(GameObjectManagerCandidate?);

        foreach (var module in modules)
        {
            foreach (var patternSpec in patternSpecs)
            {
                Logger.Debug(
                    "扫描",
                    $"扫描模块 {module.Name} 基址 0x{module.BaseAddress:X}，候选签名 {patternSpec.Name}: {patternSpec.Pattern}");

                var hits = SearchPattern(module, patternSpec.Pattern);
                if (hits.Count == 0)
                {
                    continue;
                }

                Logger.Debug("扫描", $"签名 {patternSpec.Name} 在 {module.Name} 中命中 {hits.Count} 次。");

                foreach (var hit in hits)
                {
                    foreach (var candidateAddress in ResolveCandidateAddresses(hit))
                    {
                        if (!TryValidateGameObjectManager(candidateAddress, out var score, out var sampleNames))
                        {
                            continue;
                        }

                        var candidate = new GameObjectManagerCandidate(
                            module.Name,
                            patternSpec.Name,
                            hit,
                            candidateAddress,
                            score,
                            sampleNames);

                        if (bestCandidate is null || candidate.Score > bestCandidate.Value.Score)
                        {
                            bestCandidate = candidate;
                        }
                    }
                }
            }
        }

        if (bestCandidate is GameObjectManagerCandidate resolvedCandidate)
        {
            Logger.Info(
                "扫描",
                $"GameObjectManager 自动扫描成功：模块 {resolvedCandidate.ModuleName}，签名 {resolvedCandidate.PatternName}，地址 0x{resolvedCandidate.ManagerAddress:X}，样本对象: {resolvedCandidate.SampleNames}");
            return resolvedCandidate.ManagerAddress;
        }

        var firstPattern = patternSpecs.FirstOrDefault();
        var patternSummary = firstPattern.Pattern ?? DefaultPatterns[0].Pattern;
        Logger.Warn("扫描", $"GameObjectManager 特征扫描未返回任何有效匹配项。当前首选特征码: {patternSummary}");
        return 0;
    }

    public ulong FindObjectByName(string name)
    {
        return FindObjectByName(name, 0, null);
    }

    public ulong FindObjectByName(string name, ulong gameObjectManagerAddress)
    {
        return FindObjectByName(name, gameObjectManagerAddress, null);
    }

    public ulong FindObjectByName(string name, ulong gameObjectManagerAddress, IReadOnlyList<string>? gameObjectManagerPatterns)
    {
        if (gameObjectManagerAddress == 0)
        {
            gameObjectManagerAddress = FindGameObjectManager(gameObjectManagerPatterns);
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

    public IReadOnlyList<UnityObjectInfo> DumpObjects(int limit, ulong gameObjectManagerAddress, IReadOnlyList<string>? gameObjectManagerPatterns)
    {
        if (limit <= 0)
        {
            return Array.Empty<UnityObjectInfo>();
        }

        if (gameObjectManagerAddress == 0)
        {
            gameObjectManagerAddress = FindGameObjectManager(gameObjectManagerPatterns);
        }

        if (gameObjectManagerAddress == 0)
        {
            return Array.Empty<UnityObjectInfo>();
        }

        var objects = new List<UnityObjectInfo>();
        var currentNode = ReadUInt64(gameObjectManagerAddress + 0x10);
        var remainingNodes = Math.Max(limit * 4, 256);

        while (currentNode != 0 && remainingNodes-- > 0 && objects.Count < limit)
        {
            var gameObject = ReadUInt64(currentNode + 0x10);
            if (gameObject != 0)
            {
                var namePtr = ReadUInt64(gameObject + 0x30);
                var objectName = ReadString(namePtr);
                objects.Add(new UnityObjectInfo(currentNode, gameObject, namePtr, objectName));
            }

            currentNode = ReadUInt64(currentNode + 0x8);
        }

        Logger.Debug("扫描", $"对象转储完成，共收集 {objects.Count} 个对象。");
        return objects;
    }

    private IReadOnlyList<ModuleCandidate> ResolveModules()
    {
        var modules = new List<ModuleCandidate>();
        foreach (var moduleName in ModuleCandidates)
        {
            var baseAddress = _process.GetModuleBase(moduleName);
            if (baseAddress != 0)
            {
                modules.Add(new ModuleCandidate(moduleName, baseAddress));
            }
        }

        return modules;
    }

    private static IReadOnlyList<GameObjectManagerPatternSpec> BuildPatternSpecs(IReadOnlyList<string>? configuredPatterns)
    {
        var specs = new List<GameObjectManagerPatternSpec>();
        var seenPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (configuredPatterns is not null)
        {
            var index = 1;
            foreach (var pattern in configuredPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                var trimmed = pattern.Trim();
                if (seenPatterns.Add(trimmed))
                {
                    specs.Add(new GameObjectManagerPatternSpec($"Configured{index++}", trimmed));
                }
            }
        }

        foreach (var defaultPattern in DefaultPatterns)
        {
            if (seenPatterns.Add(defaultPattern.Pattern))
            {
                specs.Add(defaultPattern);
            }
        }

        return specs;
    }

    private IReadOnlyList<ulong> SearchPattern(ModuleCandidate module, string pattern)
    {
        try
        {
            var (patternBytes, wildcardMask) = ParsePattern(pattern);
            using var search = _process.Search(
                module.BaseAddress,
                module.BaseAddress + MaxModuleScanSize,
                SearchAlignment,
                Vmm.FLAG_NOCACHE);

            search.AddSearch(patternBytes, wildcardMask, SearchResultLimit);
            search.Start();

            var result = search.Result();
            if (!result.isCompletedSuccess || result.result is null || result.result.Count == 0)
            {
                return Array.Empty<ulong>();
            }

            return result.result
                .Select(entry => entry.address)
                .Where(address => address != 0)
                .Distinct()
                .ToArray();
        }
        catch (Exception ex)
        {
            Logger.Debug("扫描", $"扫描模块 {module.Name} 时发生异常: {ex.Message}");
            return Array.Empty<ulong>();
        }
    }

    private IEnumerable<ulong> ResolveCandidateAddresses(ulong instructionAddress)
    {
        if (!TryReadRelativeTarget(instructionAddress, out var ripTarget))
        {
            yield break;
        }

        var seen = new HashSet<ulong>();
        foreach (var candidate in ExpandCandidateChain(ripTarget))
        {
            if (candidate != 0 && seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private IEnumerable<ulong> ExpandCandidateChain(ulong startAddress)
    {
        yield return startAddress;

        if (!TryReadUInt64(startAddress, out var dereferencedAddress) || dereferencedAddress == 0)
        {
            yield break;
        }

        yield return dereferencedAddress;

        if (!TryReadUInt64(dereferencedAddress, out var secondHopAddress) || secondHopAddress == 0)
        {
            yield break;
        }

        yield return secondHopAddress;
    }

    private bool TryReadRelativeTarget(ulong instructionAddress, out ulong targetAddress)
    {
        targetAddress = 0;
        try
        {
            var relativeOffsetBytes = _process.MemRead(instructionAddress + 3, 4, Vmm.FLAG_NOCACHE);
            if (relativeOffsetBytes.Length != 4)
            {
                return false;
            }

            var relativeOffset = BitConverter.ToInt32(relativeOffsetBytes, 0);
            targetAddress = (ulong)((long)instructionAddress + 7 + relativeOffset);
            return targetAddress != 0;
        }
        catch
        {
            return false;
        }
    }

    private bool TryValidateGameObjectManager(ulong managerAddress, out int score, out string sampleNames)
    {
        score = 0;
        sampleNames = string.Empty;

        if (!IsLikelyPointer(managerAddress))
        {
            return false;
        }

        if (!TryReadUInt64(managerAddress + 0x10, out var currentNode) || !IsLikelyPointer(currentNode))
        {
            return false;
        }

        var samples = new List<string>();
        var visitedNodes = new HashSet<ulong>();

        for (var i = 0; i < MaxNodeProbeCount && currentNode != 0; i++)
        {
            if (!visitedNodes.Add(currentNode))
            {
                break;
            }

            if (!TryReadUInt64(currentNode + 0x10, out var gameObject) || !IsLikelyPointer(gameObject))
            {
                break;
            }

            if (!TryReadUInt64(gameObject + 0x30, out var namePointer))
            {
                break;
            }

            var objectName = ReadString(namePointer);
            if (IsPlausibleObjectName(objectName))
            {
                score += 2;
                samples.Add(objectName);

                if (objectName.Contains("Fishing", StringComparison.OrdinalIgnoreCase) ||
                    objectName.Contains("VRChat", StringComparison.OrdinalIgnoreCase) ||
                    objectName.Contains("Manager", StringComparison.OrdinalIgnoreCase))
                {
                    score += 1;
                }
            }

            if (!TryReadUInt64(currentNode + 0x8, out currentNode))
            {
                break;
            }
        }

        if (samples.Count == 0)
        {
            return false;
        }

        sampleNames = string.Join(", ", samples.Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
        return score >= 2;
    }

    private static bool IsPlausibleObjectName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }

        var hasLetterOrDigit = false;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                hasLetterOrDigit = true;
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '_' or '-' or '.' or '(' or ')' or '[' or ']')
            {
                continue;
            }

            return false;
        }

        return hasLetterOrDigit;
    }

    private static bool IsLikelyPointer(ulong address)
    {
        return address is > 0x10000 and < 0x0000FFFFFFFFFFFF;
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

    private bool TryReadUInt64(ulong address, out ulong value)
    {
        value = 0;
        try
        {
            var bytes = _process.MemRead(address, 8, Vmm.FLAG_NOCACHE);
            if (bytes.Length != 8)
            {
                return false;
            }

            value = BitConverter.ToUInt64(bytes, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private ulong ReadUInt64(ulong address)
    {
        return TryReadUInt64(address, out var value) ? value : 0;
    }

    private string ReadString(ulong address)
    {
        if (address == 0)
        {
            return string.Empty;
        }

        try
        {
            return _process.MemReadString(Encoding.UTF8, address, 64, Vmm.FLAG_NOCACHE, true).TrimEnd('\0');
        }
        catch
        {
            return string.Empty;
        }
    }

    private readonly record struct ModuleCandidate(string Name, ulong BaseAddress);
    private readonly record struct GameObjectManagerPatternSpec(string Name, string Pattern);
    private readonly record struct GameObjectManagerCandidate(
        string ModuleName,
        string PatternName,
        ulong InstructionAddress,
        ulong ManagerAddress,
        int Score,
        string SampleNames);
}

public readonly record struct UnityObjectInfo(ulong NodeAddress, ulong GameObjectAddress, ulong NamePointer, string Name);
