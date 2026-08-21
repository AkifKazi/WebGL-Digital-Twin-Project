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
- `TelemetryEquipmentGroup` — presentation grouping for related physical measurements.
- `ConnectionHealthMonitor` — provider and aggregate data-health state.
- `WideStatRailManager` and `PortraitStatRailManager` — responsive presentation.
- Card, leader-line, responsive-shell, camera, and text-fitting components.

## Machine-specific responsibilities

The current hopper reference implementation includes:

- `HopperProcessSimulator`;
- hopper clipping/cross-section behavior;
- conveyor and rotor motion;
- separator vibration;
- rock and filtered material-flow behavior;
- hopper/conveyor sensor IDs and example engineering limits.

These components may use the reusable telemetry contracts, but generic components must not depend on them.

The current machine is represented by `Vibratory Feeder.asset`, a
`DigitalTwinMachineConfiguration`. It owns sensor identity, presentation metadata,
anchor positions, hierarchy categories, engineering limits, and equipment groups.
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

The registry and both responsive rail managers use `TelemetrySourceSelectionMode`:

- **SceneDiscovery** is the default for modular machine prefabs.
- **ExplicitList** supports deliberately restricted scenes.

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
- Do not add machine sensor IDs, groups, categories, or simulator types to `DigitalTwinSceneSetup`.
- Do not let cards or layouts parse API payloads.
- Do not let providers manipulate UI elements directly.
- Keep simulation visibly separate from live data.
- Preserve source timestamp and quality whenever the upstream system supplies them.
- Reject unknown, non-finite, duplicate, and out-of-order readings before changing operating state.
- Add validation when introducing a new authoring requirement.
