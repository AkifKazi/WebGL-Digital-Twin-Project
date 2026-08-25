# Digital Twin Integration

## Runtime architecture

Recommended production data path:

```text
Sensors / VFD / PLC
        |
        | OPC UA, fieldbus, or vendor protocol
        v
Industrial edge gateway
        |
        | validated and normalized telemetry
        | HTTPS or secure WebSocket (WSS)
        v
Unity WebGL TelemetryJsonIngestor
        v
TelemetryRegistry -> PerformanceStatSource -> cards and leader lines
```

Do not expose a plant-floor OPC UA server or PLC directly to the public browser.
Terminate OT protocols at an authenticated edge/backend service. The gateway
maps plant tags to the stable IDs used by this visualization and forwards only
the values the user is authorized to see.

OPC UA `DataValue` semantics are retained in the Unity contract:

- value
- Good, Uncertain, or Bad quality
- source timestamp (kept unchanged from the source)
- receive timestamp
- status code
- sequence number
- provider identity

Bad or stale samples do not overwrite the last usable numeric value. They set
the card to its unavailable presentation; the last healthy alarm state is restored
when good data resumes. Sources become stale automatically when their configured
update timeout expires. Non-finite values and duplicate or out-of-order sequence
numbers from the same provider are rejected before the UI enters Live mode.

## Stable tag IDs

The following are representative mappings, not an exhaustive tag schedule. The machine
configuration and gateway mapping remain authoritative for the complete sensor set.

| Tag | Meaning | Unit |
|---|---|---|
| `FEED-01.FLOW.IN` | Inlet mass-flow rate | kg/s |
| `FEED-01.FLOW.OUT` | Discharge mass-flow rate | kg/s |
| `VIB-01.MOTOR.TEMP` | Motor temperature | °C |
| `VIB-01.MOTOR.SPEED` | Motor rotational speed | rpm |
| `VIB-01.BEARING.VEL_RMS` | Bearing-housing vibration velocity RMS | mm/s RMS |
| `VIB-01.MOTOR.POWER` | Three-phase active power | kW |
| `VIB-01.MOTOR.CURRENT` | Line current | A |
| `HOP-01.LEVEL` | Hopper fill level | % |
| `VIB-01.BEARING.TEMP` | Drive bearing temperature | °C |
| `VIB-01.DRIVE.FREQUENCY` | Variable-frequency drive output frequency | Hz |
| `VIB-01.MOTOR.LOAD` | Motor shaft load relative to nameplate rating | % |
| `FEED-01.BELT.SPEED` | Discharge belt linear speed | m/s |
| `HOP-01.MASS` | Estimated contained material mass | t |

These are visualization tags, not a replacement for the facility's official
tag namespace. Map them at the gateway or update the Scriptable/scene
configuration once the real P&ID and tag schedule are available.

## JSON input

Single sample:

```json
{
  "schemaVersion": 1,
  "sensorId": "VIB-01.MOTOR.TEMP",
  "value": 66.4,
  "quality": 0,
  "sourceTimestampUnixMs": 1786464000000,
  "statusCode": 0,
  "sequenceNumber": 1842,
  "providerId": "plant-a-edge-01"
}
```

Quality values are `0 = Good`, `1 = Uncertain`, `2 = Bad`, and `3 = Stale`.
Schema version `1` is the current contract. A missing version is treated as
legacy version `0` for compatibility; any other version is rejected.

For a JavaScript WebGL host:

```javascript
unityInstance.SendMessage(
  "Digital Twin Runtime",
  "PushJson",
  JSON.stringify(reading)
);
```

Batch messages use `{ "schemaVersion": 1, "readings": [ ... ] }` and call
`PushBatchJson`.
The first valid live sample automatically changes the runtime from Simulation
to Live mode. All remaining sensors show stale until their live samples arrive,
preventing simulation values from being mistaken for plant values.

## Provider boundary

All external and test providers terminate at the same generic runtime boundary:

- `ITelemetryReadingSink` accepts normalized `TelemetryReading` values.
- `TelemetryJsonIngestor` adapts browser/API JSON into that boundary.
- `ITelemetrySimulationProvider` allows the operating-mode controller to start
  or reset a machine simulator without depending on the hopper implementation.

Providers do not manipulate cards or leader lines directly. The registry updates
the matching `PerformanceStatSource`, and the presentation reacts to that state.

## Browser API adapter

The WebGL hosting page should own network connections. A minimal WebSocket adapter
can normalize gateway messages and forward them to the Unity instance:

```javascript
function connectTelemetry(unityInstance, socketUrl, accessToken) {
  const socket = new WebSocket(socketUrl, ["telemetry.v1"]);

  socket.addEventListener("open", () => {
    unityInstance.SendMessage(
      "Digital Twin Runtime",
      "ReportGatewayConnected",
      "site-a-edge-01"
    );
    socket.send(JSON.stringify({ type: "authenticate", token: accessToken }));
  });

  socket.addEventListener("message", event => {
    const message = JSON.parse(event.data);
    const method = Array.isArray(message.readings) ? "PushBatchJson" : "PushJson";
    unityInstance.SendMessage("Digital Twin Runtime", method, JSON.stringify(message));
  });

  socket.addEventListener("close", () => {
    unityInstance.SendMessage(
      "Digital Twin Runtime",
      "ReportGatewayDisconnected",
      "socket-closed"
    );
  });

  return socket;
}
```

This is a host-page example, not an authentication prescription. Prefer secure,
short-lived browser credentials such as an HttpOnly session or approved token
exchange. Do not hard-code credentials in JavaScript or the Unity build.

