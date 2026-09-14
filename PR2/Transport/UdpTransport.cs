using System.Net;
using System.Net.Sockets;

namespace UdpTelemetry.Transport;

public sealed class UdpTransport : IDisposable
{
    private readonly UdpClient udp;

    public UdpTransport(int port = 0)
    {
        udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
    }

    public int LocalPort => ((IPEndPoint)udp.Client.LocalEndPoint!).Port;

    public async Task<int> SendAsync(byte[] data, IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await udp.SendAsync(data, data.Length, endpoint);
    }

    public Task<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken) =>
        udp.ReceiveAsync(cancellationToken).AsTask();

    public void Dispose() => udp.Dispose();
}