using System.Runtime.Versioning;
using Vmmsharp;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

[SupportedOSPlatform("windows")]
public sealed class UnityScanner
{
    private readonly VmmProcess _process;

    public UnityScanner(VmmProcess process)
    {
        _process = process;
    }

    public ulong FindGameObjectManager()
    {
        var unityPlayerBase = _process.GetModuleBase("UnityPlayer.dll");
        if (unityPlayerBase == 0)
        {
            Logger.Warn("SCAN", "UnityPlayer.dll was not found in the target process.");
            return 0;
        }

        Logger.Warn("SCAN", "Automatic Unity scanning is not implemented in this build. Configure SignalSource.TargetObjectAddress explicitly.");
        return 0;
    }

    public ulong FindObjectByName(string name)
    {
        Logger.Warn("SCAN", $"Auto-locating '{name}' is disabled until a reliable signature scan is added.");
        return FindGameObjectManager();
    }
}
