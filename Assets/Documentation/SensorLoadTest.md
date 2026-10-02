# Sensor Load Test

Measured on 2026-10-02 with Unity 6000.6.3f1, WebGL release settings, on an
Apple M2 MacBook Air (8 GB, 60 Hz display) in Chrome 154 with a fresh
profile. The page was served from localhost, so download time is excluded.
Quality was the default ("Mobile"; HD is opt-in).

## How it runs

`SensorLoadTestHarness` (Assets/LoadTest) is compiled only into the editor and
builds made by `SensorLoadTestBuild.BuildWebGL`, which writes to
`Builds/LoadTest`. Release builds do not contain it.

Open the load-test build with a query string:

```text
index.html?loadtest=30,60,120,250,500,1000,2000&hz=2&alarms=0.05
```

For each level the harness clones the machine's real sensors up to that count
(each clone keeps its template's machine part as its anchor, so it gets a real
card, leader line and history), rebuilds the layout and then sends every
sensor `hz` readings per second through `TelemetryJsonIngestor.PushBatchJson`,
the same path a gateway uses. `alarms` is the fraction of sensors pushed past
their critical limit; the set rotates every 5 seconds, so that fraction of
sensors enters and leaves alarm at once. The camera orbits at 10 degrees per
second. After a 4 second warm-up it records 10 seconds of frame times and logs
one `LOADTEST {json}` line per level.

Scenarios used here: **steady** (`alarms=0`) and **alarm storm**
(`alarms=0.05`), both at 2 readings per sensor per second. Chrome ran with
occlusion and background throttling disabled; a hidden browser pane stops
finishing frames and is not a valid place to measure.

## Results

fps is the average; p99 is the 99th-percentile frame time; worst is the
longest single frame; intake is the time spent in JSON ingestion per second.

### Before (snapshot/pre-load-test), wide layout, 1440x900 window

| Sensors | Steady fps | Steady p99 | Steady intake | Storm fps | Storm p99 | Storm worst |
|---:|---:|---:|---:|---:|---:|---:|
| 30 | 60.0 | 19 ms | 5 ms/s | 59.8 | 19 ms | 38 ms |
| 120 | 60.0 | 19 ms | 7 ms/s | 58.6 | 19 ms | 149 ms |
| 250 | 60.0 | 19 ms | 17 ms/s | 55.7 | 20 ms | 391 ms |
| 500 | 59.0 | 35 ms | 51 ms/s | 48.4 | 44 ms | 866 ms |
| 1,000 | 49.9 | 105 ms | 189 ms/s | 20.2 | 198 ms | 2,434 ms |
| 2,000 | 17.9 | 380 ms | 720 ms/s | | | |

Two costs grew faster than the sensor count:

- `ConnectionHealthMonitor.ReportReading` ran a full refresh for every
  reading. A refresh counts every sensor and redraws the health readout, so a
  batch of N readings cost N x N work.
- Every alarm change called `RebuildLayout()` on the rail managers at once, so
  a batch that moved 50 sensors into or out of alarm rebuilt the layout 50
  times in one frame.

### After (perf/coalesce-telemetry-refresh)

Both are now requested and done once per frame. `ConnectionHealthMonitor.Current`
flushes a pending refresh, so readers never see a stale snapshot.

| Sensors | Steady fps | Steady p99 | Steady intake | Storm fps | Storm p99 | Storm worst |
|---:|---:|---:|---:|---:|---:|---:|
| 30 | 60.0 | 19 ms | 4 ms/s | 59.9 | 19 ms | 35 ms |
| 120 | 60.0 | 19 ms | 4 ms/s | 60.0 | 19 ms | 32 ms |
| 250 | 60.0 | 19 ms | 4 ms/s | 59.7 | 19 ms | 40 ms |
| 500 | 59.8 | 19 ms | 6 ms/s | 59.6 | 19 ms | 58 ms |
| 1,000 | 59.6 | 19 ms | 10 ms/s | 59.3 | 19 ms | 92 ms |
| 2,000 | 59.1 | 25 ms | 18 ms/s | 58.6 | 26 ms | 174 ms |
| 3,000 | | | | 58.7 | 23 ms | 177 ms |
| 5,000 | | | | 54.4 | 44 ms | 440 ms |

The remaining long frames grow with the managed heap (40 MB at 1,000
sensors, 105 MB at 3,000, 168 MB at 5,000) rather than with ingestion, which
points to garbage collection.

### Phone layout, alarm storm

Android user agent, 500x1000 window (portrait layout, mobile quality and the
30 fps mobile frame cap). This is the phone layout on the M2's CPU, not a
phone's performance.

| Sensors | Before fps | Before worst | After fps | After worst |
|---:|---:|---:|---:|---:|
| 30 | 30.0 | 36 ms | 30.0 | 36 ms |
| 120 | 29.8 | 67 ms | 30.0 | 36 ms |
| 500 | 28.2 | 283 ms | 29.9 | 50 ms |
| 1,000 | 20.2 | 950 ms | 29.8 | 83 ms |
| 2,000 | | | 29.7 | 134 ms |

## Startup and size (release build, Builds/ReleaseCheck)

- Download: 18,705,889 bytes (data 9.97 MB, wasm 8.60 MB, framework and
  loader 0.13 MB), gzip with decompression fallback.
- Time from navigation to the loading screen closing, localhost: 3.06 s on
  the first ever load, 1.8 to 2.9 s on later loads with the Unity cache
  cleared, 0.8 to 1.0 s fully cached.

## Not covered

- Real phones and tablets, and slower laptops.
- HD quality.
- Network download time; at 50 Mbit/s the 18.7 MB download adds about 3 s.
