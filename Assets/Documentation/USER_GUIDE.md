# Digital Twin — Simple User Guide

This guide explains the main features without requiring programming knowledge.

## 1. Open the project

1. Install **Unity 6000.4.8f1** in Unity Hub.
2. Add the project folder through **Add > Add project from disk**.
3. Open `Assets/Scenes/SampleScene.unity`.
4. Wait for Unity to finish importing before pressing Play.

The first opening can take several minutes because Unity rebuilds the local `Library` cache. This is normal. Later openings are faster.

## 2. Basic controls

### Desktop or laptop

- Drag with the mouse to rotate around the machine.
- Use the mouse wheel or trackpad scroll to zoom.
- The **−** and **+** buttons also control zoom.
- **FULL BODY** shows the complete machine.
- **CROSS SECTION** shows the internal process and material flow.

### Phone or tablet

- Drag with one finger to rotate around the machine.
- Pinch with two fingers to zoom in or out.
- The interface changes automatically for portrait and landscape screens.
- Keep important controls and cards away from the browser bars and screen notch.

## 3. Understanding telemetry cards

Each telemetry card contains:

- A parameter name, such as `MOTOR SPEED`.
- A current value, such as `1480`.
- A unit, such as `rpm` or `°C`.
- A leader line showing where the measurement belongs on the machine.

Related measurements may share one card when they belong to the same equipment and their sensor positions are close together. If their warning states become different, the readings can separate into different cards automatically.

The layout fills the outside rails first. Extra inside rails appear only when more space is required. If every rail is full, higher-priority and alarmed readings are kept visible first.

## 4. Card interaction and alarm states

- Move the pointer over a card on desktop, or tap it on mobile, to highlight its card, anchor, and leader line immediately.
- Moving away starts a smooth return to the relaxed appearance.
- Normal cards use the relaxed line appearance when they are not selected.
- Warning and critical cards remain fully visible so important conditions are not missed.
- Unavailable or stale data uses the unavailable presentation instead of pretending that an old value is current.

Long parameter names remain still when they fit. When they do not fit, they move automatically using either **Ping Pong** or **Continuous** scrolling. Values and units also fit themselves by reducing text size or decimal places before asking the card for more width.

## 5. Connection-health information

The top area shows four useful items:

- **MODE** — `SIMULATION` or `LIVE DATA`.
- **GATEWAY** — whether the data connection is healthy.
- **LAST UPDATE** — how recently data arrived.
- **DATA QUALITY** — how many readings are currently good.

In simulation mode, Unity generates realistic-looking connected values for demonstrations. In live mode, values are expected from an external digital-twin gateway.

## 6. Adjust material-flow particles

1. In the Hierarchy, select `Digital Twin > Digital Twin Runtime`.
2. Find the **Particle Flow Controller** component in the Inspector.
3. Adjust only these simple controls for normal use:
   - **Flow Speed** changes the overall material-flow timeline.
   - **Quantity** changes visible particle density without changing particle size or paths.
4. Enter Play mode to check the result.

Keep both values near `1` for the designed baseline. The detailed particle components are advanced physical tuning and normally do not need editing.

## 7. Edit a telemetry reading

Telemetry sensor objects are stored under the `Telemetry Sources` parent in the Hierarchy.

Select a sensor and use its **Performance Stat Source** component:

- **Stat Id** — permanent unique ID used by live data.
- **Metric Name** — short name shown on the card.
- **Unit** — engineering unit.
- **Value Format** — displayed decimal format; for example, `0.0` shows one decimal place.
- **Current Value** — preview or simulated starting value.
- **Automatic Thresholds** — allows the value to choose Normal, Warning, or Critical automatically.
- **Preferred Rail** — preferred screen side; `Auto` is usually safest.
- **Display Priority** — higher numbers are kept visible first when space is limited.
- **Visible** — controls whether the reading may appear.

Move the sensor GameObject in the Scene view to change its world anchor position. The leader line follows that position.

Do not reuse a `Stat Id`. Live data relies on every sensor having a unique and stable ID.

