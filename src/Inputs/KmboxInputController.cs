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
            Logger.Warn("输入", "未能解析到可用的串口 KMBOX 端口。");
            return;
        }

        try
        {
            _port = new SerialPort(resolvedPort, baudRate);
            _port.Open();
            Logger.Info("输入", $"串口 KMBOX 已连接：{resolvedPort} @ {baudRate}。");
        }
        catch (Exception ex)
        {
            Logger.Warn("输入", $"打开串口控制器失败 {resolvedPort}: {ex.Message}");
        }
    }

    public void BeginCast() => MouseDown();

    public void EndCast() => MouseUp();

    public void Click(int durationMs)
    {
        MouseDown();
        Thread.Sleep(Math.Max(0, durationMs));
        MouseUp();
    }

    public void ReelPulse(int durationMs)
    {
        MouseDown();
        Thread.Sleep(Math.Max(0, durationMs));
        MouseUp();
    }

    public void ReleaseReel() => MouseUp();

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
            Logger.Warn("输入", $"串口写入失败：{ex.Message}");
        }
    }

    private void MouseDown() => Send("km.left(1)");

    private void MouseUp() => Send("km.left(0)");

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
