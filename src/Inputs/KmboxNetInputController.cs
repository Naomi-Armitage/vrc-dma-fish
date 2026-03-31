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

    public void BeginCast() => Send("km.left(1)");

    public void EndCast() => Send("km.left(0)");

    public void ReelPulse(int durationMs)
    {
        Send("km.left(1)");
        Thread.Sleep(Math.Max(0, durationMs));
        Send("km.left(0)");
    }

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
            Logger.Warn("INPUT", $"UDP send failed to {_ip}:{_port}: {ex.Message}");
        }
    }
}
