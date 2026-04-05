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

    public GameObjectManagerProbeResult? FindBestGameObjectManagerCandidate(IReadOnlyList<string>? configuredPatterns = null)
    {
        var candidates = ProbeGameObjectManagerCandidates(configuredPatterns, DefaultProbeResultLimit);
        return candidates
            .Where(candidate => candidate.IsValid)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.PrefilterRejected)
            .FirstOrDefault();
    }

    public ulong FindGameObjectManager(IReadOnlyList<string>? configuredPatterns = null)
    {
        var bestCandidate = FindBestGameObjectManagerCandidate(configuredPatterns);

        if (bestCandidate is not null)
        {
            Logger.Info(
                "扫描",
                $"GameObjectManager 自动扫描成功：模块 {bestCandidate.ModuleName}，签名 {bestCandidate.PatternName}，地址 0x{bestCandidate.ManagerAddress:X}，路径 {bestCandidate.ValidationPath}，样本对象: {bestCandidate.SampleNames}");
            return bestCandidate.ManagerAddress;
        }

        var candidates = ProbeGameObjectManagerCandidates(configuredPatterns, DefaultProbeResultLimit);
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
        if (!TryResolveTraversalPlan(gameObjectManagerAddress, gameObjectManagerPatterns, out var traversalPlan))
        {
            return 0;
        }

        var currentNode = traversalPlan.StartNodeAddress;
        var remainingNodes = 1024;

        while (currentNode != 0 && remainingNodes-- > 0)
        {
            var gameObject = ReadUInt64(currentNode + traversalPlan.NodeGameObjectOffset);
            if (gameObject != 0)
            {
                var namePtr = ReadUInt64(gameObject + traversalPlan.GameObjectNamePointerOffset);
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

            currentNode = ReadUInt64(currentNode + traversalPlan.NodeNextOffset);
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

        if (!TryResolveTraversalPlan(gameObjectManagerAddress, gameObjectManagerPatterns, out var traversalPlan))
        {
            return Array.Empty<UnityObjectInfo>();
        }

        var objects = new List<UnityObjectInfo>();
        var currentNode = traversalPlan.StartNodeAddress;
        var remainingNodes = Math.Max(limit * 4, 256);

        while (currentNode != 0 && remainingNodes-- > 0 && objects.Count < limit)
        {
            var gameObject = ReadUInt64(currentNode + traversalPlan.NodeGameObjectOffset);
            if (gameObject != 0)
            {
                var namePtr = ReadUInt64(gameObject + traversalPlan.GameObjectNamePointerOffset);
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

            currentNode = ReadUInt64(currentNode + traversalPlan.NodeNextOffset);
        }

        Logger.Debug("扫描", $"对象转储完成，共收集 {objects.Count} 个对象。");
        return objects;
    }

    private bool TryResolveTraversalPlan(
        ulong configuredGameObjectManagerAddress,
        IReadOnlyList<string>? gameObjectManagerPatterns,
        out UnityObjectTraversalPlan traversalPlan)
    {
        if (configuredGameObjectManagerAddress != 0)
        {
            traversalPlan = CreateConfiguredTraversalPlan(configuredGameObjectManagerAddress);
            return traversalPlan.StartNodeAddress != 0;
        }

        var bestCandidate = FindBestGameObjectManagerCandidate(gameObjectManagerPatterns);
        if (bestCandidate is null)
        {
            traversalPlan = default;
            return false;
        }

        traversalPlan = CreateTraversalPlan(bestCandidate);
        return traversalPlan.StartNodeAddress != 0;
    }

    private UnityObjectTraversalPlan CreateConfiguredTraversalPlan(ulong managerBaseAddress)
    {
        var startNodeAddress = ReadUInt64(managerBaseAddress + _layout.GameObjectManagerActiveNodesOffset);
        return new UnityObjectTraversalPlan(
            managerBaseAddress,
            "ConfiguredManagerBase",
            startNodeAddress,
            _layout.ObjectNodeNextOffset,
            _layout.ObjectNodeGameObjectOffset,
            _layout.GameObjectNamePointerOffset,
            _layout.GameObjectComponentArrayOffset,
            _layout.GameObjectComponentCountOffset);
    }

    private UnityObjectTraversalPlan CreateTraversalPlan(GameObjectManagerProbeResult candidate)
    {
        var interpretation = string.IsNullOrWhiteSpace(candidate.Interpretation)
            ? "ManagerBase"
            : candidate.Interpretation;
        var startNodeAddress = string.Equals(interpretation, "NodePointer", StringComparison.OrdinalIgnoreCase)
            ? candidate.ManagerAddress
            : candidate.ManagerFieldValue ?? ReadUInt64(candidate.ManagerAddress + _layout.GameObjectManagerActiveNodesOffset);

        return new UnityObjectTraversalPlan(
            candidate.ManagerAddress,
            interpretation,
            startNodeAddress,
            _layout.ObjectNodeNextOffset,
            candidate.NodeFieldOffset ?? _layout.ObjectNodeGameObjectOffset,
            candidate.NamePointerOffset ?? _layout.GameObjectNamePointerOffset,
            candidate.ComponentArrayOffset ?? _layout.GameObjectComponentArrayOffset,
            candidate.ComponentCountOffset ?? _layout.GameObjectComponentCountOffset);
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
            PlausibleClassEntries = bestAttempt.PlausibleClassEntries,
            MonoBehaviourDerivedEntries = bestAttempt.MonoBehaviourDerivedEntries,
            ComponentArrayOffset = bestAttempt.ComponentArrayOffset,
            ComponentCountOffset = bestAttempt.ComponentCountOffset,
            TraversedNodeCount = bestAttempt.TraversedNodeCount,
            CoherentNodeCount = bestAttempt.CoherentNodeCount,
            BackLinkedNodeCount = bestAttempt.BackLinkedNodeCount,
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

            var nodeTopology = InspectNodeTopology(nodeAddress, nodeGameObjectOffset, observations);
            var nameProbe = ProbeGameObjectName(gameObjectAddress, observations);
            var componentSummary = ReadComponentSummary(gameObjectAddress);
            var hasSemanticComponentEvidence =
                componentSummary.ValidComponentEntries > 0 ||
                componentSummary.PlausibleClassEntries > 0 ||
                componentSummary.MonoBehaviourDerivedEntries > 0;
            var hasTopologyEvidence =
                nodeTopology.CoherentNodeCount >= 2 ||
                nodeTopology.BackLinkedNodeCount > 0;

            var score = 1;
            if (nextNodeValue == 0 || IsLikelyPointer(nextNodeValue))
            {
                score += 1;
            }

            if (nodeTopology.CoherentNodeCount >= 2)
            {
                score += 2;
            }

            if (nodeTopology.BackLinkedNodeCount > 0)
            {
                score += 2;
            }

            if (nameProbe.NamePointerRead)
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

            if (componentSummary.PlausibleClassEntries > 0)
            {
                score += 2;
            }

            if (componentSummary.MonoBehaviourDerivedEntries > 0)
            {
                score += 1;
            }

            var isValid = score >= 5 && (hasSemanticComponentEvidence || hasTopologyEvidence || nameProbe.HasPlausibleName);
            var sampleNames = nameProbe.HasPlausibleName
                ? nameProbe.ObjectName
                : componentSummary.ValidComponentEntries > 0
                    ? $"components={componentSummary.ValidComponentEntries}/{componentSummary.DeclaredComponentCount}"
                    : nodeTopology.SampleNodes;

            var failureReason = isValid
                ? string.Empty
                : !string.IsNullOrWhiteSpace(nameProbe.FailureReason)
                    ? nameProbe.FailureReason
                    : componentSummary.IsCountPlausible
                        ? "组件数组结构合理，但未读到可用对象名。"
                        : "GameObject 可读，但对象名和组件数组都不够可信。";

            if (!isValid)
            {
                failureReason = hasTopologyEvidence
                    ? "Node chain was readable, but GameObject/component semantics were too weak."
                    : componentSummary.IsCountPlausible
                        ? "Component array was readable, but node topology and class evidence were too weak."
                        : !string.IsNullOrWhiteSpace(nameProbe.FailureReason)
                            ? nameProbe.FailureReason
                            : "GameObject pointer was readable, but topology, object name, and component structure all looked weak.";
            }

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
                componentSummary.IsCountPlausible ? componentSummary.PlausibleClassEntries : null,
                componentSummary.IsCountPlausible ? componentSummary.MonoBehaviourDerivedEntries : null,
                componentSummary.ComponentArrayOffset,
                componentSummary.ComponentCountOffset,
                nodeTopology.TraversedNodeCount,
                nodeTopology.CoherentNodeCount,
                nodeTopology.BackLinkedNodeCount,
                componentSummary.SampleComponentPointers,
                observations.ToArray());

            bestAttempt = ChooseBetterAttempt(bestAttempt, attempt);
        }

        return bestAttempt;
    }

    private NodeTopologySummary InspectNodeTopology(
        ulong startNodeAddress,
        ulong nodeGameObjectOffset,
        List<GameObjectManagerProbeObservation> observations)
    {
        var traversedNodeCount = 0;
        var coherentNodeCount = 0;
        var backLinkedNodeCount = 0;
        var samples = new List<string>();
        var seenNodes = new HashSet<ulong>();
        var currentNodeAddress = startNodeAddress;

        while (currentNodeAddress != 0 && traversedNodeCount < 4 && seenNodes.Add(currentNodeAddress))
        {
            traversedNodeCount++;

            var nextRead = TryReadUInt64(currentNodeAddress + _layout.ObjectNodeNextOffset, out var nextNodeAddress);
            var prevRead = TryReadUInt64(currentNodeAddress + _layout.ObjectNodePreviousOffset, out var previousNodeAddress);
            var gameObjectRead = TryReadUInt64(currentNodeAddress + nodeGameObjectOffset, out var currentGameObjectAddress);

            var nextLooksValid = !nextRead || nextNodeAddress == 0 || IsLikelyPointer(nextNodeAddress);
            var previousLooksValid = !prevRead || previousNodeAddress == 0 || IsLikelyPointer(previousNodeAddress);
            var gameObjectLooksValid = gameObjectRead && IsLikelyPointer(currentGameObjectAddress);

            if (nextLooksValid && previousLooksValid && gameObjectLooksValid)
            {
                coherentNodeCount++;
            }

            if (gameObjectLooksValid && samples.Count < 3)
            {
                samples.Add($"node=0x{currentNodeAddress:X} go=0x{currentGameObjectAddress:X}");
            }

            if (traversedNodeCount <= 2)
            {
                observations.Add(
                    CreateObservation(
                        "topology",
                        $"topology.node[{traversedNodeCount}].prev",
                        currentNodeAddress + _layout.ObjectNodePreviousOffset,
                        prevRead ? previousNodeAddress : null,
                        previousLooksValid,
                        note: prevRead ? string.Empty : "read failed"));
                observations.Add(
                    CreateObservation(
                        "topology",
                        $"topology.node[{traversedNodeCount}].next",
                        currentNodeAddress + _layout.ObjectNodeNextOffset,
                        nextRead ? nextNodeAddress : null,
                        nextLooksValid,
                        note: nextRead ? string.Empty : "read failed"));
            }

            if (!nextRead || nextNodeAddress == 0 || !IsLikelyPointer(nextNodeAddress))
            {
                break;
            }

            if (TryReadUInt64(nextNodeAddress + _layout.ObjectNodePreviousOffset, out var nextPreviousNodeAddress) &&
                nextPreviousNodeAddress == currentNodeAddress)
            {
                backLinkedNodeCount++;
                if (backLinkedNodeCount <= 2)
                {
                    observations.Add(
                        CreateObservation(
                            "topology",
                            $"topology.next[{backLinkedNodeCount}].prev",
                            nextNodeAddress + _layout.ObjectNodePreviousOffset,
                            nextPreviousNodeAddress,
                            looksLikePointer: true,
                            note: "backlink"));
                }
            }

            currentNodeAddress = nextNodeAddress;
        }

        return new NodeTopologySummary(
            traversedNodeCount,
            coherentNodeCount,
            backLinkedNodeCount,
            string.Join(", ", samples));
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
        return ReadComponentProbe(gameObjectAddress).Summary;
    }

    private List<UnityComponentInfo> ReadComponents(
        ulong gameObjectAddress,
        out int declaredComponentCount,
        out bool isCountPlausible)
    {
        var probe = ReadComponentProbe(gameObjectAddress);
        declaredComponentCount = probe.Summary.DeclaredComponentCount;
        isCountPlausible = probe.Summary.IsCountPlausible;
        return probe.Components;
    }

    private ComponentProbeResult ReadComponentProbe(ulong gameObjectAddress)
    {
        var best = ComponentProbeResult.Invalid;

        foreach (var componentArrayOffset in GetGameObjectComponentArrayOffsets())
        {
            foreach (var componentCountOffset in GetGameObjectComponentCountOffsets())
            {
                if (componentArrayOffset == componentCountOffset)
                {
                    continue;
                }

                var probe = TryProbeComponents(gameObjectAddress, componentArrayOffset, componentCountOffset);
                best = ChooseBetterComponentProbe(best, probe);
            }
        }

        return best;
    }

    private ComponentProbeResult TryProbeComponents(
        ulong gameObjectAddress,
        ulong componentArrayOffset,
        ulong componentCountOffset)
    {
        if (!TryReadUInt64(gameObjectAddress + componentArrayOffset, out var componentArray) ||
            !IsLikelyPointer(componentArray))
        {
            return ComponentProbeResult.Invalid;
        }

        if (!TryReadInt32(gameObjectAddress + componentCountOffset, out var componentCount))
        {
            return ComponentProbeResult.Invalid;
        }

        if (componentCount <= 0 || componentCount > _layout.MaxComponentCount)
        {
            return new ComponentProbeResult(
                new ComponentSummary(
                    componentCount,
                    0,
                    0,
                    0,
                    string.Empty,
                    false,
                    componentArrayOffset,
                    componentCountOffset),
                new List<UnityComponentInfo>());
        }

        var components = new List<UnityComponentInfo>();
        var plausibleClassEntries = 0;
        var monoBehaviourDerivedEntries = 0;

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
            if (classInfo.ClassTopologyIsPlausible)
            {
                plausibleClassEntries++;
            }

            if (classInfo.IsMonoBehaviourDerived)
            {
                monoBehaviourDerivedEntries++;
            }

            components.Add(
                new UnityComponentInfo(
                    componentPointer,
                    klassPointer,
                    ownerGameObject,
                    classInfo.Name,
                    classInfo.Namespace,
                    classInfo.IsMonoBehaviourDerived));
        }

        var samplePointers = components
            .Take(3)
            .Select(component => component.GetSampleLabel())
            .ToArray();

        return new ComponentProbeResult(
            new ComponentSummary(
                componentCount,
                components.Count,
                plausibleClassEntries,
                monoBehaviourDerivedEntries,
                string.Join(", ", samplePointers),
                true,
                componentArrayOffset,
                componentCountOffset),
            components);
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
        var classTopology = InspectClassTopology(klassPointer);

        return new UnityIl2CppClassInfo(
            klassPointer,
            classNamePointer,
            namespacePointer,
            className,
            namespaceName,
            classTopology.IsMonoBehaviourDerived,
            classTopology.IsPlausible,
            classTopology.ParentDepth);
    }

    private ClassTopologySummary InspectClassTopology(ulong klassPointer)
    {
        var visited = new HashSet<ulong>();
        var current = klassPointer;
        var parentDepth = 0;
        var readableNameCount = 0;

        for (; parentDepth < _layout.MaxClassParentDepth && IsLikelyPointer(current) && visited.Add(current); parentDepth++)
        {
            TryReadUInt64(current + _layout.Il2CppClassNamePointerOffset, out var namePointer);
            TryReadUInt64(current + _layout.Il2CppClassNamespacePointerOffset, out var namespacePointer);
            var className = ReadString(namePointer);
            var namespaceName = ReadString(namespacePointer);

            if (!string.IsNullOrWhiteSpace(className))
            {
                readableNameCount++;
            }

            if (string.Equals(className, "MonoBehaviour", StringComparison.Ordinal) &&
                string.Equals(namespaceName, "UnityEngine", StringComparison.Ordinal))
            {
                return new ClassTopologySummary(parentDepth + 1, readableNameCount, true, true);
            }

            if (!TryReadUInt64(current + _layout.Il2CppClassParentPointerOffset, out current))
            {
                break;
            }
        }

        var isPlausible = parentDepth >= 1 && (readableNameCount > 0 || parentDepth >= 2);
        return new ClassTopologySummary(parentDepth, readableNameCount, false, isPlausible);
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

    private IReadOnlyList<ulong> GetGameObjectComponentArrayOffsets() =>
        GetUniqueOffsets(_layout.GameObjectComponentArrayOffset, 0x28, 0x30, 0x38);

    private IReadOnlyList<ulong> GetGameObjectComponentCountOffsets() =>
        GetUniqueOffsets(_layout.GameObjectComponentCountOffset, 0x38, 0x40, 0x48);

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

    private static ComponentProbeResult ChooseBetterComponentProbe(ComponentProbeResult current, ComponentProbeResult candidate)
    {
        if (candidate.Summary.IsSemanticEvidenceStrong && !current.Summary.IsSemanticEvidenceStrong)
        {
            return candidate;
        }

        if (candidate.Summary.IsSemanticEvidenceStrong == current.Summary.IsSemanticEvidenceStrong &&
            candidate.Summary.SemanticScore > current.Summary.SemanticScore)
        {
            return candidate;
        }

        if (!current.Summary.IsCountPlausible && candidate.Summary.IsCountPlausible)
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
        int PlausibleClassEntries,
        int MonoBehaviourDerivedEntries,
        string SampleComponentPointers,
        bool IsCountPlausible,
        ulong? ComponentArrayOffset,
        ulong? ComponentCountOffset)
    {
        public int SemanticScore => ValidComponentEntries + (PlausibleClassEntries * 2) + MonoBehaviourDerivedEntries;

        public bool IsSemanticEvidenceStrong => PlausibleClassEntries > 0 || MonoBehaviourDerivedEntries > 0 || ValidComponentEntries > 0;

        public static ComponentSummary Invalid { get; } = new(0, 0, 0, 0, string.Empty, false, null, null);
    }

    private readonly record struct ComponentProbeResult(ComponentSummary Summary, List<UnityComponentInfo> Components)
    {
        public static ComponentProbeResult Invalid { get; } = new(ComponentSummary.Invalid, new List<UnityComponentInfo>());
    }

    private readonly record struct UnityIl2CppClassInfo(
        ulong KlassPointer,
        ulong NamePointer,
        ulong NamespacePointer,
        string Name,
        string Namespace,
        bool IsMonoBehaviourDerived,
        bool ClassTopologyIsPlausible,
        int ParentDepth)
    {
        public static UnityIl2CppClassInfo Invalid { get; } = new(0, 0, 0, string.Empty, string.Empty, false, false, 0);
    }

    private readonly record struct ClassTopologySummary(
        int ParentDepth,
        int ReadableNameCount,
        bool IsMonoBehaviourDerived,
        bool IsPlausible);

    private readonly record struct NodeTopologySummary(
        int TraversedNodeCount,
        int CoherentNodeCount,
        int BackLinkedNodeCount,
        string SampleNodes);

    private readonly record struct UnityObjectTraversalPlan(
        ulong SourceAddress,
        string Interpretation,
        ulong StartNodeAddress,
        ulong NodeNextOffset,
        ulong NodeGameObjectOffset,
        ulong GameObjectNamePointerOffset,
        ulong GameObjectComponentArrayOffset,
        ulong GameObjectComponentCountOffset);

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
        int? PlausibleClassEntries,
        int? MonoBehaviourDerivedEntries,
        ulong? ComponentArrayOffset,
        ulong? ComponentCountOffset,
        int? TraversedNodeCount,
        int? CoherentNodeCount,
        int? BackLinkedNodeCount,
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
                null,
                null,
                null,
                null,
                null,
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

    public int? PlausibleClassEntries { get; init; }

    public int? MonoBehaviourDerivedEntries { get; init; }

    public ulong? ComponentArrayOffset { get; init; }

    public ulong? ComponentCountOffset { get; init; }

    public int? TraversedNodeCount { get; init; }

    public int? CoherentNodeCount { get; init; }

    public int? BackLinkedNodeCount { get; init; }

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
