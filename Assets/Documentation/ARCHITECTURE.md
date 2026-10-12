# Digital Twin Architecture

## Design objective

The Unity application should accept another construction-machine model without rewriting telemetry ingestion or responsive presentation. Machine adoption should primarily involve model preparation, sensor configuration, approved engineering data, and genuinely machine-specific animation.

## Reusable platform responsibilities

The following classes are machine-independent and should not contain hopper tag names or hopper process assumptions:

- `TelemetryReading` — normalized runtime value and provenance.
- `TelemetryJsonContract` — versioned external JSON data-transfer objects and parsing.
- `ITelemetryReadingSink` — normalized provider-to-runtime input boundary.
- `TelemetryJsonIngestor` — browser/JSON adapter and accepted-reading coordinator.
- `TelemetryRegistry` — stable sensor-ID lookup and stale-data monitoring.
- `TelemetrySourceResolver` — shared automatic or explicit source selection.
- `PerformanceStatSource` — sensor definition, latest state, alarm evaluation, and world anchor.
- `TelemetryCardPresentation` — a strict one-sensor/one-card presentation boundary.
- `ConnectionHealthMonitor` — provider and aggregate data-health state.
- `StatRailManager` and `StatLeaderLineManager` — responsive presentation: one of each per layout, the portrait one set to always run rows.
- `TelemetryVisibility` — one per layout: owns the Telemetry / Service view and is the only thing that fades the layout's telemetry regions. It combines the view, the inspection view and the portrait sheet's cover; its rules live in the plain `TelemetryVisibilityState`, covered by edit-mode tests. `WideServiceView` and `PortraitServiceView` only place the service panel from its blend.
- `CardFocus` — the one card focus for the whole twin: which card is focused, which cards have their detail open, and what a press on a card does. Its rules live in the plain `CardFocusState`, covered by edit-mode tests. Cards report presses to it; `TelemetryDetailPanelController` and `PortraitTelemetrySheetController` register as `ICardDetailPresenter`s; leader lines, the rails and the model highlights ask it.
- `RailCardSpread` — places a rail's cards near their sensors without overlapping, and clear of the connectors from outer cards to their detail panels. Covered by edit-mode tests.
- `IInspectionView` — what generic telemetry needs from the machine's inspection view: whether the view on show displays telemetry.
- Card, leader-line, responsive-shell, camera, and text-fitting components.
- `UIShape`, `UIComponentStyle`, `UIShapeStyle`, `UIDesignTokens`, `UIControlState`, `UITextStyle`, `UITheme` and `UITextLegibility` — the UI design system: tokens, components and their states, drawn procedurally. See [`UI_DESIGN_SYSTEM.md`](UI_DESIGN_SYSTEM.md).

## Machine-specific responsibilities

The current hopper reference implementation includes:

- `HopperProcessSimulator`;
- hopper clipping/cross-section behavior, and `MachineViewModeController`, which implements `IInspectionView` (Cross Section and X-Ray show telemetry, Full Model does not);
- conveyor and rotor motion;
- separator vibration;
- rock and filtered material-flow behavior;
- hopper/conveyor sensor IDs and example engineering limits.

These components may use the reusable telemetry contracts, but generic components must not depend on them.

The current machine is represented by `Vibratory Feeder.asset`, a
`DigitalTwinMachineConfiguration`. It owns sensor identity, presentation metadata,
anchor positions, hierarchy categories, and engineering limits.
`VibratoryFeederSceneConfigurator` owns the feeder-only process simulator and particle-flow setup.
The reference installer creates the asset automatically on the first Unity script reload;
its separate provider also keeps automated setup deterministic before that delayed creation runs.

`DigitalTwinSceneSetup` contains only the reusable assembly procedure. It loads the one
configuration marked as default and does not contain feeder sensor IDs, feeder group IDs,
or a direct dependency on `HopperProcessSimulator`.

## Source ownership

`DigitalTwinMachineConfiguration` is the authoring source of truth for static machine
metadata. Scene setup materializes each configured sensor as a `PerformanceStatSource`,
which owns the latest runtime state and world-space anchor. Configured metadata includes:

- stable telemetry ID;
- display label and engineering unit;
- numeric formatting;
- alarm thresholds;
- stale timeout;
- display priority and rail preference;
- current state and world-space anchor.

The registry and both responsive rail managers use `TelemetrySourceSelectionMode`.
Cards remain fixed-width and every visible sensor owns a separate card. Portrait
overflow is handled by explicit pagination rather than silently dropping readings:

- **SceneDiscovery** is the default for modular machine prefabs.
- **ExplicitList** supports deliberately restricted scenes.

Alarm state and display priority determine page order, so warning and critical readings
move to the first portrait page. Page navigation belongs only to the presentation layer;
it never changes the registry, sensor state, or machine model.

All three consumers use `TelemetrySourceResolver`, preventing different discovery rules from drifting apart.

Exactly one machine configuration is marked **Default** for deterministic editor and CI
setup. Adding a machine means creating another configuration and, only when physical
behavior requires it, a small configurator implementing
`IDigitalTwinMachineSceneConfigurator`. The central setup script remains unchanged.

## Data-provider boundary

Every provider must produce `TelemetryReading` values and submit them through `ITelemetryReadingSink`. Current adapters are:

- `TelemetryJsonIngestor` for normalized browser/API messages;
- `HopperProcessSimulator` for the reference process simulation.

Simulation control uses `ITelemetrySimulationProvider`, so `TelemetryOperatingModeController` does not depend on `HopperProcessSimulator`.

Providers must not update cards directly. Successful ingestion updates `PerformanceStatSource`; subscribed presentation components then react to the source.

## Security and system boundary

Unity is the real-time 3D visualization client. It is not the authoritative historian, authentication server, or plant-control system.

A production edge/backend service owns:

- PLC, OPC UA, MQTT/Sparkplug, and vendor-protocol connections;
- authentication and asset authorization;
- tag mapping and unit normalization;
- rate limiting and aggregation;
- durable historical storage;
- audit logs and operational security.

The browser receives only authorized, normalized state through HTTPS or secure WebSockets and forwards it to Unity WebGL.

## Extension rules

- Do not add machine tag names to generic runtime classes.
- Do not add machine sensor IDs, categories, or simulator types to `DigitalTwinSceneSetup`.
- Do not let cards or layouts parse API payloads.
- Do not let providers manipulate UI elements directly.
- Do not track card focus, or which cards have a detail open, outside `CardFocus`; ask it.
- Do not fade or hide telemetry outside a layout's `TelemetryVisibility`, and do not read machine view controllers from generic telemetry; go through `IInspectionView`.
- Keep simulation visibly separate from live data.
- Preserve source timestamp and quality whenever the upstream system supplies them.
- Reject unknown, non-finite, duplicate, and out-of-order readings before changing operating state.
- Add validation when introducing a new authoring requirement.
