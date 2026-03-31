using System.IO.Ports;

namespace VrcDmaFish.Inputs;

public sealed class SerialKmboxController : IInputController, IDisposable
{
    private readonly SerialPort _port;

    public SerialKmboxController(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(portName, baudRate);
        _port.Open();
        Logger.Info("KMBOX", $"Serial B+ connected on {portName}");
    }

    public void BeginCast() => Send("km.left(1)");
    public void EndCast() => Send("km.left(0)");
    public void ReelPulse(int durationMs)
    {
        Send("km.left(1)");
        Thread.Sleep(durationMs);
        Send("km.left(0)");
    }

    public void Wait(int durationMs) => Thread.Sleep(durationMs);

    private void Send(string cmd) => _port.Write($"{cmd}\r\n");

    public void Dispose() => _port.Dispose();
}
