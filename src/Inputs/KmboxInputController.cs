using System.IO.Ports;
using VrcDmaFish.UI;

namespace VrcDmaFish.Inputs;

public sealed class KmboxInputController : IInputController
{
    private readonly SerialPort? _port;

    public KmboxInputController(string portName, int baudRate)
    {
        var resolvedPort = ResolvePortName(portName);
        if (string.IsNullOrWhiteSpace(resolvedPort))
        {
            Logger.Warn("INPUT", "No serial KMBOX port could be resolved.");
            return;
        }

        try
        {
            _port = new SerialPort(resolvedPort, baudRate);
            _port.Open();
            Logger.Info("INPUT", $"Serial KMBOX connected on {resolvedPort} @ {baudRate}.");
        }
        catch (Exception ex)
        {
            Logger.Warn("INPUT", $"Failed to open serial controller on {resolvedPort}: {ex.Message}");
        }
    }

    public void BeginCast() => Send("km.left(1)");

    public void EndCast() => Send("km.left(0)");

    public void ReelPulse(int durationMs)
    {
        Send("km.left(1)");
        Thread.Sleep(Math.Max(0, durationMs));
        Send("km.left(0)");
    }

    public void Wait(int ms) => Thread.Sleep(Math.Max(0, ms));

    public void Dispose()
    {
        if (_port is null)
        {
            return;
        }

        try
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }
        }
        catch
        {
        }

        _port.Dispose();
    }

    private void Send(string command)
    {
        if (_port is null)
        {
            return;
        }

        try
        {
            _port.Write(command + "\r\n");
        }
        catch (Exception ex)
        {
            Logger.Warn("INPUT", $"Serial write failed: {ex.Message}");
        }
    }

    private static string? ResolvePortName(string? portName)
    {
        if (!string.IsNullOrWhiteSpace(portName) &&
            !string.Equals(portName, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return portName;
        }

        return SerialPort.GetPortNames()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
