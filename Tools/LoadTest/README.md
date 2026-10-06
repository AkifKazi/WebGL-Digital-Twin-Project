# Load test kit

Measures how the WebGL digital twin performs as the number of sensors grows,
and checks that nothing else broke. One command:

```bash
Tools/LoadTest/run_load_test.sh
```

It runs, in order:

1. The project's edit-mode unit tests, then its editor and play-mode tests (about 5 minutes).
2. A load-test WebGL build in `Builds/LoadTest` (about 2.5 minutes).
3. Each scenario below in a throwaway Chrome window (about 25 minutes in total).

The report is `Tools/LoadTest/results/<date_time>/summary.md`, next to the raw
Unity and Chrome logs. Results are not committed.

Options: `--quick` (the four standard scenarios, about 12 minutes),
`--skip-tests`, `--skip-build`, `--only <scenario>`.

Requirements: the project closed in the Unity editor, the Unity version in
`ProjectSettings/ProjectVersion.txt` installed through Unity Hub, Google
Chrome, python3. Leave the machine otherwise idle while it runs; other work
competes for the CPU and skews the numbers. Keep the laptop plugged in: on battery,
Chrome caps pages at 30 fps (15 fps for the phone layout), so frame rates are
not meaningful (the report warns when this happens).

## Scenarios

| Name | What it does |
|---|---|
| `startup` | Times navigation to an interactive twin, 4 loads. Load 1 is a true first visit (fresh profile); later loads use the caches. |
| `steady_wide` | 30 to 3,000 sensors, 2 readings per sensor per second, no alarm changes. 1440x900 window. |
| `storm_wide` | 30 to 5,000 sensors, with 5% of sensors entering and leaving alarm every 5 seconds. |
| `storm_phone` | The storm on the phone layout (Android user agent, 500x1000 window, 30 fps phone cap). Uses this Mac's CPU, so it is not a phone's speed. |
| `dropout_wide` | 1,000 and 3,000 sensors; the data feed stops for 7 seconds so every sensor goes stale, then resumes. Checks that all sensors recover. |
| `hd_storm_wide` | The storm with HD quality switched on. |
| `soak_1000` | 1,000 sensors with alarm changes, measured 30 times over 10 minutes, to catch slowdowns or memory growth over time. |

Scenarios live in the `SCENARIOS` list in `run_load_test.sh`. A scenario is
the load-test build's URL options:

| Option | Meaning |
|---|---|
| `loadtest=30,120,500` | Sensor counts to measure, in order. Sensors are clones of the machine's real ones, anchored to the same parts. |
| `hz=2` | Readings per sensor per second, sent as JSON batches through the same path a gateway uses. |
| `alarms=0.05` | Fraction of sensors pushed past their critical limit; the set rotates every 5 seconds. |
| `dropout=7` | Seconds without data in the middle of each measurement. |
| `hd=1` | Switch HD quality on. |
| `repeat=30` | Measure each count this many times. |
| `warmup=4`, `measure=10` | Seconds of warm-up and of measurement per count. |
| `orbit=10` | Camera orbit speed in degrees per second (0 to keep it still). |

## Reading the numbers

- **Avg fps**: frames per second. 60 is the target on desktop, 30 on phones
  (the twin caps phones at 30).
- **Median / p95 / p99 / Worst**: frame times. A frame at 60 fps takes 17 ms.
  p99 is the slowest 1 in 100 frames. Browsers count anything over 50 ms as
  a long task; over 200 ms feels like a freeze.
- **1% low fps**: the frame rate of the slowest 1% of frames.
- **Intake ms/s**: time spent turning incoming JSON into sensor readings, per
  second. 1,000 ms/s would mean the whole second.
- **Rebuild**: time to lay the cards out again after the sensor count changed.
- **Heap MB**: managed memory. Steady growth across a soak run would be a leak.
- **Peak stale / Stale at end**: in a dropout, how many sensors went stale and
  how many were still stale at the end (should be 0).

## How it works

- `Assets/LoadTest/SensorLoadTestHarness.cs` is the in-app driver. It is
  compiled only into the editor and into builds made by
  `SensorLoadTestBuild.BuildWebGL` (the `DIGITAL_TWIN_LOAD_TEST` define), so
  the release build never contains it. In the editor it does nothing unless
  `-loadtest` is passed.
- `Assets/LoadTest/Editor/SensorLoadTestBuild.cs` makes the load-test build
  and has `SmokeTest` to try the harness in editor play mode.
- `run_load_test.sh` serves the build from a temporary folder on
  `localhost:8771`, starts Chrome with throttling disabled (a hidden or
  covered browser window otherwise stops rendering frames), waits for
  `LOADTEST_DONE` in the console log and summarises it with `summarise.py`.
- `timing.html` times startup by loading the build in a frame.

Past results and what was fixed: `Assets/Documentation/SensorLoadTest.md`.