For REST polling, retrieve the current versioned batch over HTTPS and pass the
response body to `PushBatchJson`. Use WebSockets for ongoing state where low
latency matters. Keep history and high-frequency waveforms in the backend; send
Unity only the current or aggregated values required for visualization.

## Acceptance rules

- Unknown sensor IDs are rejected and cannot switch the player into Live mode.
- `NaN` and infinite numeric values are rejected.
- Non-zero duplicate or older sequence numbers from the same provider are rejected.
- Missing source timestamps use the browser receive time.
- Invalid quality integers are converted to Bad quality.
- Accepted Good or Uncertain readings update the visible numeric value.
- Bad and Stale readings retain the last usable number but show Unavailable state.
- The first accepted live reading switches from Simulation to Live mode.

## Connection-health contract

The top bar reports the operating mode, gateway state, age of the latest update,
and aggregate sensor quality. In Live mode, the gateway changes to `DEGRADED`
after 3 seconds without a message and `OFFLINE` after 10 seconds. These defaults
are deliberately visible in the `ConnectionHealthMonitor` Inspector so they can
be aligned with the real gateway's publish interval and service-level agreement.

The WebGL host can optionally report transport-level state on the
`Digital Twin Runtime` object:

```javascript
unityInstance.SendMessage("Digital Twin Runtime", "ReportGatewayConnected", "plant-a-edge-01");
unityInstance.SendMessage("Digital Twin Runtime", "ReportGatewayHeartbeat", "plant-a-edge-01");
unityInstance.SendMessage("Digital Twin Runtime", "ReportGatewayDisconnected", "socket-closed");
```

Every accepted `PushJson` or `PushBatchJson` sample also counts as gateway
activity, so explicit heartbeat calls are only needed when the gateway can remain
healthy while no sensor values change. `ReportGatewayConnected` and heartbeat
calls refresh the last-contact clock; disconnect immediately shows `OFFLINE`.

## Simulation model

`HopperProcessSimulator` is a deterministic, correlated process model for UI
development. It is intentionally separate from the live ingestion path.

- Hopper level follows mass balance: inlet mass minus discharge mass.
- Discharge responds to fill head and drive-speed fraction.
- Motor power responds to process load.
- Three-phase current is derived from active power, line voltage, power factor,
  and motor efficiency.
- Temperature follows a slow first-order thermal response.
- Vibration follows speed/load with low-amplitude continuous noise.
- Drive frequency is correlated with simulated shaft speed.
- Motor load is derived from active input power, efficiency, and rated output.
- Bearing temperature follows the motor thermal state at a lower temperature.
- Hopper mass is derived from usable volume, bulk density, and fill percentage.
- Belt speed follows the drive speed ratio.
- Noise is smooth rather than independent random jumps.

## Card presentation rules

Every visible `PerformanceStatSource` maps to one `TelemetryCardPresentation` and one
card. The API, registry, and UI do not group readings. This keeps identity, spacing,
alarm behavior, and future gateway mapping deterministic.

Long labels use the Inspector-selected adaptive wrapping or sliding mode on wide rails.
Portrait top and bottom rails default to one-line sliding when a label overflows.
Adaptive wrapping grows to at most four lines and then falls back to sliding; neither
mode adds an ellipsis. Value and unit are composed in one text object on one baseline. The fitter may
reduce decimal precision and type size to retain the complete unit, while rail/card width
remains fixed. **Use Accurate Unit Casing** is available on the card prefab for SI-aware
symbols such as `kW`, `kN`, and `mm/s`; the approved default remains uppercase.

The current leader-line prefab intentionally uses the approved thin, low-opacity visual
tuning. With secondary rails occupied, normal lines use the lower resting opacity. When
secondary rails are empty they transition to the higher resting opacity. Hover/tap focus
is immediate, while warning and critical presentations remain fully emphasized.

## Responsive rail allocation

The outer rails are always evaluated first. A presentation that does not fit
its preferred outer rail is offered to the opposite outer rail before either
inner overflow rail is enabled. Allocation order is preferred outer, opposite outer,
preferred inner, opposite inner, then the next portrait page when none has physical
capacity. Alarm severity and display priority determine page order, with alarms on page
one. The arrow beside the zoom controls fades out over 0.3 seconds when its direction has
no page. Inner rails remain inactive when the outer rails hold all content, preserving
the maximum stage area.

Layout selection uses device class as well as orientation. Desktop/laptop WebGL
keeps the wide interface on a portrait monitor, including 1080x1920 displays.
Mobile browsers may switch between mobile portrait and landscape layouts. The
device mode can be overridden in `ResponsiveLayoutShell` for testing.

The initial equipment values are plausible placeholders for a 22 kW, 400 V,
approximately 1480 rpm drive and an 80 m³ hopper. Replace nameplate values,
bulk density, operating limits, and alarm thresholds with approved engineering
data before presenting the UI as an operational twin.

The sample scene intentionally includes warning/critical demonstration values so reviewers
can see alarm styling and priority. These are a failure scenario, not approved operational
limits. Replace values and thresholds with engineering-approved data before live use.

## Standards basis

- OPC UA Part 4, `DataValue`: value, status/quality, and source/server timestamps.
- OPC UA Part 14: PubSub transport over MQTT where an MQTT architecture is used.
- Eclipse Sparkplug: timestamped typed metrics and birth/death state conventions.
- ISO 20816-1: vibration magnitude/change and machine-specific operational limits.

The current `7.1` and `11 mm/s RMS` vibration boundaries are provisional UI
defaults. ISO 20816 evaluation depends on machine class, installation, measuring
location, operating condition, and the relevant machine-specific part of the
standard. They must be reviewed by the responsible mechanical engineer.
