# User guide

How to set up, configure and extend the twin. Each section says when you would do it, where
the thing lives, how to do it, and why it works that way. Start with [README.md](README.md) for
the overview and the rules that keep the project modular.

Menu items are under **Tools > Digital Twin** unless stated otherwise.

## 1. Build and publish

**When:** sharing a new version of the demo.

1. Run **Validate Project**; fix every error.
2. Build with the **Web** build profile into `Builds/WebGL` (or batch:
   `-executeMethod WebGLBuildAudit.BuildRelease -auditOutput Builds/WebGL`, which also reports
   size, warnings and errors). Never a Development Build: larger and slower.
3. Revert `Assets/Settings/Build Profiles/Web.asset` if the build rewrote it.
4. Copy the contents of `Builds/WebGL` (`index.html`, `Build`, `TemplateData`, `StreamingAssets`)
   over the root of the [digital-twin-demo](https://github.com/AkifKazi/digital-twin-demo) repo
   and push. GitHub Pages serves it at <https://akifkazi.github.io/digital-twin-demo/>.
5. Check it once in a private window (true first load) and once on a real phone.

**Why these settings:** GitHub Pages cannot send `Content-Encoding` headers, so the profile uses
Gzip with Decompression Fallback (Brotli would be ~25% smaller but decode slower in JavaScript).
Data Caching makes return visits ~1 s. The release download is about 19 MB.

## 2. Swap or add a machine

**When:** showing a different machine, or proving the platform is reusable.

1. **Model:** remove unseen geometry (keep what the cross-section needs), real-world scale, sensible
   pivots, one root prefab. Only split parts that move, highlight, hide or need their own material.
   Place it under the machine area of `SampleScene`; the canvas, rails, camera and runtime stay
   outside the model prefab, which is what keeps the UI stable when the model changes.
2. **Configuration:** *Assets > Create > Digital Twin > Machine Configuration*. Add every sensor
   (section 3). Mark exactly one configuration **Default**.
3. Run **Set Up Sensors And Flow Controls**. It creates or updates a `PerformanceStatSource` per
   configured sensor and removes sensors that belonged only to the previous default.
4. Move each generated sensor object to its measuring point: its Transform is the leader-line anchor.
5. On `Shared Machine View Controller`, assign the full model and the cross-section model. A
   machine with different inspection views implements `IInspectionView` once.
6. Only for real physical behaviour: an Editor class implementing
   `IDigitalTwinMachineSceneConfigurator` (named in the configuration), and a simulator
   implementing `ITelemetrySimulationProvider`.
7. Retarget the orbit camera (target and distance limits) to the new model's bounds.
8. Send test JSON with the new IDs (section 4) and run **Validate Project**.

**Why:** generic code must not change. If you find yourself editing `DigitalTwinSceneSetup`,
the registry, cards or layouts, the change belongs in configuration or a configurator instead.

To demo adoption without a second model: duplicate `Vibratory Feeder.asset`, change its identity
and sensors, make it the only Default, run setup, then restore the feeder.

## 3. Sensors

**When:** adding, renaming or retuning a measurement.

Durable definitions live in the machine configuration (so setup can reproduce them); quick edits
can go on the sensor's **Performance Stat Source** in the scene (under `Telemetry Sources`).

| Field | How to set it |
| --- | --- |
| Stat Id | Stable key agreed with the gateway, e.g. `EXC-07.ENGINE.COOLANT_TEMP`. Never reuse or rename it for a label change: it is the integration contract. |
| Metric Name, Unit, Value Format | What the card shows; `0.0` = one decimal. |
| Thresholds | Warning and critical limits (high, low or a band). Alarms clear only after falling back past the limit by **Alarm Hysteresis** (10% of the warning-critical gap). |
| Stale timeout | A little over the sensor's normal publish interval. |
| Display Priority | Higher stays on page one when space runs out. Alarms always come first. |
| Preferred Rail | Leave on Auto unless a side matters. |

To add one quickly, duplicate a similar sensor, give it a unique Stat Id, place it, and validate.
Every sensor gets its own card automatically: keep the registry and both `StatRailManager`s on
**Scene Discovery** (use **Explicit List** only for a deliberately restricted scene).

Units show in capitals by default; the viewer can choose engineering casing (mm/s, kW) in the
settings.

## 4. Live data

**When:** connecting a gateway or testing with real IDs.

Send normalised readings from the host page; never connect a browser to a PLC or OPC UA server.
The gateway owns authentication, protocol conversion, tag mapping, rate limiting and history.

```json
{ "schemaVersion": 1, "sensorId": "VIB-01.MOTOR.TEMP", "value": 66.4, "quality": 0,
  "sourceTimestampUnixMs": 1786464000000, "statusCode": 0, "sequenceNumber": 1842,
  "providerId": "site-a-edge-01" }
```

```javascript
unityInstance.SendMessage("Digital Twin Runtime", "PushJson", JSON.stringify(reading));
unityInstance.SendMessage("Digital Twin Runtime", "PushBatchJson",
  JSON.stringify({ schemaVersion: 1, readings: [ /* ... */ ] }));
// optional transport state: ReportGatewayConnected | ReportGatewayHeartbeat | ReportGatewayDisconnected
```

- Quality: 0 Good, 1 Uncertain, 2 Bad, 3 Stale. Bad and Stale keep the last usable number but
  show the card as unavailable.
- Rejected: unknown IDs, non-finite or out-of-range values, duplicate or older sequence numbers
  from the same provider, unsupported schema versions. A missing timestamp uses receive time.
- The first accepted live reading switches Simulation to Live; sensors not yet heard from show
  stale, so simulated values are never mistaken for plant data.
- The gateway shows DEGRADED after 3 s without data and OFFLINE after 10 s (tune on
  `ConnectionHealthMonitor` to match the real publish interval).
- For REST polling, pass each batch response to `PushBatchJson`. Keep waveforms and history in the
  backend; send Unity only current or aggregated values.

## 5. Simulation

`HopperProcessSimulator` drives the demo: correlated level, flow, power, current, temperature,
vibration and belt speed, with slow demand swings so history charts are never flat. Its starting
values are placeholders for a 22 kW, 400 V, ~1480 rpm drive and an 80 m³ hopper, and the scene
deliberately includes warning and critical demonstrations. `FeedbackStressTest` runs 24 simulated
hours and fails on flat signals or unexpected alarms. `TelemetryOperatingModeController` switches
between simulation and live data. Particle flow: adjust only **Flow Speed** and **Quantity** on
`Particle Flow Controller` (keep near 1).

## 6. Layout, rails and cards

**When:** cards crowd the model, or a screen shape needs a different arrangement.

- **Viewers choose** under *Settings > Telemetry > Card rails*: 2 (default), 4, 6 or 8 in all on a
  desktop; 2 or 4 on a phone. A choice is a ceiling: rails that would leave the model less than 30%
  of the screen drop, and their cards go to the next page. Paging arrows sit beside the zoom.
- **Authors override** in **Telemetry Layout Settings** (on the layout controller): per screen
  (desktop columns, desktop rows for tall windows, phone sideways, phone upright) set *Viewer's
  choice*, *Auto* (as many as fit while the model keeps 40%) or 1–4 a side; turn the history panel
  on or off; size the detail rail. Phones never exceed 2 a side. Changes apply in Play mode at once.
- A desktop in a tall window keeps desktop controls but runs cards in rows. `ResponsiveLayoutShell`
  > **Device Override** forces Desktop or Mobile for testing; return it to Auto before building.
- Alarm cards take the innermost rail on their side (shortest lines). Inner rails keep a card's room
  free so cards can step aside for a focused card's line or a panel connector.

## 7. Viewer settings and keyboard

The gear opens *Settings* (a full page on a phone), saved in the browser and only when changed
from the default, so later default changes still reach everyone:

- **Display:** interface scale (100% on phones, 90% on desktops by default), high contrast, reduce
  motion, edge shading (Strong).
- **Telemetry:** units, card rails, dim other cards.
- **Camera:** orbit and zoom sensitivity, invert tilt, smooth camera.
- **Graphics:** SD/HD (desktop only; phones are locked to Mobile quality at 30 fps), frame-rate
  limit, frame-rate readout.

Keyboard (`KeyboardNavigator` on the EventSystem): Tab moves through controls and cards, arrows
move and adjust, Enter presses, Esc steps back (the Ask window takes Esc while it has the keyboard).
With nothing focused, A/D and the arrows orbit, W/S and +/- zoom, R or Home resets the view. A
modal sets `KeyboardNavigator.Scope`; a non-Selectable joins by implementing `IKeyboardTarget`.

## 8. Text and languages

Every on-screen string is in `Assets/StreamingAssets/Localization/ui_text.csv` (and beside a build,
so it can be edited on the server without rebuilding). First column `key`, then one column per
language code; a `notes` column is ignored. Empty cells fall back to `en`, then to the English in
code. Keep `{0}` placeholders. Choose a language with `?lang=de` or
`SendMessage("Digital Twin Runtime", "SetLanguage", "de")`; `LoadLanguageCsv` and
`ReloadLanguageTable` load or re-read a table. **Update Language Table** adds rows for new keys
without touching existing ones. Rajdhani covers Latin only: add a fallback TMP font for Cyrillic,
Greek or CJK.

## 9. AI data table and the Ask assistant

**AI data table** (`TelemetryAiDataTable`, added automatically): `sensors`, `maintenance` and
`series` tables with windowed figures (15 min, 60 min, 24 h by default: mean, min, max, trend per
hour, time in alarm, coverage, minutes to a warning). Generated backfill is excluded. The page gets
them every 10 s:

```javascript
window.addEventListener("digitaltwin:ai-table", e => sendToModel(JSON.parse(e.detail)));
// also: RequestAiTable, DownloadAiTable ("sensors" | "maintenance" | "series" | "all"), SetAiWindows ("5,30,240")
// show a result in a sensor's detail panel:
unityInstance.SendMessage("Digital Twin Runtime", "PushAiInsight",
  JSON.stringify({ sensorId: "VIB-01.MOTOR.TEMP", text: "Rising 3 °C/h; warning in ~4 h.", ttlSeconds: 900 }));
```

**Ask assistant** (`TwinAssistant`): by default the twin answers with rules over its live figures
(English questions about a named sensor, alarms, overall state, services). To use a language
model, set **Page Answers** or `SetAssistantSource("page")`, then answer from the page:

```javascript
window.addEventListener("digitaltwin:ask", async e => {
  const q = JSON.parse(e.detail);                 // { id, question, view }
  const a = await askModel(q.question, window.DigitalTwinAi.latestJson);
  unityInstance.SendMessage("Digital Twin Runtime", "PushAssistantReply", JSON.stringify({
    id: q.id, text: a.text, sensorIds: ["FEED-01.SLIP"], parts: ["Drive Roller"],
    check: "Re-tension the belt.", followUps: ["What needs service next?"] }));
});
```

No answer within 20 s falls back to the twin's own. Page text is shown as plain text (markup is not
interpreted) and truncated. The assistant only advises; pressing a reading or part in an answer does
what the viewer's own press would.

