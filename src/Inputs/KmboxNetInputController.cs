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

    public void ClickLeft()
    {
        byte[] data = Encoding.ASCII.GetBytes("km.click(1)\r\n");
        _client?.Send(data, data.Length, _ip, _port);
    }

    public void Move(int x, int y)
    {
        byte[] data = Encoding.ASCII.GetBytes($"km.move({x},{y})\r\n");
        _client?.Send(data, data.Length, _ip, _port);
    }

    public void Dispose() => _client?.Dispose();
}
