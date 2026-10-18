# Replication optimization

## Implemented techniques

1. **Net Dormancy:** the 10 stationary marker actors enter `DORM_DormantAll` in optimized mode; they wake in baseline mode. They keep their initial actor state and stop sending their periodic pulse property while dormant.
2. **Lower update frequency:** moving markers change from 30 updates per second to 5 in optimized mode. Every actor remains relevant to both players, so the test measures update rate and dormancy rather than visibility culling.
3. **Quantized location:** moving marker locations use `FVector_NetQuantize10` in both profiles to limit the size of position data. This shared setting keeps the before/after comparison focused on the first two techniques.

Start an optimized capture by launching the Listen Server with `-NetworkLabOptimized -NetworkLabCapture`; omit `-NetworkLabOptimized` for baseline. Connect one client to each fresh server and retain the `.nprof` output from `Saved/Profiling`. The in-game F2 key toggles baseline and optimized modes without restarting.

## Comparison

| Metric | Before | After | Change |
| --- | ---: | ---: | ---: |
| Server outgoing traffic (KB/s) | 19.51 | 7.15 | 63.3% lower |
| Total outgoing traffic (bytes) | 399,528 | 146,467 | 63.3% lower |
| Replicated markers | 50 | 50 | 0 |
| Sampling duration | 20 s | 20 s | Same scenario |

Both runs completed on 2026-10-18 with one localhost client, 50 replicated markers, and a 20-second sample after a 5-second warm-up. Raw Network Profiler captures are in `media/replication_baseline.nprof` and `media/replication_optimized.nprof`. This Unreal installation does not include the Network Profiler viewer, so the actor-level breakdown remains to be opened there. The KB/s figures above use the same server connection byte counters and 20-second sample for both runs. The optimized run met the practical's approximate 40–60% reduction target (63.3%).
