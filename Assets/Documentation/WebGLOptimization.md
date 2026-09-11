# WebGL Optimization Baseline

Last verified with Unity 6000.4.8f1 on 2026-08-11.

## Release baseline

The release build completed with zero warnings and zero errors.

- Initial compressed build: 48,127,570 bytes
- Current compressed build: 47,332,591 bytes
- Reduction: 794,979 bytes (1.65%)
- Current uncompressed user assets: approximately 56.7 MB
- Largest categories: textures (approximately 39.7 MB), then meshes (approximately 14.1 MB)

Build sizes vary between Unity versions. Compare builds made with the same editor,
platform settings, and compression settings.

## Changes already applied

- Telemetry cards and leader lines are pooled instead of destroyed and recreated.
- Reusable collection buffers avoid allocations during telemetry layout rebuilds.
- Particle path randomness is calculated once per particle instead of five times per
  particle on every frame; distance-only threshold checks avoid unnecessary square roots.
- Simplified particle and orbit-camera inspectors expose everyday controls first while
  preserving calibrated settings under clearly labelled advanced foldouts.
- TextMesh Pro example content was moved outside `Assets` to `ProjectSamples` so it
  remains recoverable without being imported or considered by builds.
- The unused Visual Scripting package was removed from the project manifest.
- The visible particle configuration was retained because its current maximum count
  is modest and preserving the approved appearance has higher value than speculative
  particle-system changes.

## Loading pass — 2026-08-11

- Previous gzip release: 47,332,591 bytes
- Current Brotli release: 35,252,565 bytes
- Download reduction: 12,080,026 bytes (25.5%)
- Build warnings: 0
- Build errors: 0
- Unity splash payload removed (optional in Unity 6)
- WebGL-only 1024 maximum applied to the galvanized-support and concrete/rock normal
  maps; their desktop/source imports and all base-colour textures remain unchanged
- Total uncompressed texture payload reduced from approximately 39.7 MB to 29.0 MB
- Data caching and decompression fallback remain enabled

For native Brotli decoding, deploy over HTTPS and configure the server to return the
correct `Content-Encoding: br` and MIME types for Unity's `.unityweb` files. The
fallback keeps the build functional on a misconfigured host, but JavaScript
decompression is slower than correct native browser decompression.

The scene has two intentional roots: `Scene` and `Digital Twin`. Its 26 telemetry
sources are organised by functional subsystem under `Telemetry Sources`; their stable
integration IDs remain on the source components rather than in the hierarchy names.
Obsolete per-sensor simulator components and one disabled collider renderer were
removed. The central `HopperProcessSimulator` remains the single simulation source.

## GitHub Pages deployment decision — 2026-08-12

The production WebGL profile uses Gzip with Decompression Fallback and Data Caching
enabled. GitHub Pages does not provide project-controlled `Content-Encoding` rules,
so this profile prioritizes reliable laptop/mobile loading over the smaller but more
CPU-intensive Brotli fallback build. Development Build remains disabled.

## Repeatable audit build

`Assets/Editor/WebGLBuildAudit.cs` produces a non-development WebGL build and prints
its total size, duration, warning count, and error count. Run Unity in batch mode with:

```text
-executeMethod WebGLBuildAudit.BuildRelease -auditOutput <output-folder>
```

The audit throws an error when the build is unsuccessful, so it can also be used in
continuous integration. The release build no longer applies texture optimisation or
rewrites source assets. Run the separate `WebGLLoadOptimization.Apply` authoring action
only when intentionally changing import settings, review those changes, and then build.

## Next visual-risk optimization tier

Textures dominate the payload. Any texture-resolution or format change should be
tested as an A/B build on representative laptop and mobile screens. Start with normal
maps and non-hero environment textures, and keep the current import settings as the
visual reference. Reflection and HDR assets should be reviewed separately because
changes can affect the approved material appearance across the whole scene.

## Load-time pass — 2026-09-08

Measured on one machine with the same editor and gzip settings throughout, so
the before and after numbers are comparable.

| | Before | After | Change |
|---|---|---|---|
| Total build | 42,685,943 B | 28,977,518 B | −32.1% |
| `.data.unityweb` | 28.27 MB | 18.94 MB | −33.0% |
| `.wasm.unityweb` | 12.30 MB | 8.55 MB | −30.5% |
| Textures | 28.9 MB | 12.3 MB | −57% |
| Meshes | 13.3 MB | 13.3 MB | unchanged |

### What produced the saving

Textures were the whole of it. Every texture was importing at 2048 for WebGL
because the per-platform overrides in the meta files were present but had
`overridden: 0`, so they never applied. The single largest asset in the build
was `Worn Metal - Normal` at 5.3 MB, 13% of everything downloaded, for a
surface never inspected closely.

WebGL ceilings are now set per texture in `WebGLLoadOptimization`, with colour
maps crunched. Crunch is not applied to normal maps: it quantises, which shows
on their gradients.

### A mistake worth recording

The first pass raised `Site Skybox` to 1024 and doubled it from 2.0 MB to
4.0 MB, because its default import was already 512 and adding an override
*raised* the ceiling rather than lowering it. Always read the effective value
before overriding it. It is pinned at 512 now.

