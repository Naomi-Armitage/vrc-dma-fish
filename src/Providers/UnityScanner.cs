using System.Text;
using VrcDmaFish.Models;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

public sealed class UnityScanner
{
    private const int SearchAlignment = 8;
    private const int SearchResultLimit = 64;
    private const int MaxModuleScanSize = 0x10000000;
    private const int DefaultProbeResultLimit = 64;
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

    private readonly IProcessMemory _memory;
    private readonly UnityNativeLayout _layout;

    public UnityScanner(IProcessMemory memory, UnityNativeLayout layout)
    {
        _memory = memory;
        _layout = layout;
    }

    public ulong FindGameObjectManager(IReadOnlyList<string>? configuredPatterns = null)
    {
        var candidates = ProbeGameObjectManagerCandidates(configuredPatterns, DefaultProbeResultLimit);
        var bestCandidate = candidates
            .Where(candidate => candidate.IsValid)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.PrefilterRejected)
            .FirstOrDefault();

        if (bestCandidate is not null)
        {
            Logger.Info(
                "扫描",
                $"GameObjectManager 自动扫描成功：模块 {bestCandidate.ModuleName}，签名 {bestCandidate.PatternName}，地址 0x{bestCandidate.ManagerAddress:X}，路径 {bestCandidate.ValidationPath}，样本对象: {bestCandidate.SampleNames}");
            return bestCandidate.ManagerAddress;
        }

        if (Logger.IsDebugEnabled)
        {
            foreach (var candidate in candidates.Take(16))
            {
                Logger.Debug(
                    "扫描",
                    $"候选 GOM 校验结果：module={candidate.ModuleName} pattern={candidate.PatternName} hit=0x{candidate.InstructionAddress:X} source={candidate.CandidateSource} candidate=0x{candidate.ManagerAddress:X} valid={candidate.IsValid} score={candidate.Score} prefilter={candidate.PrefilterRejected} reason={candidate.FailureReason}");
            }
        }