## 10. UI design system

**When:** changing how anything looks, or adding UI.

- **Where:** tokens in `Assets/UI/Design System/UI Tokens.asset`; one folder per component with one
  style asset per state; `Assets/Resources/UI Theme.asset` lists them. Edits show immediately.
- **How it draws:** every panel, control, card and line is a `UIShape` drawn by
  `Assets/Materials/UI/UIShape.shader`. A shape names its component and state; code shows a state by
  setting it (a card in Warning, a chosen Segment via `UIShape.ShowChosen`). Hover and press come
  from `UIHoverSprite` or `UIControlState`. Only icons are sprites (`Assets/UI/Sprites`).
- **Change a colour or spacing everywhere:** edit a token. **Change one state's look:** edit that
  style. **Reset all:** *UI Skin > Reset Design System To Defaults* (source: `UIDesignSystemDefaults`).
- **Rules:** at rest content is a dark well with no light; hover lights the bottom edge quietly;
  Selected is a near-white edge and glow; controls' labels and icons use `UIControlColors`
  (near white chosen, quiet cyan at rest, neutral disabled); accent is for edges and marks only;
  alarms breathe and still answer the pointer; unavailable is grey and hatched.
- **New UI in the editor:** *GameObject > Digital Twin UI* gives ready components with auto layout.
  **New UI in code:** `UIBuilder`. Setup tools use `UIRoleShape`.
