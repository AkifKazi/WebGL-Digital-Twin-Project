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
