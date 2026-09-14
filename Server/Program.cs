using System.Net.Sockets;
using UdpBasics.Protocol;

const int port = 7777;
using var udp = new UdpClient(port);
using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

var state = new PlayerState(0, 0, 0, 0);
Console.WriteLine($"Dedicated authoritative UDP server: 0.0.0.0:{port}");
Console.WriteLine("Press Ctrl+C to stop.");

while (!shutdown.IsCancellationRequested)
{
    try
    {
        var received = await udp.ReceiveAsync(shutdown.Token);
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        if (!Packet.TryDeserialize(received.Buffer, out var packet, out var error))
        {
            Console.WriteLine($"[{timestamp}] {received.RemoteEndPoint} INVALID: {error}");
            var invalidReply = Packet.Error(0, error);
            await udp.SendAsync(invalidReply.Serialize(), received.RemoteEndPoint, shutdown.Token);
            continue;
        }

        Packet reply;
        string commandDetails;

        switch (packet!.Type)
        {
            case PacketType.Movement:
            {
                var delta = packet.ReadMovement();
                state = state with
                {
                    X = state.X + delta.X,
                    Y = state.Y + delta.Y,
                    Z = state.Z + delta.Z
                };
                commandDetails = $"MOVEMENT delta=({delta.X:F2}, {delta.Y:F2}, {delta.Z:F2})";
                reply = Packet.State(packet.SequenceNumber, state);
                break;
            }
            case PacketType.Shoot:
            {
                var weaponId = packet.ReadWeaponId();
                state = state with { Shots = state.Shots + 1 };
                commandDetails = $"SHOOT weaponId={weaponId}";
                reply = Packet.State(packet.SequenceNumber, state);
                break;
            }
            default:
                commandDetails = $"UNSUPPORTED type={(byte)packet.Type}";
                reply = Packet.Error(packet.SequenceNumber, commandDetails);
                break;
        }

        Console.WriteLine(
            $"[{timestamp}] {received.RemoteEndPoint} seq={packet.SequenceNumber} {commandDetails}");
        await udp.SendAsync(reply.Serialize(), received.RemoteEndPoint, shutdown.Token);
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
        break;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"ERROR: {exception.Message}");
    }
}

Console.WriteLine("Server stopped.");