### What did not help

Mesh compression was already at or near maximum on every model, so setting it
changed one file. Meshes are 13.3 MB because of polygon count, not import
settings, and are now the largest category at 44%. Reducing them means
decimating geometry or authoring LODs, which is model work rather than a
setting.

### Code size

IL2CPP code generation is set to size, and managed stripping to High, with
`Assets/link.xml` preserving the telemetry contracts that `JsonUtility`
resolves by name. Together these took the `wasm` from 12.30 MB to 8.55 MB.

These settings do not persist into `ProjectSettings.asset` from batch mode, so
`WebGLBuildAudit` applies them in the building session. Confirm them in Player
Settings before building from the editor.

An incremental build reuses cached IL2CPP output, so a code-size change only
shows after deleting `Library/Bee`. The measurement above is a clean build.

### Warnings

A clean build reports pre-existing `CS0618` deprecation warnings in the
telemetry and rail scripts. Incremental builds do not recompile, which is why
earlier passes recorded zero. They are unrelated to the settings above.

### Still on the table
- Film grain contributes about 2.8 MB: URP's `PostProcessData` references
  eleven grain textures and includes them whether or not the effect is used.
  The scene profile uses only Bloom, Vignette, Tonemapping and Motion Blur.
- `LiberationSans SDF` is about 1.0 MB and is included because it sits in a
  `Resources` folder. The scene uses Rajdhani throughout.
- `ReflectionProbe-0` is 2.0 MB and can be rebaked at a lower resolution.

## Model update pass — 2026-09-11

The separator model was replaced (`Separator - Full Model.fbx`,
`Separator - Cross-Section Assembly.fbx`). This pass re-measured before
changing anything: `LoadTimeAudit` records per-mesh vertex and triangle counts,
and a clean `WebGLBuildAudit` build gives per-asset shipped sizes. Same editor,
gzip, and `Library/Bee` cleared for both builds.

| | Before | After |
|---|---|---|
| Total build | 28,700,294 B | 26,679,922 B (−7.0%) |
| `.data.unityweb` | 18.68 MB | 16.75 MB |
| `.wasm.unityweb` | 8.55 MB | 8.56 MB |

### Removed without visual change

- **Film grain and SMAA lookup textures.** URP's post-processing data
  references ten film-grain textures and the SMAA area/search textures, so all
  of them shipped although the volume has no Film Grain override and the
  camera uses FXAA. `Assets/Settings/Post Process Data.asset` is a project
  copy with those slots pointed at a 4×4 placeholder. An empty or null slot
  would not work: URP's editor reloader resizes the array and refills null
  slots, but leaves filled ones alone. Adding Film Grain, or switching the
  camera to SMAA, means pointing those slots back at the package textures.
- **LiberationSans.** It was TextMesh Pro's default font and lived in a
  `Resources` folder, which ships regardless of use. The interface only uses
  Rajdhani, so TMP's default now points at Rajdhani Medium.
- **Superseded assets**, removed through `ProjectUnusedAssetCleanup` (which
  re-checks production dependencies first): the previous hopper model files,
  their extracted prefabs, three unreferenced materials, and the retired
  hover-overlay material.
- **Import flags** on the new model files: cameras, lights, blend shapes,
  visibility and animation are no longer imported. Mesh compression is
  unchanged (Low on the hero models).

Build warnings fell from 4 to 0 and the project validator reports 30 sensors,
0 errors, 0 warnings. The build report no longer lists any film-grain, SMAA or
LiberationSans asset, and all twelve placeholder slots survived a fresh editor
load, which is when URP's reloader runs.

### Where the size is now

Meshes are the largest category. The audit shows it is polygon count, not
settings — four items hold roughly three quarters of the scene's triangles:

| Mesh | Triangles | Note |
|---|---|---|
| Full model — isolation springs | 225,280 | twelve springs |
| Cross-section — isolation springs | 122,880 | the same springs, cut |
| Rock Pile (`Material Stockpile`) | 151,928 | background prop |
| Conveyor idler rollers | 25,008 each | two lines |

These need reducing in the modelling tool; see the decimation notes in the
project hand-off.

### Load time beyond download size

- **Decompression.** GitHub Pages cannot send `Content-Encoding`, so the gzip
  build is decompressed in JavaScript by Unity's fallback. A host that allows
  headers (Netlify, Cloudflare Pages) would enable native decompression and
  Brotli, measured at 25.5% smaller in the August pass.
- **Mobile textures.** The build uses the default desktop texture format. iOS
  and most Android GPUs cannot sample it, so textures are converted on the
  CPU while loading on phones. A second build with ASTC textures, chosen by
  the loader when the browser reports ASTC support, would remove that step.
  Not yet measured on a device.

### Particles

The 16 particle systems (about 3,600 particles at most) are a few kilobytes of
settings plus a 96-vertex rock mesh and a 128 px dust texture. They do not
affect loading. Their cost is per frame: roughly 1,560 mesh particles of 96
vertices each, and world collision on one dust plume.
