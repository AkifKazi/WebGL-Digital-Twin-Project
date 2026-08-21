# Modular Unity WebGL Digital Twin

An interactive Unity WebGL digital-twin prototype for industrial and construction equipment. The current reference asset is a hopper/conveyor system, but the telemetry, alarm, responsive presentation, connection-health, and data-ingestion layers are designed to be reused with other machine prefabs.

The project supports:

- responsive wide and portrait telemetry rails;
- grouped sensor cards and world-space leader lines;
- Good, Uncertain, Bad, and Stale data states;
- automatic warning and critical thresholds;
- simulation and live JSON input;
- connection-health and last-update reporting;
- duplicate and out-of-order sample rejection;
- desktop and mobile WebGL interaction.
- configuration-driven adoption of additional machine types.

For a non-technical walkthrough, read [`Assets/Documentation/USER_GUIDE.md`](Assets/Documentation/USER_GUIDE.md).

## Project status

The project is a working visualization prototype, not a plant-control system. The hopper process model and engineering limits are demonstration values. A production deployment must use approved tag mappings, limits, security, and machine data.

## Requirements and quick start

1. Install **Unity 6000.4.8f1** through Unity Hub.
2. Clone or download the repository.
3. In Unity Hub, select **Add > Add project from disk** and choose this folder.
4. Open `Assets/Scenes/SampleScene.unity`.
5. Allow Unity to restore packages and rebuild the excluded `Library` cache.
6. Enter Play mode.

Use **FULL BODY** and **CROSS SECTION** to change views. Drag to orbit and scroll or pinch to zoom.

## Architecture and module boundary

The runtime is split conceptually into three areas:

- **Reusable platform:** telemetry contracts, source discovery, registry, data quality, alarms, cards, leader lines, responsive layouts, connection health, camera interaction, and JSON ingestion.
- **Machine definition:** model prefab, sensor anchors, IDs, labels, units, thresholds, display priorities, and equipment groups.
- **Machine-specific behavior:** the hopper simulator, material flow, conveyor behavior, separator vibration, and cross-section implementation.

`TelemetryOperatingModeController` depends on the generic `ITelemetrySimulationProvider` contract rather than the hopper class. `HopperProcessSimulator` is one implementation and can be replaced by a simulator for another type of equipment.

`DigitalTwinSceneSetup` is machine-agnostic. It reads the single
`DigitalTwinMachineConfiguration` marked **Default** and materializes that machine's
sensors, hierarchy categories, engineering limits, anchor positions, and equipment groups.
The current feeder-specific simulator and particle setup live in
`VibratoryFeederSceneConfigurator`, outside the central setup script.

`TelemetryRegistry`, `WideStatRailManager`, and `PortraitStatRailManager` use the same source-selection rule. The default **Scene Discovery** mode automatically finds `PerformanceStatSource` components created from the selected machine configuration. **Explicit List** remains available when a scene needs strict manual control.

See [`Assets/Documentation/ARCHITECTURE.md`](Assets/Documentation/ARCHITECTURE.md) for ownership and extension rules.

## Add-machine demo workflow

This is the intended authoring demonstration; it does not add a second production machine to the repository.

1. Import an optimized construction-machine model and create a root prefab.
2. Create a `DigitalTwinMachineConfiguration` asset.
3. Add sensor IDs, labels, units, formats, alarm limits, categories, anchor positions, priorities, and stale timeouts to that asset.
4. Add equipment-group definitions only for nearby measurements belonging to the same physical component.
5. Mark exactly one machine configuration as **Default**.
6. Run **Tools > Digital Twin > Set Up Sensors And Flow Controls** to materialize the configured anchors.
7. Leave source selection on **Scene Discovery** so the registry and both layouts use the generated sensors automatically.
8. Add an optional machine-specific editor configurator and simulator only for genuine physical behavior.
9. Send test readings using the canonical JSON contract.
10. Run **Tools > Digital Twin > Validate Project** before building.

The reusable UI and live-data code should not require machine-specific edits. Only the model, sensor configuration, physical animation, and optional simulator should change.

The site environment is imported without embedded textures. `Construction Site.fbx` is
approximately 200 KB, and project validation enforces the mentor-agreed 10 MB maximum for
the environment FBX.

See [`Assets/Documentation/ADDING_A_MACHINE.md`](Assets/Documentation/ADDING_A_MACHINE.md) for the detailed checklist.

## Live data and API integration

Unity consumes a vendor-neutral reading instead of a PLC- or manufacturer-specific payload:

```json
{
  "schemaVersion": 1,
  "sensorId": "VIB-01.MOTOR.TEMP",
  "value": 66.4,
  "quality": 0,
  "sourceTimestampUnixMs": 1786464000000,
  "statusCode": 0,
  "sequenceNumber": 1842,
  "providerId": "site-a-edge-01"
}
```

Quality values are `0 = Good`, `1 = Uncertain`, `2 = Bad`, and `3 = Stale`.

For WebGL, the hosting page should connect to an authenticated HTTPS or secure WebSocket gateway and forward normalized payloads with:

```javascript
unityInstance.SendMessage("Digital Twin Runtime", "PushJson", JSON.stringify(reading));
```

Versioned batches use `schemaVersion: 1`, a `readings` array, and `PushBatchJson`. Legacy batches without a version remain accepted as schema version 0.

Do not connect a public browser directly to a PLC or plant-floor OPC UA server. Authentication, authorization, protocol conversion, rate limiting, tag mapping, and historical storage belong at the edge/backend boundary.

Full contract and transport guidance: [`Assets/Documentation/DigitalTwinIntegration.md`](Assets/Documentation/DigitalTwinIntegration.md).

## Repository contents

- `Assets/Scripts/Runtime/` — reusable runtime plus machine-specific runtime components.
- `Assets/Editor/` — setup, authoring, validation, and build tools.
- `Assets/Documentation/` — user, architecture, machine-adoption, API, and deployment guidance.
- `Assets/Scenes/` — production sample scene.
- `Assets/Models/`, `Materials/`, and `Prefabs/` — authored visual assets.
- `Packages/` — reproducible Unity dependencies.
- `ProjectSettings/` — Unity and platform configuration.

Generated builds, caches, profiler captures, recovery archives, and private project references are excluded from source control.

## Validation and WebGL builds

Before a demo or deployment:

1. Save the scene.
2. Run **Tools > Digital Twin > Validate Project**.
3. Confirm that validation reports zero errors.
4. Exercise simulation, live input, stale data, cross-section mode, card focus, and responsive layouts.
5. Create a non-development WebGL build using the project workflow.

The generated `Builds/` directory is intentionally not stored on the source branch.
