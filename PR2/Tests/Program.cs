using UdpTelemetry.Protocol;
using UdpTelemetry.Telemetry;

Run("PING/PONG round trip", () =>
{
    var ping = Packet.Ping(17, 987654321UL);
    Assert(Packet.TryDeserialize(ping.Serialize(), out var parsedPing, out _), "PING rejected");
    Assert(parsedPing!.ClientSendTimeUs == 987654321UL, "PING timestamp changed");

    var pong = Packet.Pong(17, 987654321UL, 100UL, 200UL);
    Assert(Packet.TryDeserialize(pong.Serialize(), out var parsedPong, out _), "PONG rejected");
    Assert(parsedPong!.ServerReceiveTimeUs == 100UL && parsedPong.ServerSendTimeUs == 200UL,
        "PONG server timestamps changed");
});

Run("Malformed datagrams are rejected", () =>
{
    var bytes = Packet.Ping(1, 100).Serialize();
    Assert(!Packet.TryDeserialize(bytes[..^1], out _, out _), "Truncated packet accepted");
    bytes[8] = 0;
    Assert(!Packet.TryDeserialize(bytes, out _, out _), "Wrong version accepted");
    bytes = Packet.Ping(1, 100).Serialize();
    bytes[6] = 0;
    Assert(!Packet.TryDeserialize(bytes, out _, out _), "Wrong payload size accepted");
});

Run("Telemetry computes RTT and SRTT", () =>
{
    var tracker = new TelemetryTracker();
    Assert(tracker.TrackPing(1, 0, "test", 1), "Ping was not tracked");
    var status = tracker.OnPong(1, 100_000, out var first);
    Assert(status == ResponseStatus.Received && first!.RttMs == 100, "First RTT is wrong");
    Assert(tracker.TrackPing(2, 200_000, "test", 2), "Second ping was not tracked");
    tracker.OnPong(2, 350_000, out var second);
    Assert(second!.SrttMs == 106.25, "SRTT formula is wrong");
    Assert(tracker.MeanJitterMs == 50, "Jitter formula is wrong");
});

Run("Timeout, late, duplicate and unknown states", () =>
{
    var tracker = new TelemetryTracker();
    tracker.TrackPing(1, 0, "test", 1);
    Assert(tracker.Expire(1_000_001).Count == 1, "Timeout was not recorded");
    Assert(tracker.OnPong(1, 1_100_000, out _) == ResponseStatus.LateResponse, "Late response missing");
    Assert(tracker.OnPong(1, 1_200_000, out _) == ResponseStatus.DuplicateResponse, "Duplicate missing");
    Assert(tracker.OnPong(99, 1, out _) == ResponseStatus.UnknownResponse, "Unknown response missing");
});

Console.WriteLine("All PR2 tests passed.");

static void Run(string name, Action test)
{
    test();
    Console.WriteLine($"PASS {name}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}