# Install for a Unity project

Four steps: three Unity packages, the tools folder, two NVIDIA files, and (Linux only, for FBX export) one library link.

Tested on Unity 7000.0.0a12 with a new **Universal 3D (URP)** project on Linux. Unity fetches the packages with Git, so `git` must be on the PATH (Windows: Git for Windows).

## 1. Packages

Open `Packages/manifest.json` in your project and add these lines at the top of `dependencies`:

```json
"com.sa.motion.unity": "https://github.com/IAFahim/sa-motion.git?path=packages/com.sa.motion.unity#e56b12fe81fb7166c2c8a0984025220e3c07bb48",
"com.sa.unity": "https://github.com/IAFahim/com.sa.unity.git#v0.3.7",
"com.sa.inbetween.sa": "https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.sa#v0.9.3",
"com.unity.formats.fbx": "5.1.6",
```

- All three SA lines are needed. `com.sa.unity` depends on `com.sa.motion.unity`, and Unity cannot fetch that one by itself: without the first line it stops with `Package [com.sa.motion.unity@0.1.0] cannot be found`. The commit is the sa-motion revision `com.sa.unity` 0.3.7 was tested with.
- `com.sa.unity` brings Entities and Entities Graphics 6.7.0.
- `com.unity.formats.fbx` is the FBX Exporter (it ships with the Editor). It is only needed for **Export > FBX**.
- Optional, for the Timeline bridge track: `"com.sa.inbetween.sa.timeline": "https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.sa.timeline#v0.9.3"`.
- Generation timelines (author and bake in the Timeline window) are in `com.sa.inbetween.sa.timeline` **0.2.0**, on branch
  `feat/timeline-gen` until it is released: `"com.sa.inbetween.sa.timeline": "https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.sa.timeline#feat/timeline-gen"`
  (it works with `com.sa.inbetween.sa` v0.9.3).
- Optional, only if the project also uses Tl: `"com.tl.unity": "https://github.com/IAFahim/tl.unity.git?path=/com.tl.unity#main"`.
  The SA packages do not need it. With it, a generation timeline's Tl Settings (When it ends: Loop or Next) are honoured by Bake
  Timeline. Tl needs the newest Unity 7 editor and brings Entities Graphics 7.0.0; read its `docs/USING-ECS.md` first.

Go back to Unity. It downloads the packages and compiles; the Console shows no errors.

## 2. Tools folder

From the [v0.9.3 release page](https://github.com/IAFahim/sa-inbetween/releases/tag/v0.9.3) download `SHA256SUMS` and the tools for your system:

- Windows: `SaInbetween-tools-v0.9.3-win-x64.zip`
- Linux: `SaInbetween-tools-v0.9.3-linux-x64.tar.gz`

Check the download. Linux: `sha256sum -c SHA256SUMS --ignore-missing` prints `OK` for it. Windows PowerShell: `Get-FileHash .\SaInbetween-tools-v0.9.3-win-x64.zip` prints the hash on its line of `SHA256SUMS`.

The archive holds one folder, `SaInbetween`. It must end up in the project folder, next to `Assets` (not inside it):

- Linux: `tar xzf SaInbetween-tools-v0.9.3-linux-x64.tar.gz -C <project>`
- Windows: right-click the zip, **Extract All**, then move the `SaInbetween` folder from the extracted folder into the project folder.

It contains the generator (`cli/`), the baker (`bake/`) and the character family file. Both programs are self-contained, so no .NET install is needed. On Windows the model runtime uses the Microsoft Visual C++ Redistributable 2015-2022 (x64), which most PCs already have; if Generate fails with exit `-1073741515`, install it from https://aka.ms/vs/17/release/vc_redist.x64.exe.

## 3. The two NVIDIA files

They are not redistributed with the tools (see `SaInbetween/README.md`). Put them here:

1. `SaInbetween/model/mmm_v6_soma_fp32.onnx`: the MotionBricks SOMA model, 757,864,273 bytes, SHA-256 `856419dc72aeb14e591eebf4dd0ea05734a2506ddb00bcd39dd6dac2ad60f4fb`.
2. `SaInbetween/soma_rest.pose`: the SOMA rest pose, 2,156 bytes (how to make it: "Tools folder, for the technical artist" in `docs/designer-guide.md`).

Open `Edit > Project Settings > SA Inbetween`: all five lines (Generator, Baker, Model, Character family, Rest pose) say *found*.

## 4. FBX export on Linux

The FBX SDK inside the FBX Exporter loads `libxml2.so.2`. Distributions that ship only `libxml2.so.16` (Arch and its derivatives) need a link, made once, and Unity Hub (or the Editor) started with that folder on `LD_LIBRARY_PATH`:

```sh
mkdir -p ~/.local/lib/unity-libxml2
ln -s /usr/lib/libxml2.so.16 ~/.local/lib/unity-libxml2/libxml2.so.2
LD_LIBRARY_PATH=~/.local/lib/unity-libxml2 unityhub
```

Without it **Export > FBX** says `The FBX SDK native library could not load` with the library name. Everything else works without it.

## 5. First bridge

Choose **SA > Inbetween > Create Demo Scene** and press the three buttons in the window (`docs/designer-guide.md`,
"Your first 10 minutes"). Everything runs inside Unity; nothing needs a terminal.

On Windows, `windows-smoke.ps1` from the same release page checks the tools before you open Unity: the archive hash,
the Visual C++ runtime, that both programs start, the model's SHA-256 and one generate
(`docs/windows-tester-checklist.md`).

## Notes

- Generation takes a few seconds on CPU. Do not enter Play mode while it runs.
- `Edit > Project Settings > SA Inbetween` can point at a tools folder anywhere on disk instead of `<project>/SaInbetween`. That choice is stored in your Editor preferences, so it applies to every project on this computer.
