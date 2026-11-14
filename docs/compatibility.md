# Versions and compatibility

What SA Inbetween 0.9.3 was built and tested with. "Tested" means a run with its counts recorded in
`docs/playtest.md` or `docs/reports/`; anything else is marked.

## Packages that go together

| Package | Version | Git ref in `Packages/manifest.json` |
|---|---|---|
| `com.sa.inbetween.sa` (recipes, importer, Inspector, demo) | 0.9.3 | `https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.sa#v0.9.3` |
| `com.sa.inbetween.sa.timeline` (optional) | 0.1.3 | `https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.sa.timeline#v0.9.3` |
| `com.sa.unity` | 0.3.7 | `https://github.com/IAFahim/com.sa.unity.git#v0.3.7` |
| `com.sa.motion.unity` | 0.1.0 | `https://github.com/IAFahim/sa-motion.git?path=packages/com.sa.motion.unity#e56b12fe81fb7166c2c8a0984025220e3c07bb48` |
| `com.unity.formats.fbx` (only for Export > FBX) | 5.1.6 | registry |
| Tools archive (`SaInbetween/`: generator, baker, family file) | v0.9.3 | release asset `SaInbetween-tools-v0.9.3-<os>.zip/.tar.gz` |
| NVIDIA model `mmm_v6_soma_fp32.onnx` | MotionBricks v6 SOMA fp32 | 757,864,273 bytes, SHA-256 `856419dc...60f4fb` (checked before each Generate) |

Mixing releases is not supported: `com.sa.inbetween.sa` 0.9.3 declares `com.sa.unity` 0.3.7, and the tools of an
older release may not read files the 0.9.3 Editor code writes. Older pairs: sa-inbetween 0.9.2 with com.sa.unity 0.3.6,
0.9.1 with 0.3.6, 0.9.0 with 0.3.4.

## Unity

| Unity | Render pipeline | Status |
|---|---|---|
| 7000.0.0a12 | URP 17.7 (Universal 3D template) | **Tested** on Linux: clean install (`docs/reports/clean-install-2026-11-14.md`) and the 0.9.3 test runs in `docs/playtest.md` |
| 7000.0.0a16 | URP 17.7 | Installed here, not tested in this release |
| Unity 6 (6000.x) and older | any | **Not supported**: the packages declare `"unity": "7000.0"` and use Unity 7 APIs |

The packages need Entities 6.7.0 and Entities Graphics 6.7.0 (pulled by `com.sa.unity`) and, for the Timeline
package, Timeline 6.7.0. The Inspector preview and the First Bridge window render offscreen through the Editor's
active graphics API; this release rendered them on Vulkan on Linux.

## Operating systems

| OS | Tools archive | Status |
|---|---|---|
| Linux x64 (glibc; tested on Arch-based Omarchy 4.5, kernel 7.2) | `linux-x64.tar.gz` | **Tested** end to end. FBX export needs the `libxml2.so.2` link (`docs/install.md` step 4) |
| Windows 10/11 x64 | `win-x64.zip` | **Not run by a person yet.** Checked statically, and the release workflow runs `scripts/windows-smoke.ps1` on GitHub's Windows runner (archive hash, layout, PE32+ x64, Visual C++ runtime, both programs start). Generate on Windows needs a tester: `docs/windows-tester-checklist.md` |
| macOS | none | Not built, not supported |
| Linux arm64, Windows arm64 | none | Not built |

## Generation time

Generation runs on the CPU through ONNX Runtime. It does not use the GPU, so the graphics card does not change it.
Measured on the test machine (AMD Ryzen 5 8500G, 6 cores / 12 threads, 30 GB RAM, Linux):

| What | Time |
|---|---|
| CLI `generate`, rest pose to rest pose, 26 frames (the smoke test) | 4.1 s, plus 0.2 s bake |
| Inspector Generate, Mixamo stab frame 40 to slash start, 1.0 s, 30 frames (0.9.2 clean install, first run) | under 5 s |
| Generate again, Synty jump apex to landing, 0.8 s, 26 frames (clips already read; two pose reads, generate, bake) | about 6 s wall clock |
| Generate from a 12.27 s, 60 fps clip, first time (reading the clip: 0.25 s, 369 frames) | about 14 s wall clock |

Memory: the generator loads the 723 MiB model; plan on about 1.5 GB free while it runs. A slower CPU scales the time
roughly with its single-thread speed; a 4-core laptop CPU can take two to three times as long.

## Rigs and clips

| Source | Status |
|---|---|
| Humanoid FBX characters and clips (Mixamo, Synty Polygon and Sidekick) | **Tested** |
| Humanoid clips that copy another avatar (Synty animation FBX files without a mesh) | **Tested** (converted on the character you pick) |
| `.anim` clips, Humanoid or Generic | Tested (Humanoid `.anim` in the sweep; Generic in the Clips tests) |
| 60 fps clips, clips longer than 10 s | Tested: a 12.27 s, 60 fps Humanoid `.anim` made from the Synty jump, started at 11.37 s |
| Root-motion clips (Synty `RootMotion` and `RootMotionVertical` jumps) | **Tested** |
| Generic rigs with humanoid bone names, no avatar | Works by bone names; Humanoid export is unavailable. See `docs/known-limitations.md` |
| Non-humanoid rigs (animals, props, vehicles) | **Not supported** |
| Legacy clips | Not supported; the recipe says to switch the FBX to Humanoid or Generic |
