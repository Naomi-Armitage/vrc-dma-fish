using System.Net.Sockets;
using System.Text;

namespace VrcDmaFish.Inputs;

public sealed class KmboxNetInputController : IInputController
{
    private UdpClient? _client;
    private string _ip;
    private int _port;

    public KmboxNetInputController(string ip, int port)
    {
        _ip = ip;
        _port = port;
        _client = new UdpClient();
    }

    private void Send(string cmd)
    {
        try {
            byte[] data = Encoding.ASCII.GetBytes(cmd + "\r\n");
            _client?.Send(data, data.Length, _ip, _port);
        } catch { }
    }

    public void BeginCast() { Send("km.left(1)"); }
    public void EndCast() { Send("km.left(0)"); }
    public void ReelPulse(int durationMs)
    {
        Send("km.left(1)");
        Thread.Sleep(durationMs);
        Send("km.left(0)");
    }
    public void Wait(int ms) { Thread.Sleep(ms); }
    public void ClickLeft() { Send("km.click(1)"); }
    public void Move(int x, int y) { Send($"km.move({x},{y})"); }
    public void Dispose() => _client?.Dispose();
}
