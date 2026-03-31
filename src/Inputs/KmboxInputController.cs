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
        } catch { /* Handle in logs */ }
    }

    public void ClickLeft()
    {
        _port?.Write("km.click(1)\r\n");
    }

    public void Move(int x, int y)
    {
        _port?.Write($"km.move({x},{y})\r\n");
    }

    public void Dispose() => _port?.Dispose();
}