- **Keep the reference in step:** `python3 Tools/DesignSystemSite/export_design_system.py` regenerates
  `docs/design-system/data.js`; `UIDesignSystemCatalog.BuildAndCapture` (batch) refreshes
  `docs/design-system/catalog.png` and the catalog scene. The site works offline from `index.html`.

## 11. Performance and load testing

Measured on an M2 MacBook Air in Chrome: about 59 fps up to 3,000 sensors with 5% of them changing
alarm every 5 s, degrading at 5,000; flat memory over 15 minutes at 1,000; every sensor recovers
after a 7 s data dropout. Startup is ~2 s first visit, ~1 s after (localhost).

**Re-measure** after changing ingestion, layout or rendering:

```bash
Tools/LoadTest/run_load_test.sh            # tests, load-test build, all scenarios (~35 min)
Tools/LoadTest/run_load_test.sh --quick    # four standard scenarios (~12 min)
```

Options `--skip-tests`, `--skip-build`, `--only <scenario>`. The report is
`Tools/LoadTest/results/<date>/summary.md`. Scenarios (in `run_load_test.sh`) are URL options of the
load-test build: `loadtest=30,500,1000` sensor counts, `hz` readings per second, `alarms` fraction in
alarm, `dropout` seconds, `hd=1`, `repeat`. The harness (`Assets/LoadTest`) exists only in the
editor and `SensorLoadTestBuild` builds, never in a release. Close Unity, stay on mains power, and do
not measure in a hidden browser pane (it stops rendering).

