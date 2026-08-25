# Adding a Construction Machine

This document describes the intended demo and production-authoring workflow. It does not require adding a second example machine to the repository.

## 1. Prepare the model

- Remove unseen internal geometry unless it is required by a cross-section view.
- Combine meshes and materials where that reduces WebGL draw calls without preventing necessary animation.
- Use sensible real-world scale, pivot positions, and component names.
- Separate only the parts that must move, highlight, hide, or receive different materials.
- Create a root prefab for the complete machine.

Place the new root prefab under the machine/model area in `SampleScene`. The responsive
canvas, rails, camera controls, and telemetry runtime must stay outside the model prefab;
this is what keeps the UI stable when the model is replaced.

## 2. Create a machine configuration

Create **Assets > Create > Digital Twin > Machine Configuration**. Give it a stable
machine ID and display name. Mark it as **Default** only when it is the machine that
`DigitalTwinSceneSetup` should materialize in the sample scene.

Add a sensor definition for every measurement and configure:

- a stable telemetry ID agreed with the gateway mapping;
- a concise UI label;
- an accurate engineering unit and number format;
- approved warning and critical thresholds;
- a stale timeout appropriate to its normal publish interval;
- a display priority;
- optional wide and portrait rail preferences;
- a hierarchy category and local anchor position.

Run **Tools > Digital Twin > Set Up Sensors And Flow Controls**. The generic setup creates
or updates the `PerformanceStatSource` anchors from the selected configuration. It also
removes sensors belonging only to the previously selected configuration.

## 3. Use stable identity

Prefer an identity scheme that separates site, asset, component, and measurement in the backend. The Unity sensor ID can be a stable composed key such as `EXC-07.ENGINE.COOLANT_TEMP`.

Do not change an ID merely because the visible label changes. The ID is an integration contract; the label is presentation.

## 4. Connect the model views and anchors

Every sensor produces one independent card; no backend or UI grouping is used. After
setup, move each generated sensor GameObject to its measurement location. Its Transform
is the leader-line anchor and may require manual placement for each machine.

Select `Shared Machine View Controller` and assign:

- **Full Hopper** to the complete machine model;
- **Cut Hopper** to the prepared cross-section/cutaway model;
- compatible full-model renderers when the clip animation is used.

The wide and portrait controls share this one controller, so Full Body and Cross Section
cannot drift into different states. Cross-section geometry and clip values are necessarily
machine-specific. Orbit and zoom remain reusable as long as the camera target and distance
limits are adjusted for the new model's bounds.

For small edits you may work directly on a sensor's `Performance Stat Source` Inspector:
ID, label, unit, format, thresholds, stale timeout, display priority, rail preference, and
visibility are all authoring fields. Put durable metadata in the machine configuration if
the setup tool must reproduce it later.

## 5. Select sensor discovery

Keep `TelemetryRegistry`, `WideStatRailManager`, and `PortraitStatRailManager` on **Scene Discovery** for the normal modular workflow. Sources inside the loaded machine prefab are then selected by one shared rule.

Use **Explicit List** only when the scene deliberately contains sensors that must not appear in a particular twin view.

## 6. Add optional machine behavior

Visual movement belongs in machine-specific components. Examples include hydraulic articulation, wheel or shaft rotation, belt motion, and material flow.

If the machine needs scene-specific setup, implement
`IDigitalTwinMachineSceneConfigurator` in an Editor script and enter its type name in the
configuration. If it needs a local process simulator, return a `MonoBehaviour` implementing
`ITelemetrySimulationProvider`; the generic setup assigns it to
`TelemetryOperatingModeController`.

Do not add new machine assumptions to `TelemetryOperatingModeController`, the registry, cards, or layouts.

## 7. Test the data contract

Create schema-version-1 single and batch JSON payloads using the machine's real
sensor IDs. Send them through the WebGL host bridge or a development harness and test:

- normal values;
- warning and critical transitions;
- Bad and Uncertain quality;
- missing updates and stale state;
- unknown IDs;
- duplicate and out-of-order sequences;
- values that require unit or decimal fitting;
- wide, portrait, and mobile layouts.

## 8. Connect live data

Create a gateway mapping from the machine/PLC namespace to the Unity sensor IDs. Normalize values to the documented contract before they reach Unity. Keep authentication, credentials, industrial protocols, history, and high-frequency processing outside the visualization client.

## 9. Validate

Run **Tools > Digital Twin > Validate Project** and resolve all errors. Then test
simulation and live modes separately so simulated readings cannot be mistaken for
machine data.

Validation also checks that exactly one configuration is default, configured sensor IDs
match the scene, portrait pagination is reachable, both layouts share one machine-view
controller, and the site-environment FBX stays below the 10 MB project limit.

## Current add-machine demo

The project intentionally defines one production configuration. On the first Unity script
reload it is materialized as `Assets/Machine Configurations/Vibratory Feeder.asset`. To demonstrate adoption without
adding a second model, duplicate the asset temporarily, change its identity and sensor
definitions, mark exactly one copy as Default, and run scene setup. Restore the feeder as
Default after the demonstration.

No changes to `DigitalTwinSceneSetup`, the telemetry API, cards, or responsive layouts
should be required.

## Adoption review questions

- Did the generic UI or ingestion code require machine-specific edits?
- Are all new requirements configuration, model work, or true physical behavior?
- Are sensor IDs stable and documented in the gateway mapping?
- Can the machine be removed without breaking the reusable runtime?
- Can its simulator be replaced without changing the operating-mode controller?
