# Install for a Unity project

One Git URL. Tested on Unity 7000.0.0a16 with a new, empty **Universal 3D (URP)** project on Linux (0.10.0). Unity fetches the
packages with Git, so `git` must be on the PATH (Windows: Git for Windows).

## 1. Add the installer by Git URL

**Window > Package Manager > + > Install package from git URL**, paste, **Install**:

```
https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.install#v0.10.0
```

That is the only line you type. The installer (`com.sa.inbetween.install`, no dependencies of its own) then:

1. **Adds the other packages to `Packages/manifest.json`**, because Package Manager cannot follow a Git dependency from a
   package by itself: `com.sa.motion.unity` (sa-motion `e56b12f`), `com.sa.unity` 0.3.7, `com.sa.inbetween.sa` 0.10.0 and
   `com.sa.inbetween.sa.timeline` 0.3.0 from the same Git revision as the installer, and `com.unity.formats.fbx` 5.1.6 (only
   needed for Export > FBX). Unity resolves and compiles them; the Console shows no errors.
2. **Downloads the tools** from the matching GitHub release (`SaInbetween-tools-v0.10.0-linux-x64.tar.gz` or
   `-win-x64.zip`), checks the archive's SHA-256 against the release's `SHA256SUMS`, and unpacks it to
   `<project>/SaInbetween` (next to `Assets`): the generator `cli/`, the baker `bake/` and `humanoid-v1.safamily`. Both programs
   are self-contained; no .NET install is needed.
3. **Asks you once before anything from NVIDIA is downloaded** (see below), then fetches the motion model and makes the rest
   pose.
4. **Checks the project settings**: a Scriptable Render Pipeline must be set so SA characters in SubScenes are drawn (the
   Universal 3D template already has one; the Fix makes a URP asset otherwise).
5. Opens **SA > Inbetween > Setup**: every line says *ready* or has a **Fix** button, and **Fix all** does whatever is left.

Nothing else is needed. **Edit > Project Settings > SA Inbetween** then shows all five lines (Generator, Baker, Model, Character
family, Rest pose) as *found*.

## 2. The NVIDIA files (asked once)

SA Inbetween generates motion with NVIDIA's MotionBricks model. It is not part of SA Inbetween, is not in the tools archive and
is never committed or shipped by us. When you choose **Accept and download** in the dialog, the installer downloads from
NVIDIA's own repository (`NVlabs/motionbricks`, revision `54fc563`):

| File | Size | Licence | Check |
|---|---|---|---|
| `SaInbetween/model/mmm_v6_soma_fp32.onnx` | 757,864,273 bytes | [NVIDIA Open Model Agreement](https://www.nvidia.com/en-us/agreements/enterprise-software/nvidia-open-model-agreement/) | SHA-256 `856419dc...60f4fb` |
| `combat_quickstep_ff_idle_180_R_002__A365.bvh` (only frame 0 is kept, as `SaInbetween/soma_rest.pose`) | 2,241,123 bytes | NVIDIA Sample Data Evaluation License | SHA-256 `5e175607...7138fa` |

The rest pose is the SOMA skeleton in frame 0 of that sample; the installer makes it on your computer (77 joints, 2,156 bytes,
the same values the tools used before, within 1e-6). The answer is stored in `UserSettings/SaInbetweenSetup.json` of this
project. **Not now** leaves everything else working: playing baked clips, the Timeline, exports. Only Generate needs the two
files.

## 3. Linux: FBX export (optional)

The FBX SDK inside the FBX Exporter loads `libxml2.so.2`. Distributions that ship only `libxml2.so.16` (Arch and its
derivatives) need a link; the Setup window's **Fix** on that line makes it, or by hand:

```sh
mkdir -p ~/.local/lib/unity-libxml2
ln -s /usr/lib/libxml2.so.16 ~/.local/lib/unity-libxml2/libxml2.so.2
LD_LIBRARY_PATH=~/.local/lib/unity-libxml2 unityhub
```

Without it **Export > FBX** says `The FBX SDK native library could not load` with the library name. Everything else works.

## 4. First steps

- **SA > Inbetween > Create Demo Scene**: the First Bridge window, three buttons (`docs/designer-guide.md`, "Your first 10 minutes").
- **SA > Inbetween > Create Chain Demo**: one Timeline row with run, jump, land, roll and walk blocks; every gap is baked into an
  in-between on its own (`docs/designer-guide.md`, "A chain of poses on one row").
- **Assets > SA > Add SA Character That Plays This Clip** (select a `.saclip` and your character): a SubScene with the SA character
  and an **SA Clip Player**; press Play. This needs no NVIDIA file and works the same in a player build.

## Offline, behind a proxy, or a fork

Before Unity starts, these environment variables point the installer somewhere else (each takes `https://` or `file://`):
`SA_INBETWEEN_RELEASE_URL` (folder holding `SHA256SUMS` and the tools archive), `SA_INBETWEEN_MODEL_URL`,
`SA_INBETWEEN_REST_SOURCE_URL`. The SHA-256 checks stay the same. In batch mode `SA_INBETWEEN_ACCEPT_NVIDIA=1` answers the NVIDIA
question, and `-executeMethod SA.Inbetween.Install.InbetweenSetup.BatchFixAll` runs every step and exits 0 when all are ready.

## By hand (what the installer writes)

If you prefer to edit `Packages/manifest.json` yourself, these are the lines it adds:

```json
"com.sa.motion.unity": "https://github.com/IAFahim/sa-motion.git?path=packages/com.sa.motion.unity#e56b12fe81fb7166c2c8a0984025220e3c07bb48",
"com.sa.unity": "https://github.com/IAFahim/com.sa.unity.git#v0.3.7",
"com.sa.inbetween.sa": "https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.sa#v0.10.0",
"com.sa.inbetween.sa.timeline": "https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.sa.timeline#v0.10.0",
"com.unity.formats.fbx": "5.1.6",
```

All three SA lines are needed: `com.sa.unity` depends on `com.sa.motion.unity`, and Unity cannot fetch that one by itself
(without it: `Package [com.sa.motion.unity@0.1.0] cannot be found`). The tools archive is on the
[v0.10.0 release page](https://github.com/IAFahim/sa-inbetween/releases/tag/v0.10.0) with `SHA256SUMS`
(`sha256sum -c SHA256SUMS --ignore-missing`); unpack it into the project folder so it becomes `<project>/SaInbetween`.

Optional, only if the project also uses Tl: `"com.tl.unity": "https://github.com/IAFahim/tl.unity.git?path=/com.tl.unity#main"`.
The SA packages do not need it. With it, a generation timeline's Tl Settings (When it ends: Loop or Next) are honoured by Bake.

## Notes

- Generation takes a few seconds on CPU per in-between. Do not enter Play mode while it runs.
- **Edit > Project Settings > SA Inbetween** can point at a tools folder anywhere on disk instead of `<project>/SaInbetween`.
  That choice is stored in your Editor preferences, so it applies to every project on this computer.
- On Windows the model runtime uses the Microsoft Visual C++ Redistributable 2015-2022 (x64); if Generate fails with exit
  `-1073741515`, install it from https://aka.ms/vs/17/release/vc_redist.x64.exe. `windows-smoke.ps1` from the release page checks
  the tools before you open Unity (`docs/windows-tester-checklist.md`).
