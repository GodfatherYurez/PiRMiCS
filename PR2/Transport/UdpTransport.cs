using System.Net;
using System.Net.Sockets;

namespace UdpTelemetry.Transport;

public sealed class UdpTransport : IDisposable
{
    // Обертка изолирует низкоуровневую работу с UdpClient от логики эксперимента.
    private readonly UdpClient udp;

    public UdpTransport(int port = 0)
    {
        // Порт 0 означает: ОС сама выбирает свободный локальный порт для клиента.
        udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
    }

    public int LocalPort => ((IPEndPoint)udp.Client.LocalEndPoint!).Port;

    public async Task<int> SendAsync(byte[] data, IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        // Проверка токена нужна до начала отправки, так как используемый overload ее не принимает.
        cancellationToken.ThrowIfCancellationRequested();
        return await udp.SendAsync(data, data.Length, endpoint);
    }

    public Task<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken) =>
        udp.ReceiveAsync(cancellationToken).AsTask();

    public void Dispose() => udp.Dispose();
}