Where size goes: meshes (polygon count, fix in the modelling tool), then textures (WebGL ceilings
are set per texture by **Apply WebGL Load Optimization**; read the effective size before overriding,
an override can raise it). Code is stripped (`Assets/link.xml` keeps what `JsonUtility` needs).

## 12. Tests and tools

- Edit-mode tests: `Assets/Editor/Tests` (Unity Test Runner, or `-runTests -testPlatform EditMode`).
- Play tests: classes ending `PlayTest` in `Assets/Editor`, run in batch with
  `-executeMethod <Name>.Run` (no `-quit`); captures go to `Logs/<Name>`.
- **Validate Project**: IDs, references, one Default machine, reachable pages, shared view
  controller, environment FBX under 10 MB. **Validate Telemetry Layout Stress**: layouts and rails.
- Set Up … tools rebuild parts of the scene; run them to repair, not to test. After one rebuilds a
  UI prefab, run *UI Skin > Apply Procedural Skin*.

## 13. Troubleshooting

| Symptom | Check |
| --- | --- |
| Pink materials | Shader import finished; URP assets assigned in Graphics and Quality settings. |
| Missing card | `Visible`, other pages, Scene Discovery, display priority. |
| Wrong alarm | Threshold mode and limits; hysteresis delays clearing, not raising. |
| No leader line | The sensor has a card and sits near the machine; normal lines show only when focused. |
| Live values stale | Gateway connection, sensor ID, timestamps, quality, publish interval vs stale timeout. |
| Phone runs hot | Mobile quality preset and 30 fps lock still on. |
| A setting change has no effect | The scene's Telemetry Layout Settings may fix a count instead of *Viewer's choice*. |
