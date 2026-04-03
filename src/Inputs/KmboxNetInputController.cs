using System.Net.Sockets;
using System.Text;
using VrcDmaFish.UI;

namespace VrcDmaFish.Inputs;

public sealed class KmboxNetInputController : IInputController
{
    private readonly UdpClient _client = new();
    private readonly string _ip;
    private readonly int _port;

    public KmboxNetInputController(string ip, int port)
    {
        _ip = ip;
        _port = port;
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

    public void Dispose() => _client.Dispose();

    private void Send(string cmd)
    {
        try
        {
            var data = Encoding.ASCII.GetBytes(cmd + "\r\n");
            _client.Send(data, data.Length, _ip, _port);
        }
        catch (Exception ex)
        {
            Logger.Warn("输入", $"UDP 发送失败 {_ip}:{_port}: {ex.Message}");
        }
    }

    private void MouseDown() => Send("km.left(1)");

    private void MouseUp() => Send("km.left(0)");
}
