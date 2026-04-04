using System.Runtime.Versioning;
using Vmmsharp;

namespace VrcDmaFish.Providers;

public interface IProcessMemory : IDisposable
{
    string Name { get; }

    int ProcessId { get; }

    bool IsValid { get; }

    ulong GetModuleBase(string moduleName);

    IReadOnlyList<ulong> Search(ulong startAddress, ulong endAddress, int alignment, byte[] patternBytes, byte[] wildcardMask, int resultLimit);

    bool TryReadBytes(ulong address, int length, out byte[] bytes);
}

 [SupportedOSPlatform("windows")]
public sealed class VmmProcessMemory : IProcessMemory
{
    private readonly VmmProcess _process;

    public VmmProcessMemory(VmmProcess process)
    {
        _process = process;
    }

    public string Name => _process.Name;

    public int ProcessId => (int)_process.PID;

    public bool IsValid => _process.IsValid;

    public ulong GetModuleBase(string moduleName) => _process.GetModuleBase(moduleName);

    public IReadOnlyList<ulong> Search(ulong startAddress, ulong endAddress, int alignment, byte[] patternBytes, byte[] wildcardMask, int resultLimit)
    {
        using var search = _process.Search(startAddress, endAddress, (uint)alignment, Vmm.FLAG_NOCACHE);
        search.AddSearch(patternBytes, wildcardMask, (uint)resultLimit);
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

    public bool TryReadBytes(ulong address, int length, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        try
        {
            var read = _process.MemRead(address, (uint)length, Vmm.FLAG_NOCACHE);
            if (read.Length != length)
            {
                return false;
            }

            bytes = read;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
    }
}

public sealed class RecordingProcessMemory : IProcessMemory
{
    private readonly IProcessMemory _inner;
    private readonly Dictionary<ulong, byte[]> _recordedBlocks = new();

    public RecordingProcessMemory(IProcessMemory inner)
    {
        _inner = inner;
    }

    public string Name => _inner.Name;

    public int ProcessId => _inner.ProcessId;

    public bool IsValid => _inner.IsValid;

    public ulong GetModuleBase(string moduleName) => _inner.GetModuleBase(moduleName);

    public IReadOnlyList<ulong> Search(ulong startAddress, ulong endAddress, int alignment, byte[] patternBytes, byte[] wildcardMask, int resultLimit) =>
        _inner.Search(startAddress, endAddress, alignment, patternBytes, wildcardMask, resultLimit);

    public bool TryReadBytes(ulong address, int length, out byte[] bytes)
    {
        if (!_inner.TryReadBytes(address, length, out bytes))
        {
            return false;
        }

        if (_recordedBlocks.TryGetValue(address, out var existing))
        {
            if (existing.Length < bytes.Length)
            {
                _recordedBlocks[address] = bytes.ToArray();
            }
        }
        else
        {
            _recordedBlocks[address] = bytes.ToArray();
        }

        return true;
    }

    public RecordedMemoryBlock[] ExportBlocks()
    {
        return _recordedBlocks
            .OrderBy(entry => entry.Key)
            .Select(entry => new RecordedMemoryBlock(entry.Key, Convert.ToHexString(entry.Value)))
            .ToArray();
    }

    public void Dispose()
    {
    }
}

public sealed class ReplayProcessMemory : IProcessMemory
{
    private readonly ReplayMemoryRegion[] _regions;
    private readonly Dictionary<string, ulong> _moduleBases;

    public ReplayProcessMemory(IEnumerable<RecordedMemoryBlock> blocks, IReadOnlyDictionary<string, ulong>? moduleBases = null)
    {
        _regions = blocks
            .Select(block => new ReplayMemoryRegion(block.Address, Convert.FromHexString(block.HexData)))
            .OrderBy(region => region.Address)
            .ToArray();
        _moduleBases = moduleBases is null
            ? new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ulong>(moduleBases, StringComparer.OrdinalIgnoreCase);
    }

    public string Name => "Replay";

    public int ProcessId => 0;

    public bool IsValid => true;

    public ulong GetModuleBase(string moduleName) =>
        _moduleBases.TryGetValue(moduleName, out var baseAddress) ? baseAddress : 0;

    public IReadOnlyList<ulong> Search(ulong startAddress, ulong endAddress, int alignment, byte[] patternBytes, byte[] wildcardMask, int resultLimit) =>
        Array.Empty<ulong>();

    public bool TryReadBytes(ulong address, int length, out byte[] bytes)
    {
        foreach (var region in _regions)
        {
            if (address < region.Address)
            {
                break;
            }

            var offset = address - region.Address;
            if (offset > int.MaxValue)
            {
                continue;
            }

            if (offset + (ulong)length > (ulong)region.Data.Length)
            {
                continue;
            }

            bytes = region.Data.AsSpan((int)offset, length).ToArray();
            return true;
        }

        bytes = Array.Empty<byte>();
        return false;
    }

    public void Dispose()
    {
    }

    private readonly record struct ReplayMemoryRegion(ulong Address, byte[] Data);
}

public sealed record RecordedMemoryBlock(ulong Address, string HexData);
