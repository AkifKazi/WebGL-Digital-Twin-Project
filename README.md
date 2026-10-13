# Digital Twin

A Unity WebGL digital twin of a vibratory feeder (hopper, separator, conveyor). Live or simulated
sensor readings appear on cards joined to the machine by leader lines, with alarms, a Service view,
cross-section and X-ray views, an Ask assistant, and layouts for desktops and phones.

- **Live demo:** <https://akifkazi.github.io/digital-twin-demo/> (built files: [digital-twin-demo](https://github.com/AkifKazi/digital-twin-demo))
- **Source:** this repo, [digital-twin-project](https://github.com/AkifKazi/digital-twin-project)
- **How to configure and extend it:** [USER_GUIDE.md](USER_GUIDE.md)

It is a visualisation prototype, not a control system: the process model, tags and limits are
demonstration values. Replace them with approved engineering data before calling it operational.

## Quick start

1. Install Unity **6000.6.3f1** (the version in `ProjectSettings/ProjectVersion.txt`) via Unity Hub,
   then *Add project from disk*. The first open rebuilds `Library` and takes some minutes.
2. Open `Assets/Scenes/SampleScene.unity` and press Play. Drag to orbit, scroll or pinch to zoom,
   click a card for its history, Tab for the keyboard, the gear for viewer settings.
3. Before a build or demo: **Tools > Digital Twin > Validate Project** must report zero errors.

## How it fits together

```text
Plant sensors -> edge gateway (auth, tag mapping) -> HTTPS/WSS -> host page
  -> unityInstance.SendMessage("Digital Twin Runtime", "PushJson" | "PushBatchJson", json)
  -> TelemetryJsonIngestor -> TelemetryRegistry -> PerformanceStatSource -> cards, lines, panels
```

Three layers, kept apart so another machine can replace the feeder without touching the UI or the
data path:

| Layer | What changes per machine | Main types |
| --- | --- | --- |
| Reusable platform | nothing | `TelemetryJsonIngestor`, `TelemetryRegistry`, `PerformanceStatSource`, `StatRailManager`, `StatLeaderLineManager`, `TelemetryVisibility`, `CardFocus`, the UI design system |
| Machine definition | everything here | `DigitalTwinMachineConfiguration` asset (`Assets/Machine Configurations`), model prefab, sensor anchors |
| Machine behaviour | only real physics | `HopperProcessSimulator`, `VibratoryFeederSceneConfigurator`, `MachineViewModeController`, particle flow |

**Rules that keep it modular** (break one and the next machine needs code changes):

- No machine tag, ID or simulator type in generic runtime classes or `DigitalTwinSceneSetup`.
- Providers submit `TelemetryReading`s through `ITelemetryReadingSink`; they never touch UI.
  Cards and layouts never parse payloads.
- Card focus lives only in `CardFocus`; telemetry fades only through a layout's
  `TelemetryVisibility`; generic code reads the machine's view only through `IInspectionView`.
- Simulation stays visibly separate from live data; the first live reading switches to Live mode.
- Reject unknown, non-finite, duplicate and out-of-order readings before they change state.
- New authoring requirement → add a check to the project validator.

## Terms

- **Layout:** the *wide* layout (desktop, phone held sideways) or the *portrait* layout (phone held
  upright). `ResponsiveLayoutShell` shows one at a time.
- **Card / rail:** one card per sensor, on rails beside the model (columns) or above and below it
  (rows). Each layout's `StatRailManager` owns its rails; extra cards go to further pages.
- **Detail rail / sheet:** where a card's history panel opens: a rail of its own on wide screens,
  a sheet from the bottom on a phone held upright.
- **Leader line:** card-to-sensor line. Alarm lines always show; others while their card is
  focused. Lines fade over translucent UI marked `LeaderLineObstacle`.
- **View:** Telemetry or Service. **Inspection view:** Full Model, Cross Section or X-Ray
  (Full Model hides telemetry).
- **Card focus:** first press focuses a card, the next opens its detail (a click opens at once on a
  desktop). One focused card at a time, in `CardFocus`.
- **Ask window / twin assistant:** the conversation about the machine, answered by the twin's
  own rules or a language model behind the host page. Advice only.

## Repository map

| Path | Contents |
| --- | --- |
| `Assets/Scripts/Runtime` | runtime code, one class per file, file named for the class |
| `Assets/Editor` | setup tools, the validator, play tests; `Assets/Editor/Tests` edit-mode tests |
| `Assets/Machine Configurations` | machine definitions |
| `Assets/UI/Design System`, `Assets/Resources/UI Theme.asset` | the UI design system |
| `Assets/StreamingAssets/Localization/ui_text.csv` | every on-screen string, editable after a build |
| `Assets/WebGLTemplates/DigitalTwin` | the page template (safe areas, page API bridge) |
| `Assets/LoadTest`, `Tools/LoadTest` | load-test harness and its one-command runner |
| `docs/design-system` | interactive design-system reference (open `index.html`) |
| `Tools/DesignSystemSite` | exporter that keeps that site in step with Unity |

Not in git: `Library`, `Builds`, `Logs`, `Temp`, `UserSettings`, `ProjectArchive`,
`ProjectReferences`, `ProjectSamples`.

## Things that will bite you

- Building rewrites `Assets/Settings/Build Profiles/Web.asset`; revert it unless you meant to change it.
- After a setup tool rebuilds a UI prefab, rerun **Tools > Digital Twin > UI Skin > Apply Procedural Skin**.
- After changing the design system, rerun `python3 Tools/DesignSystemSite/export_design_system.py`.
- Close Unity before the load-test kit, and keep the laptop on mains power (on battery Chrome caps at 30 fps).
- Rename or move the project folder only with Unity closed, then re-add it in Unity Hub.
- Builds go to the demo repo, never this one.
