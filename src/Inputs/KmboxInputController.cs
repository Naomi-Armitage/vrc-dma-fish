using System.IO.Ports;

namespace VrcDmaFish.Inputs;

public sealed class KmboxInputController : IInputController
{
    private SerialPort? _port;

    public KmboxInputController(string portName, int baudRate)
    {
        try {
            _port = new SerialPort(portName, baudRate);
            _port.Open();
        } catch { }
    }

    public void BeginCast() { _port?.Write("km.left(1)\r\n"); }
    public void EndCast() { _port?.Write("km.left(0)\r\n"); }
    public void ReelPulse(int durationMs) 
    { 
        _port?.Write("km.left(1)\r\n"); 
        Thread.Sleep(durationMs); 
        _port?.Write("km.left(0)\r\n"); 
    }
    public void Wait(int ms) { Thread.Sleep(ms); }
    public void ClickLeft() { _port?.Write("km.click(1)\r\n"); }
    public void Move(int x, int y) { _port?.Write($"km.move({x},{y})\r\n"); }
    public void Dispose() => _port?.Dispose();
}