        var patternSpecs = BuildPatternSpecs(configuredPatterns);
        var firstPattern = patternSpecs.FirstOrDefault();
        var patternSummary = firstPattern.Pattern ?? DefaultPatterns[0].Pattern;
        Logger.Warn("扫描", $"GameObjectManager 特征扫描未返回任何有效匹配项。当前首选特征码: {patternSummary}");
        return 0;
    }

    public IReadOnlyList<GameObjectManagerProbeResult> ProbeGameObjectManagerCandidates(
        IReadOnlyList<string>? configuredPatterns = null,
        int limit = DefaultProbeResultLimit,
        bool includeReplayData = false)
    {
        if (limit <= 0)
        {
            return Array.Empty<GameObjectManagerProbeResult>();
        }

        var modules = ResolveModules();
        if (modules.Count == 0)
        {
            Logger.Warn("扫描", "目标进程中未找到 UnityPlayer.dll 或 GameAssembly.dll。");
            return Array.Empty<GameObjectManagerProbeResult>();
        }

        var patternSpecs = BuildPatternSpecs(configuredPatterns);
        var results = new List<GameObjectManagerProbeResult>();
        var seenCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    foreach (var descriptor in ResolveCandidateDescriptors(module, patternSpec, hit))
                    {
                        var dedupeKey = $"{descriptor.ModuleName}|{descriptor.PatternName}|{descriptor.InstructionAddress:X}|{descriptor.CandidateSource}|{descriptor.CandidateAddress:X}";
                        if (!seenCandidates.Add(dedupeKey))
                        {
                            continue;
                        }

                        results.Add(includeReplayData ? ValidateCandidateWithReplay(descriptor) : ValidateCandidate(descriptor));
                    }
                }
            }
        }

        return results
            .OrderByDescending(result => result.IsValid)
            .ThenByDescending(result => result.Score)
            .ThenBy(result => result.PrefilterRejected)
            .ThenBy(result => result.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.PatternName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.InstructionAddress)
            .Take(limit)
            .ToArray();
    }

    public static GameObjectManagerProbeResult RevalidateRecordedCandidate(GameObjectManagerProbeResult candidate)
    {
        using var replay = new ReplayProcessMemory(candidate.ReplayMemoryBlocks);
        var scanner = new UnityScanner(replay, candidate.Layout);
        return scanner.ValidateCandidate(CreateDescriptor(candidate));
    }

    public ulong FindObjectByName(string name)
    {
        return FindTargetObject(name, 0, 0, null);
    }

    public ulong FindObjectByName(string name, ulong gameObjectManagerAddress)
    {
        return FindTargetObject(name, 0, gameObjectManagerAddress, null);
    }

    public ulong FindObjectByName(string name, ulong gameObjectManagerAddress, IReadOnlyList<string>? gameObjectManagerPatterns)
    {
        return FindTargetObject(name, 0, gameObjectManagerAddress, gameObjectManagerPatterns);
    }

    public ulong FindTargetObject(
        string name,
        ulong targetKlassAddress,
        ulong gameObjectManagerAddress,
        IReadOnlyList<string>? gameObjectManagerPatterns)
    {
        if (gameObjectManagerAddress == 0)
        {
            gameObjectManagerAddress = FindGameObjectManager(gameObjectManagerPatterns);
        }

        if (gameObjectManagerAddress == 0)
        {
            return 0;
        }

        var currentNode = ReadUInt64(gameObjectManagerAddress + _layout.GameObjectManagerActiveNodesOffset);
        var remainingNodes = 1024;

        while (currentNode != 0 && remainingNodes-- > 0)
        {
            var gameObject = ReadUInt64(currentNode + _layout.ObjectNodeGameObjectOffset);
            if (gameObject != 0)
            {
                var namePtr = ReadUInt64(gameObject + _layout.GameObjectNamePointerOffset);
                var objectName = ReadString(namePtr);

                if (TryFindMatchingComponent(gameObject, name, targetKlassAddress, out var componentInfo))
                {
                    var matchSource = targetKlassAddress != 0 && componentInfo.KlassPointer == targetKlassAddress
                        ? $"klass=0x{targetKlassAddress:X}"
                        : $"class='{componentInfo.GetDisplayName()}'";
                    var gameObjectLabel = string.IsNullOrWhiteSpace(objectName) ? "<unnamed>" : objectName;
                    Logger.Info(
                        "扫描",
                        $"找到目标组件 {matchSource}，component=0x{componentInfo.ComponentPointer:X}，gameObject=0x{gameObject:X} ('{gameObjectLabel}')。");
                    return componentInfo.ComponentPointer;
                }

                if (targetKlassAddress == 0 &&
                    !string.IsNullOrWhiteSpace(objectName) &&
                    objectName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("扫描", $"找到对象 '{objectName}'，地址 0x{gameObject:X}。");
                    return gameObject;
                }
            }

            currentNode = ReadUInt64(currentNode + _layout.ObjectNodeNextOffset);
        }

        if (targetKlassAddress != 0)
        {
            Logger.Warn("扫描", $"未找到目标组件：klass=0x{targetKlassAddress:X}，name='{name}'。");
        }
        else
        {
            Logger.Warn("扫描", $"未找到 GameObject/Component '{name}'。");
        }

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
        var currentNode = ReadUInt64(gameObjectManagerAddress + _layout.GameObjectManagerActiveNodesOffset);
        var remainingNodes = Math.Max(limit * 4, 256);

        while (currentNode != 0 && remainingNodes-- > 0 && objects.Count < limit)
        {
            var gameObject = ReadUInt64(currentNode + _layout.ObjectNodeGameObjectOffset);
            if (gameObject != 0)
            {
                var namePtr = ReadUInt64(gameObject + _layout.GameObjectNamePointerOffset);
                var objectName = ReadString(namePtr);
                var componentSummary = ReadComponentSummary(gameObject);
                objects.Add(
                    new UnityObjectInfo(
                        currentNode,
                        gameObject,
                        namePtr,
                        objectName,
                        componentSummary.DeclaredComponentCount,
                        componentSummary.ValidComponentEntries,
                        componentSummary.SampleComponentPointers));
            }

            currentNode = ReadUInt64(currentNode + _layout.ObjectNodeNextOffset);
        }

        Logger.Debug("扫描", $"对象转储完成，共收集 {objects.Count} 个对象。");
        return objects;
    }

    private IReadOnlyList<ModuleCandidate> ResolveModules()
    {
        var modules = new List<ModuleCandidate>();
        foreach (var moduleName in ModuleCandidates)
        {
            var baseAddress = _memory.GetModuleBase(moduleName);
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
            return _memory.Search(
                module.BaseAddress,
                module.BaseAddress + MaxModuleScanSize,
                SearchAlignment,
                patternBytes,
                wildcardMask,
                SearchResultLimit);
        }
        catch (Exception ex)
        {
            Logger.Debug("扫描", $"扫描模块 {module.Name} 时发生异常: {ex.Message}");
            return Array.Empty<ulong>();
        }
    }

    private IEnumerable<GameObjectManagerCandidateDescriptor> ResolveCandidateDescriptors(ModuleCandidate module, GameObjectManagerPatternSpec patternSpec, ulong instructionAddress)
    {
        if (!TryReadRelativeTarget(instructionAddress, out var ripTarget))
        {
            yield break;
        }

        var chain = new List<(string Source, ulong Address)>
        {
            ("rip", ripTarget),
        };

        if (TryReadUInt64(ripTarget, out var dereferencedAddress) && dereferencedAddress != 0)
        {
            chain.Add(("rip*1", dereferencedAddress));

            if (TryReadUInt64(dereferencedAddress, out var secondHopAddress) && secondHopAddress != 0)
            {
                chain.Add(("rip*2", secondHopAddress));
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in chain)
        {
            var dedupeKey = $"{entry.Source}|{entry.Address:X}";
            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            var prefilter = EvaluateCandidateSeed(entry.Address);
            yield return new GameObjectManagerCandidateDescriptor(
                module.Name,
                patternSpec.Name,
                instructionAddress,
                ripTarget,
                entry.Source,
                entry.Address,
                prefilter.Rejected,
                prefilter.Reason);
        }
    }

    private CandidatePrefilterResult EvaluateCandidateSeed(ulong candidateAddress)
    {
        if (!IsLikelyPointer(candidateAddress))
        {
            return new CandidatePrefilterResult(true, "candidate 不是看起来像用户态指针的地址。");
        }

        if ((candidateAddress & 0x7) != 0)
        {
            return new CandidatePrefilterResult(true, "candidate 未按 8 字节对齐。");
        }

        if (!TryReadBytes(candidateAddress, 8, out _))
        {
            return new CandidatePrefilterResult(true, "candidate 地址不可读。");
        }

        var hasStructuredRead =
            GetManagerFieldOffsets().Any(offset => TryReadBytes(candidateAddress + offset, 8, out _)) ||
            GetNodeGameObjectOffsets().Any(offset => TryReadBytes(candidateAddress + offset, 8, out _));

        return hasStructuredRead
            ? new CandidatePrefilterResult(false, string.Empty)
            : new CandidatePrefilterResult(true, "candidate 周围没有读到像 manager/node 结构的字段。");
    }

    private GameObjectManagerProbeResult ValidateCandidateWithReplay(GameObjectManagerCandidateDescriptor descriptor)
    {
        using var recorder = new RecordingProcessMemory(_memory);
        var scanner = new UnityScanner(recorder, _layout);
        var result = scanner.ValidateCandidate(descriptor);
        return result with
        {
            ReplayMemoryBlocks = recorder.ExportBlocks(),
        };
    }

    private GameObjectManagerProbeResult ValidateCandidate(GameObjectManagerCandidateDescriptor descriptor)
    {
        var attempts = new[]
        {
            ValidateAsManagerBase(descriptor),
            ValidateAsNodePointer(descriptor),
        };

        var bestAttempt = attempts
            .OrderByDescending(attempt => attempt.IsValid)
            .ThenByDescending(attempt => attempt.Score)
            .First();

        var failureReason = bestAttempt.FailureReason;
        if (string.IsNullOrWhiteSpace(failureReason) && descriptor.PrefilterRejected)
        {
            failureReason = descriptor.PrefilterReason;
        }

        return new GameObjectManagerProbeResult
        {
            Layout = _layout,
            ModuleName = descriptor.ModuleName,
            PatternName = descriptor.PatternName,
            InstructionAddress = descriptor.InstructionAddress,
            RipTargetAddress = descriptor.RipTargetAddress,
            CandidateSource = descriptor.CandidateSource,
            ManagerAddress = descriptor.CandidateAddress,
            PrefilterRejected = descriptor.PrefilterRejected,
            PrefilterReason = descriptor.PrefilterReason,
            IsValid = bestAttempt.IsValid,
            Score = bestAttempt.Score,
            SampleNames = bestAttempt.SampleNames,
            FailureReason = failureReason,
            Interpretation = bestAttempt.Interpretation,
            ValidationPath = bestAttempt.ValidationPath,
            ManagerFieldLabel = bestAttempt.ManagerFieldLabel,
            ManagerFieldOffset = bestAttempt.ManagerFieldOffset,
            ManagerFieldAddress = bestAttempt.ManagerFieldAddress,
            ManagerFieldValue = bestAttempt.ManagerFieldValue,
            NodeFieldLabel = bestAttempt.NodeFieldLabel,
            NodeFieldOffset = bestAttempt.NodeFieldOffset,
            NodeFieldAddress = bestAttempt.NodeFieldAddress,
            NodeFieldValue = bestAttempt.NodeFieldValue,
            NextNodeValue = bestAttempt.NextNodeValue,
            GameObjectAddress = bestAttempt.GameObjectAddress,
            NamePointerLabel = bestAttempt.NamePointerLabel,
            NamePointerOffset = bestAttempt.NamePointerOffset,
            NamePointerAddress = bestAttempt.NamePointerAddress,
            NamePointerValue = bestAttempt.NamePointerValue,
            ObjectName = bestAttempt.ObjectName,
            DeclaredComponentCount = bestAttempt.DeclaredComponentCount,
            ValidComponentEntries = bestAttempt.ValidComponentEntries,
            SampleComponentPointers = bestAttempt.SampleComponentPointers,
            Observations = attempts.SelectMany(attempt => attempt.Observations).ToArray(),
            ReplayMemoryBlocks = Array.Empty<RecordedMemoryBlock>(),
        };
    }

    private GomValidationAttempt ValidateAsManagerBase(GameObjectManagerCandidateDescriptor descriptor)
    {
        var bestAttempt = GomValidationAttempt.Invalid("ManagerBase", "manager 路径未读到可信链路。");

        foreach (var managerOffset in GetManagerFieldOffsets())
        {
            var observations = new List<GameObjectManagerProbeObservation>();
            var managerFieldAddress = descriptor.CandidateAddress + managerOffset;
            var managerFieldLabel = $"manager+0x{managerOffset:X}";
            var managerValueRead = TryReadUInt64(managerFieldAddress, out var nodePointer);
            observations.Add(
                CreateObservation(
                    "manager",
                    managerFieldLabel,
                    managerFieldAddress,
                    managerValueRead ? nodePointer : null,
                    managerValueRead && IsLikelyPointer(nodePointer),
                    note: managerValueRead ? string.Empty : "读取失败"));

            if (!managerValueRead || !IsLikelyPointer(nodePointer))
            {
                bestAttempt = ChooseBetterAttempt(
                    bestAttempt,
                    GomValidationAttempt.FromFailure(
                        "ManagerBase",
                        observations,
                        $"manager 字段 {managerFieldLabel} 未读到可信节点指针。"));
                continue;
            }

            var attempt = ProbeNodePath(
                interpretation: "ManagerBase",
                nodeAddress: nodePointer,
                managerFieldLabel: managerFieldLabel,
                managerFieldOffset: managerOffset,
                managerFieldAddress: managerFieldAddress,
                managerFieldValue: nodePointer,
                seedObservations: observations);

            bestAttempt = ChooseBetterAttempt(bestAttempt, attempt);
        }

        return bestAttempt;
    }

    private GomValidationAttempt ValidateAsNodePointer(GameObjectManagerCandidateDescriptor descriptor)
    {
        if (!IsLikelyPointer(descriptor.CandidateAddress))
        {
            return GomValidationAttempt.Invalid("NodePointer", "candidate 自身不像节点指针。");
        }

        var observations = new List<GameObjectManagerProbeObservation>
        {
            CreateObservation(
                "candidate",
                "candidate(node)",
                descriptor.CandidateAddress,
                descriptor.CandidateAddress,
                looksLikePointer: true,
                note: descriptor.PrefilterRejected ? descriptor.PrefilterReason : "将 candidate 直接按节点指针解释"),
        };

        return ProbeNodePath(
            interpretation: "NodePointer",
            nodeAddress: descriptor.CandidateAddress,
            managerFieldLabel: "candidate(node)",
            managerFieldOffset: null,
            managerFieldAddress: null,
            managerFieldValue: null,
            seedObservations: observations);
    }

    private GomValidationAttempt ProbeNodePath(
        string interpretation,
        ulong nodeAddress,
        string managerFieldLabel,
        ulong? managerFieldOffset,
        ulong? managerFieldAddress,
        ulong? managerFieldValue,
        List<GameObjectManagerProbeObservation> seedObservations)
    {
        var bestAttempt = GomValidationAttempt.Invalid(interpretation, "节点路径未读到可信的 GameObject。");

        foreach (var nodeGameObjectOffset in GetNodeGameObjectOffsets())
        {
            var observations = new List<GameObjectManagerProbeObservation>(seedObservations);
            var nodeFieldAddress = nodeAddress + nodeGameObjectOffset;
            var nodeFieldLabel = $"node+0x{nodeGameObjectOffset:X}";
            var nodeValueRead = TryReadUInt64(nodeFieldAddress, out var gameObjectAddress);
            observations.Add(
                CreateObservation(
                    "node",
                    nodeFieldLabel,
                    nodeFieldAddress,
                    nodeValueRead ? gameObjectAddress : null,
                    nodeValueRead && IsLikelyPointer(gameObjectAddress),
                    note: nodeValueRead ? string.Empty : "读取失败"));

            TryReadUInt64(nodeAddress + _layout.ObjectNodeNextOffset, out var nextNodeValue);
            observations.Add(
                CreateObservation(
                    "node",
                    $"node+0x{_layout.ObjectNodeNextOffset:X}",
                    nodeAddress + _layout.ObjectNodeNextOffset,
                    nextNodeValue == 0 ? null : nextNodeValue,
                    nextNodeValue == 0 || IsLikelyPointer(nextNodeValue),
                    note: nextNodeValue == 0 ? "next=0" : string.Empty));

            if (!nodeValueRead || !IsLikelyPointer(gameObjectAddress))
            {
                bestAttempt = ChooseBetterAttempt(
                    bestAttempt,
                    GomValidationAttempt.FromFailure(
                        interpretation,
                        observations,
                        $"{nodeFieldLabel} 未读到可信 GameObject 指针。"));
                continue;
            }

            var nameProbe = ProbeGameObjectName(gameObjectAddress, observations);
            var componentSummary = ReadComponentSummary(gameObjectAddress);

            var score = 2;
            if (nextNodeValue == 0 || IsLikelyPointer(nextNodeValue))
            {
                score += 1;
            }

            if (nameProbe.NamePointerRead)
            {
                score += 1;
            }

            if (nameProbe.HasReadableName)
            {
                score += 1;
            }

            if (nameProbe.HasPlausibleName)
            {
                score += 1;
            }

            if (componentSummary.IsCountPlausible)
            {
                score += 1;
            }

            if (componentSummary.ValidComponentEntries > 0)
            {
                score += 1;
            }

            var isValid = score >= 4 && (nameProbe.HasReadableName || componentSummary.IsCountPlausible);
            var sampleNames = !string.IsNullOrWhiteSpace(nameProbe.ObjectName)
                ? nameProbe.ObjectName
                : componentSummary.ValidComponentEntries > 0
                    ? $"components={componentSummary.ValidComponentEntries}/{componentSummary.DeclaredComponentCount}"
                    : string.Empty;

            var failureReason = isValid
                ? string.Empty
                : !string.IsNullOrWhiteSpace(nameProbe.FailureReason)
                    ? nameProbe.FailureReason
                    : componentSummary.IsCountPlausible
                        ? "组件数组结构合理，但未读到可用对象名。"
                        : "GameObject 可读，但对象名和组件数组都不够可信。";

            var validationPath = $"{managerFieldLabel} -> {nodeFieldLabel} -> {nameProbe.NamePointerLabel}";
            var attempt = new GomValidationAttempt(
                interpretation,
                isValid,
                score,
                sampleNames,
                failureReason,
                validationPath,
                managerFieldLabel,
                managerFieldOffset,
                managerFieldAddress,
                managerFieldValue,
                nodeFieldLabel,
                nodeGameObjectOffset,
                nodeFieldAddress,
                gameObjectAddress,
                nextNodeValue == 0 ? null : nextNodeValue,
                gameObjectAddress,
                nameProbe.NamePointerLabel,
                nameProbe.NamePointerOffset,
                nameProbe.NamePointerFieldAddress,
                nameProbe.NamePointerValue,
                nameProbe.ObjectName,
                componentSummary.DeclaredComponentCount == 0 ? null : componentSummary.DeclaredComponentCount,
                componentSummary.IsCountPlausible ? componentSummary.ValidComponentEntries : null,
                componentSummary.SampleComponentPointers,
                observations.ToArray());

            bestAttempt = ChooseBetterAttempt(bestAttempt, attempt);
        }

        return bestAttempt;
    }

    private NameProbeResult ProbeGameObjectName(ulong gameObjectAddress, List<GameObjectManagerProbeObservation> observations)
    {
        var best = NameProbeResult.Invalid;

        foreach (var nameOffset in GetNamePointerOffsets())
        {
            var nameFieldAddress = gameObjectAddress + nameOffset;
            var nameFieldLabel = $"gameObject+0x{nameOffset:X}";
            var namePointerRead = TryReadUInt64(nameFieldAddress, out var namePointer);
            var objectName = namePointerRead ? ReadString(namePointer) : string.Empty;
            var hasReadableName = !string.IsNullOrWhiteSpace(objectName);
            var hasPlausibleName = IsPlausibleObjectName(objectName);
            observations.Add(
                CreateObservation(
                    "gameobject",
                    nameFieldLabel,
                    nameFieldAddress,
                    namePointerRead ? namePointer : null,
                    namePointerRead && IsLikelyPointer(namePointer),
                    textValue: hasReadableName ? objectName : string.Empty,
                    note: namePointerRead ? string.Empty : "读取失败"));

            var probe = new NameProbeResult(
                nameFieldLabel,
                nameOffset,
                nameFieldAddress,
                namePointerRead,
                namePointerRead ? namePointer : null,
                objectName,
                hasReadableName,
                hasPlausibleName,
                hasReadableName ? string.Empty : $"{nameFieldLabel} 未读到可用对象名。");

            best = ChooseBetterNameProbe(best, probe);
        }

        return best;
    }

    private bool TryFindMatchingComponent(
        ulong gameObjectAddress,
        string targetName,
        ulong targetKlassAddress,
        out UnityComponentInfo componentInfo)
    {
        componentInfo = default;
        var components = ReadComponents(gameObjectAddress, out _, out var isCountPlausible);
        if (!isCountPlausible)
        {
            return false;
        }

        foreach (var component in components)
        {
            if (targetKlassAddress != 0 && component.KlassPointer == targetKlassAddress)
            {
                componentInfo = component;
                return true;
            }

            if (IsTargetClassNameMatch(component, targetName))
            {
                componentInfo = component;
                return true;
            }
        }

        return false;
    }

    private ComponentSummary ReadComponentSummary(ulong gameObjectAddress)
    {
        var components = ReadComponents(gameObjectAddress, out var declaredComponentCount, out var isCountPlausible);
        if (!isCountPlausible)
        {
            return declaredComponentCount <= 0
                ? ComponentSummary.Invalid
                : new ComponentSummary(declaredComponentCount, 0, string.Empty, false);
        }

        var samplePointers = components
            .Take(3)
            .Select(component => component.GetSampleLabel())
            .ToArray();

        return new ComponentSummary(
            declaredComponentCount,
            components.Count,
            string.Join(", ", samplePointers),
            true);
    }

    private List<UnityComponentInfo> ReadComponents(
        ulong gameObjectAddress,
        out int declaredComponentCount,
        out bool isCountPlausible)
    {
        declaredComponentCount = 0;
        isCountPlausible = false;

        if (!TryReadUInt64(gameObjectAddress + _layout.GameObjectComponentArrayOffset, out var componentArray) ||
            !IsLikelyPointer(componentArray))
        {
            return new List<UnityComponentInfo>();
        }

        if (!TryReadInt32(gameObjectAddress + _layout.GameObjectComponentCountOffset, out var componentCount))
        {
            return new List<UnityComponentInfo>();
        }

        declaredComponentCount = componentCount;
        if (componentCount <= 0 || componentCount > _layout.MaxComponentCount)
        {
            return new List<UnityComponentInfo>();
        }

        isCountPlausible = true;
        var components = new List<UnityComponentInfo>();
        for (var i = 0; i < componentCount; i++)
        {
            var elementAddress = componentArray + ((ulong)i * _layout.ComponentArrayElementStride);
            if (!TryReadUInt64(elementAddress + _layout.ComponentArrayElementComponentPointerOffset, out var componentPointer) ||
                !IsLikelyPointer(componentPointer))
            {
                continue;
            }

            if (!TryReadUInt64(componentPointer + _layout.ComponentGameObjectOffset, out var ownerGameObject) ||
                ownerGameObject != gameObjectAddress)
            {
                continue;
            }

            TryReadUInt64(componentPointer + _layout.ComponentKlassPointerOffset, out var klassPointer);
            var classInfo = ReadIl2CppClassInfo(klassPointer);
            components.Add(
                new UnityComponentInfo(
                    componentPointer,
                    klassPointer,
                    ownerGameObject,
                    classInfo.Name,
                    classInfo.Namespace,
                    classInfo.IsMonoBehaviourDerived));
        }

        return components;
    }

    private UnityIl2CppClassInfo ReadIl2CppClassInfo(ulong klassPointer)
    {
        if (!IsLikelyPointer(klassPointer))
        {
            return UnityIl2CppClassInfo.Invalid;
        }

        TryReadUInt64(klassPointer + _layout.Il2CppClassNamePointerOffset, out var classNamePointer);
        TryReadUInt64(klassPointer + _layout.Il2CppClassNamespacePointerOffset, out var namespacePointer);

        var className = ReadString(classNamePointer);
        var namespaceName = ReadString(namespacePointer);
        var isMonoBehaviourDerived = IsMonoBehaviourDerived(klassPointer);

        return new UnityIl2CppClassInfo(klassPointer, classNamePointer, namespacePointer, className, namespaceName, isMonoBehaviourDerived);
    }

    private bool IsMonoBehaviourDerived(ulong klassPointer)
    {
        var visited = new HashSet<ulong>();
        var current = klassPointer;

        for (var depth = 0; depth < _layout.MaxClassParentDepth && IsLikelyPointer(current) && visited.Add(current); depth++)
        {
            TryReadUInt64(current + _layout.Il2CppClassNamePointerOffset, out var namePointer);
            TryReadUInt64(current + _layout.Il2CppClassNamespacePointerOffset, out var namespacePointer);
            var className = ReadString(namePointer);
            var namespaceName = ReadString(namespacePointer);

            if (string.Equals(className, "MonoBehaviour", StringComparison.Ordinal) &&
                string.Equals(namespaceName, "UnityEngine", StringComparison.Ordinal))
            {
                return true;
            }

            if (!TryReadUInt64(current + _layout.Il2CppClassParentPointerOffset, out current))
            {
                break;
            }
        }

        return false;
    }

    private static bool IsTargetClassNameMatch(UnityComponentInfo component, string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(component.ClassName) &&
            component.ClassName.Contains(targetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var displayName = component.GetDisplayName();
        return !string.IsNullOrWhiteSpace(displayName) &&
               displayName.Contains(targetName, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ulong> GetUniqueOffsets(params ulong[] offsets)
    {
        var unique = new List<ulong>();
        foreach (var offset in offsets)
        {
            if (!unique.Contains(offset))
            {
                unique.Add(offset);
            }
        }

        return unique;
    }

    private IReadOnlyList<ulong> GetManagerFieldOffsets() =>
        GetUniqueOffsets(_layout.GameObjectManagerActiveNodesOffset, 0x10, 0x18, 0x20, 0x28);

    private IReadOnlyList<ulong> GetNodeGameObjectOffsets() =>
        GetUniqueOffsets(_layout.ObjectNodeGameObjectOffset, 0x10, 0x18);

    private IReadOnlyList<ulong> GetNamePointerOffsets() =>
        GetUniqueOffsets(_layout.GameObjectNamePointerOffset, 0x60, 0x30);

    private static GomValidationAttempt ChooseBetterAttempt(GomValidationAttempt current, GomValidationAttempt candidate)
    {
        if (candidate.IsValid && !current.IsValid)
        {
            return candidate;
        }

        if (candidate.IsValid == current.IsValid && candidate.Score > current.Score)
        {
            return candidate;
        }

        if (!current.IsValid && candidate.Score == current.Score && current.Observations.Length == 0)
        {
            return candidate;
        }

        return current;
    }

    private static NameProbeResult ChooseBetterNameProbe(NameProbeResult current, NameProbeResult candidate)
    {
        if (candidate.HasPlausibleName && !current.HasPlausibleName)
        {
            return candidate;
        }

        if (candidate.HasReadableName && !current.HasReadableName)
        {
            return candidate;
        }

        if (candidate.NamePointerRead && !current.NamePointerRead)
        {
            return candidate;
        }

        return current;
    }

    private static GameObjectManagerProbeObservation CreateObservation(
        string stage,
        string label,
        ulong address,
        ulong? value,
        bool looksLikePointer,
        string textValue = "",
        string note = "")
    {
        return new GameObjectManagerProbeObservation
        {
            Stage = stage,
            Label = label,
            Address = address,
            Value = value,
            ReadSucceeded = value.HasValue,
            LooksLikePointer = looksLikePointer,
            TextValue = textValue,
            Note = note,
        };
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

    private bool TryReadRelativeTarget(ulong instructionAddress, out ulong targetAddress)
    {
        targetAddress = 0;
        if (!TryReadBytes(instructionAddress + 3, 4, out var relativeOffsetBytes))
        {
            return false;
        }

        var relativeOffset = BitConverter.ToInt32(relativeOffsetBytes, 0);
        targetAddress = (ulong)((long)instructionAddress + 7 + relativeOffset);
        return targetAddress != 0;
    }

    private bool TryReadBytes(ulong address, int length, out byte[] bytes) => _memory.TryReadBytes(address, length, out bytes);

    private bool TryReadUInt64(ulong address, out ulong value)
    {
        value = 0;
        if (!TryReadBytes(address, 8, out var bytes))
        {
            return false;
        }

        value = BitConverter.ToUInt64(bytes, 0);
        return true;
    }

    private bool TryReadInt32(ulong address, out int value)
    {
        value = 0;
        if (!TryReadBytes(address, 4, out var bytes))
        {
            return false;
        }

        value = BitConverter.ToInt32(bytes, 0);
        return true;
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

        if (!TryReadBytes(address, _layout.MaxStringLength, out var bytes))
        {
            return string.Empty;
        }

        var zeroIndex = Array.IndexOf(bytes, (byte)0);
        var textLength = zeroIndex >= 0 ? zeroIndex : bytes.Length;
        if (textLength <= 0)
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(bytes, 0, textLength).Trim();
        }
        catch
        {
            return string.Empty;
        }
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

    private static GameObjectManagerCandidateDescriptor CreateDescriptor(GameObjectManagerProbeResult candidate) =>
        new(
            candidate.ModuleName,
            candidate.PatternName,
            candidate.InstructionAddress,
            candidate.RipTargetAddress,
            candidate.CandidateSource,
            candidate.ManagerAddress,
            candidate.PrefilterRejected,
            candidate.PrefilterReason);

    private readonly record struct ModuleCandidate(string Name, ulong BaseAddress);
    private readonly record struct GameObjectManagerPatternSpec(string Name, string Pattern);
    private readonly record struct GameObjectManagerCandidateDescriptor(
        string ModuleName,
        string PatternName,
        ulong InstructionAddress,
        ulong RipTargetAddress,
        string CandidateSource,
        ulong CandidateAddress,
        bool PrefilterRejected,
        string PrefilterReason);

    private readonly record struct CandidatePrefilterResult(bool Rejected, string Reason);

    private readonly record struct NameProbeResult(
        string NamePointerLabel,
        ulong NamePointerOffset,
        ulong NamePointerFieldAddress,
        bool NamePointerRead,
        ulong? NamePointerValue,
        string ObjectName,
        bool HasReadableName,
        bool HasPlausibleName,
        string FailureReason)
    {
        public static NameProbeResult Invalid { get; } = new(string.Empty, 0, 0, false, null, string.Empty, false, false, string.Empty);
    }

    private readonly record struct ComponentSummary(
        int DeclaredComponentCount,
        int ValidComponentEntries,
        string SampleComponentPointers,
        bool IsCountPlausible)
    {
        public static ComponentSummary Invalid { get; } = new(0, 0, string.Empty, false);
    }

    private readonly record struct UnityIl2CppClassInfo(
        ulong KlassPointer,
        ulong NamePointer,
        ulong NamespacePointer,
        string Name,
        string Namespace,
        bool IsMonoBehaviourDerived)
    {
        public static UnityIl2CppClassInfo Invalid { get; } = new(0, 0, 0, string.Empty, string.Empty, false);
    }

    private readonly record struct GomValidationAttempt(
        string Interpretation,
        bool IsValid,
        int Score,
        string SampleNames,
        string FailureReason,
        string ValidationPath,
        string ManagerFieldLabel,
        ulong? ManagerFieldOffset,
        ulong? ManagerFieldAddress,
        ulong? ManagerFieldValue,
        string NodeFieldLabel,
        ulong? NodeFieldOffset,
        ulong? NodeFieldAddress,
        ulong? NodeFieldValue,
        ulong? NextNodeValue,
        ulong? GameObjectAddress,
        string NamePointerLabel,
        ulong? NamePointerOffset,
        ulong? NamePointerAddress,
        ulong? NamePointerValue,
        string ObjectName,
        int? DeclaredComponentCount,
        int? ValidComponentEntries,
        string SampleComponentPointers,
        GameObjectManagerProbeObservation[] Observations)
    {
        public static GomValidationAttempt Invalid(string interpretation, string failureReason) =>
            new(
                interpretation,
                false,
                0,
                string.Empty,
                failureReason,
                string.Empty,
                string.Empty,
                null,
                null,
                null,
                string.Empty,
                null,
                null,
                null,
                null,
                null,
                string.Empty,
                null,
                null,
                null,
                string.Empty,
                null,
                null,
                string.Empty,
                Array.Empty<GameObjectManagerProbeObservation>());

        public static GomValidationAttempt FromFailure(string interpretation, List<GameObjectManagerProbeObservation> observations, string failureReason) =>
            Invalid(interpretation, failureReason) with { Observations = observations.ToArray() };
    }
}

public sealed record GameObjectManagerProbeDump
{
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;

    public string ProcessName { get; init; } = string.Empty;

    public int ProcessId { get; init; }

    public UnityNativeLayout Layout { get; init; } = UnityNativeLayout.Default;

    public GameObjectManagerProbeResult[] Candidates { get; init; } = Array.Empty<GameObjectManagerProbeResult>();
}

public sealed record GameObjectManagerProbeResult
{
    public UnityNativeLayout Layout { get; init; } = UnityNativeLayout.Default;

    public string ModuleName { get; init; } = string.Empty;

    public string PatternName { get; init; } = string.Empty;

    public ulong InstructionAddress { get; init; }

    public ulong RipTargetAddress { get; init; }

    public string CandidateSource { get; init; } = string.Empty;

    public ulong ManagerAddress { get; init; }

    public bool PrefilterRejected { get; init; }

    public string PrefilterReason { get; init; } = string.Empty;

    public bool IsValid { get; init; }

    public int Score { get; init; }

    public string SampleNames { get; init; } = string.Empty;

    public string FailureReason { get; init; } = string.Empty;

    public string Interpretation { get; init; } = string.Empty;

    public string ValidationPath { get; init; } = string.Empty;

    public string ManagerFieldLabel { get; init; } = string.Empty;

    public ulong? ManagerFieldOffset { get; init; }

    public ulong? ManagerFieldAddress { get; init; }

    public ulong? ManagerFieldValue { get; init; }

    public string NodeFieldLabel { get; init; } = string.Empty;

    public ulong? NodeFieldOffset { get; init; }

    public ulong? NodeFieldAddress { get; init; }

    public ulong? NodeFieldValue { get; init; }

    public ulong? NextNodeValue { get; init; }

    public ulong? GameObjectAddress { get; init; }

    public string NamePointerLabel { get; init; } = string.Empty;

    public ulong? NamePointerOffset { get; init; }

    public ulong? NamePointerAddress { get; init; }

    public ulong? NamePointerValue { get; init; }

    public string ObjectName { get; init; } = string.Empty;

    public int? DeclaredComponentCount { get; init; }

    public int? ValidComponentEntries { get; init; }

    public string SampleComponentPointers { get; init; } = string.Empty;

    public GameObjectManagerProbeObservation[] Observations { get; init; } = Array.Empty<GameObjectManagerProbeObservation>();

    public RecordedMemoryBlock[] ReplayMemoryBlocks { get; init; } = Array.Empty<RecordedMemoryBlock>();

}

public sealed record GameObjectManagerProbeObservation
{
    public string Stage { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public ulong Address { get; init; }

    public ulong? Value { get; init; }

    public bool ReadSucceeded { get; init; }

    public bool LooksLikePointer { get; init; }

    public string TextValue { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;
}

public readonly record struct UnityObjectInfo(
    ulong NodeAddress,
    ulong GameObjectAddress,
    ulong NamePointer,
    string Name,
    int DeclaredComponentCount,
    int ValidComponentEntries,
    string SampleComponentPointers);

public readonly record struct UnityComponentInfo(
    ulong ComponentPointer,
    ulong KlassPointer,
    ulong OwnerGameObject,
    string ClassName,
    string Namespace,
    bool IsMonoBehaviourDerived)
{
    public string GetDisplayName()
    {
        if (string.IsNullOrWhiteSpace(ClassName))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(Namespace)
            ? ClassName
            : $"{Namespace}.{ClassName}";
    }

    public string GetSampleLabel()
    {
        var typeLabel = GetDisplayName();
        var monoBehaviourSuffix = IsMonoBehaviourDerived ? "/MonoBehaviour" : string.Empty;
        if (!string.IsNullOrWhiteSpace(typeLabel))
        {
            return $"{typeLabel}@0x{ComponentPointer:X}/klass=0x{KlassPointer:X}{monoBehaviourSuffix}";
        }

        return $"0x{ComponentPointer:X}/klass=0x{KlassPointer:X}{monoBehaviourSuffix}";
    }
}
