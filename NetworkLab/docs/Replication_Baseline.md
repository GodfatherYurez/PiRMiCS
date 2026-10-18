# Replication baseline

## Scenario

- Engine: Unreal Engine 5.8.3.
- Network: localhost Listen Server plus one client.
- Actors: 50 visible, always-relevant replicated marker actors (10 stationary, 40 moving).
- Capture: the server waits for both players, allows a 5-second warm-up, then records for 20 seconds using Network Profiler.
- Starting condition: baseline mode, all markers awake, `NetUpdateFrequency = 30`.
- Server log: `../Saved/Logs/NetworkLab_BaselineServer_Metric.log`.
- Raw Network Profiler capture: `media/replication_baseline.nprof` (1,022,770 bytes).
- Traffic counter: summed `OutTotalBytes` for server client connections at the start/end of the same 20-second sampling period.

The measured run completed on 2026-10-18. The `.nprof` capture is included in this folder. The server log is retained under `Saved/Logs/NetworkLab_BaselineServer_Metric.log` in the project directory.

The run log confirms a successful client join, 50 spawned marker actors, and a completed Network Profiler capture. Record server outgoing KB/s, total bytes, actor count, and top actors in this document after opening the capture in the Unreal Network Profiler. Keep both runs on the same client count and capture duration.

| Metric | Baseline |
| --- | ---: |
| Server outgoing traffic (KB/s) | 19.51 |
| Total outgoing traffic (bytes) | 399,528 |
| Replicated markers | 50 |
| Sampling duration | 20 s |
| Highest-traffic actors/RPCs/properties | Pending inspection in Network Profiler viewer |

The engine installation on this computer does not include the Network Profiler viewer. The exact server traffic value is measured from the connection byte counters; the raw `.nprof` capture is included so actor-level breakdown can be opened in the Network Profiler viewer later.
