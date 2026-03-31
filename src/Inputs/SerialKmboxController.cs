using System.IO.Ports;

namespace VrcDmaFish.Inputs;

public sealed class SerialKmboxController : IInputController, IDisposable
{
    private readonly SerialPort? _port;

    public SerialKmboxController(string portName, int baudRate = 115200)
    {
        if (string.IsNullOrEmpty(portName) || portName.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Info("KMBOX", "正在自动扫描串口... (๑•̀ㅂ•́)و✧");
            var ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                Logger.Warn("KMBOX", "没找到任何串口喵！请检查盒子是否插好喵。");
                return;
            }
            portName = ports[0]; // 默认取第一个喵
            Logger.Info("KMBOX", $"自动选择了串口: {portName}");
        }

        try {
            _port = new SerialPort(portName, baudRate);
            _port.Open();
            Logger.Info("KMBOX", $"Serial B+ connected on {portName} @ {baudRate}bps");
        } catch (Exception ex) {
            Logger.Error("KMBOX", $"串口连接失败: {ex.Message}");
        }
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

    private void Send(string cmd) 
    {
        if (_port?.IsOpen == true)
            _port.Write($"{cmd}\r\n");
    }

    public void Dispose() => _port?.Dispose();
}
