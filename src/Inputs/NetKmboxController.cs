using System.Net;
using System.Net.Sockets;

namespace VrcDmaFish.Inputs;

public sealed class NetKmboxController : IInputController, IDisposable
{
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _endPoint;

    public NetKmboxController(string ip, int port)
    {
        _udpClient = new UdpClient();
        _endPoint = new IPEndPoint(IPAddress.Parse(ip), port);
        Logger.Info("KMBOX", $"Net Controller initialized targeting {ip}:{port}");
    }

    public void BeginCast() => SendClick(1); // 1 = Left Down
    public void EndCast() => SendClick(0);   // 0 = Left Up

    public void ReelPulse(int durationMs)
    {
        SendClick(1);
        Thread.Sleep(durationMs);
        SendClick(0);
    }

    public void Wait(int durationMs) => Thread.Sleep(durationMs);

    private void SendClick(int state)
    {
        // 这里的二进制协议参考了标准的 KMBOX NET UDP 封包
        // 简化版示例，实际使用时需要根据具体固件版本调整 Offset 和 Header 喵
        byte[] packet = new byte[12];
        packet[0] = 0x55; // Header
        packet[1] = 0xAA;
        packet[4] = (byte)(state == 1 ? 0x01 : 0x02); // 模拟左键动作
        _udpClient.Send(packet, packet.Length, _endPoint);
    }

    public void Dispose() => _udpClient.Dispose();
}
