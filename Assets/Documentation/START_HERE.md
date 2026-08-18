# Digital Twin Project — Start Here

## Project structure

- `Scenes` contains the production scene and its baked lighting data.
- `Scripts/Runtime` contains the active application code. Every script filename
  matches its primary class name.
- `Models`, `Materials`, `Materials/Textures`, `UI/Sprites`, and `Prefabs` contain
  the authored production assets without deep nesting.
- `UI/Fonts` contains the production Rajdhani Medium and SemiBold TMP font assets;
  `UI/Sprites` contains the interface artwork.
- `Editor` contains validation, build-audit, and repeatable setup tools.

## Everyday controls

- Select `Digital Twin/Digital Twin Runtime` in the Hierarchy.
- Use `Particle Flow Controller` for global particle speed and quantity.
- Keep individual particle-path components under Advanced Physical Tuning unless the
  geometry or outlet path changes.
- Use `Telemetry Operating Mode Controller` to switch between generated simulation
  data and live telemetry.
- Read connection health from the responsive top bar: mode, gateway state, latest
  update age, and total sensor quality are always visible in wide and portrait layouts.

## Before a demo or deployment

1. Save the scene.
2. Run `Tools > Digital Twin > Validate Project`.
3. Confirm that the Console reports zero validation errors.
4. Make a non-development WebGL build using the Web build profile.
5. Test once in a private/incognito browser window to measure a true first load.
6. Test again normally to confirm browser caching works.

## Adding a sensor

Use an existing sensor as a structural reference, but assign a unique industrial tag,
unit, thresholds, display priority, and world position. Add it to the Telemetry
Registry and both responsive rail managers. The validation tool reports any missed
assignment or duplicate ID.

## Live-data boundary

The Unity player accepts individual or batched JSON readings through
`TelemetryJsonIngestor`. A production deployment should connect the browser to a
secure HTTPS/WSS edge gateway, not directly to a PLC. The gateway is responsible for
authentication, protocol conversion, rate limiting, timestamp preservation, and
mapping plant-system tags to the stable IDs used in this project.

See `DigitalTwinIntegration.md` for the payload contract and integration examples.

## Deployment profile

GitHub Pages uses the Web build profile with Gzip, Decompression Fallback, and Data
Caching enabled. Do not deploy a Development Build; it is larger and exposes debug
overhead.
