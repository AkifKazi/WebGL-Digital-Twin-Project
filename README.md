# WebGL Digital Twin Project

An interactive Unity WebGL digital-twin prototype for industrial telemetry visualization. The project includes responsive telemetry rails, grouped sensor cards, leader lines, connection-health UI, simulated/live-data accommodation, and mobile interaction support.

## Open the project

1. Install **Unity 6000.4.8f1** through Unity Hub.
2. Clone or download this repository.
3. In Unity Hub, select **Add > Add project from disk** and choose the repository folder.
4. Open `Assets/Scenes/SampleScene.unity` if it is not already open.
5. Allow Unity to restore packages and rebuild the local `Library` folder on the first launch.

The first launch takes longer because `Library` is intentionally excluded from version control. Later launches will use the locally generated cache.

## Repository contents

- `Assets/` — scenes, scripts, prefabs, models, materials, UI, fonts, and editor tools.
- `Packages/` — reproducible Unity package dependencies.
- `ProjectSettings/` — Unity project and platform configuration.

Generated builds, caches, profiler captures, IDE files, recovery archives, and private project references are intentionally excluded.

## WebGL

Use the project’s optimized WebGL build workflow from Unity when generating a deployable build. The generated `Builds/` directory is not stored on the source branch; this keeps project clones small and prevents compiled output from being mixed with editable source files.