## 8. Add a new telemetry sensor

The safest beginner workflow is:

1. Duplicate a similar sensor under `Telemetry Sources`.
2. Rename the GameObject using a clear human-readable name.
3. Give it a unique **Stat Id**.
4. Change its metric name, unit, value format, thresholds, and display priority.
5. Position it at the real measurement location on the machine.
6. Add it to the Telemetry Registry and responsive rail source lists if it is not already discovered.
7. Run **Tools > Digital Twin > Validate Project**.

Only group sensors when they describe the same physical equipment and their anchors are close together.

## 9. Change unit letter casing

Open `Assets/Prefabs/Performance Stat Card.prefab` and select the prefab root.

In **Performance Stat Card View > Metric Fitting**:

- Disable **Use Accurate Unit Casing** for the current all-uppercase visual style.
- Enable it for engineering-accurate symbols such as `mm/s`, `kN`, `kW`, and `MPa`.

The default is the all-uppercase visual style. The setting changes only the displayed unit casing; it does not change the measured value.

## 10. Simulation and live data

Simulation is intended for design, demonstrations, and UI testing. The generated values are connected so that flow, level, motor load, current, temperature, and vibration behave more naturally than unrelated random numbers.

Live WebGL data should follow this path:

```text
Machine sensors or PLC
        -> secure industrial gateway
        -> HTTPS or secure WebSocket
        -> Unity WebGL telemetry input
```

Do not connect a public browser directly to a PLC. See `DigitalTwinIntegration.md` for JSON examples, quality values, timestamps, and gateway messages.

## 11. Responsive layout and mobile quality

- Desktop and laptop browsers use adaptive PC/WebGL quality.
- Mobile WebGL is locked to the Mobile quality preset and 30 FPS to reduce heat and battery use.
- A vertical desktop monitor remains a desktop interface instead of being mistaken for a phone.
- Phones can switch between portrait and landscape layouts.
- Safe-area information is used to reduce overlap with notches and rounded screen areas.

For layout testing, select the object containing **Responsive Layout Shell**. Its **Device Override** can temporarily force Desktop or Mobile behavior. Return it to `Auto` before building.

## 12. Useful project tools

The Unity menu contains several tools under **Tools > Digital Twin**:

- **Validate Project** — checks sensor IDs, assignments, fonts, references, and project structure.
- **Validate Telemetry Layout Stress** — checks cards, layouts, rails, and responsive references.
- **Standardize UI Fonts** — reapplies Rajdhani Medium to labels and Rajdhani SemiBold to readings.
- **Organize and Rename Assets** — applies the approved asset organization rules.
- **Remove Verified Unused Assets** — removes only the explicitly audited unused candidates.

The setup tools are mainly for rebuilding or repairing project structure. Do not run them repeatedly just to test the scene.

## 13. Before sharing or building

1. Save the scene and project.
2. Run **Tools > Digital Twin > Validate Project**.
3. Confirm that the Console reports zero errors.
4. Test Full Body, Cross Section, zoom, rotation, card focus, and both screen orientations.
5. Make a non-development WebGL build.
6. Test the build once on desktop and once on a real phone.

For Git, keep `Assets`, `Packages`, and `ProjectSettings`. Do not upload `Library`, `Temp`, `Logs`, `UserSettings`, or generated builds to this source repository.

## 14. If something looks wrong

- **Pink material:** allow shader importing to finish and confirm the correct render-pipeline settings are present.
- **Missing font:** run **Standardize UI Fonts** and confirm the Rajdhani font assets exist in `Assets/UI/Fonts`.
- **Missing card:** check `Visible`, display priority, registry assignment, and available rail space.
- **Wrong warning state:** check the sensor threshold mode and warning/critical limits.
- **No leader line:** check that the sensor has a card presentation and is positioned near the machine.
- **Live values are stale:** check the gateway connection, sensor ID, timestamps, quality, and update interval.
- **Mobile runs hot:** confirm the Mobile quality preset and 30 FPS lock are still enabled.

When making large changes, create a Git commit first. This makes it easy to return to the last working version.

