# Install for a Unity project

Three pieces: two Unity packages and a tools folder.

## 1. Packages

Add to `Packages/manifest.json` under `dependencies` (use the latest release tag):

```json
"com.sa.unity": "https://github.com/IAFahim/com.sa.unity.git#v0.3.1",
"com.sa.inbetween.sa": "https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.sa#v0.8.0"
```

`com.sa.unity` pulls in `com.sa.motion.unity`, Entities and Entities Graphics. Optional: `com.sa.inbetween.sa.timeline` from the same repository, path `unity/com.sa.inbetween.sa.timeline`, for the Timeline bridge track.

## 2. Tools folder

Download `SaInbetween-tools-<version>-win-x64.zip` (or `linux-x64.tar.gz`) from the release page and unpack it into your project root as `SaInbetween/`. It holds the generator, the baker and the character family file. Add the two NVIDIA files described in its README (`model/mmm_v6_soma_fp32.onnx` and `soma_rest.pose`).

Open `Edit > Project Settings > SA Inbetween`: every line should say *found*.

## 3. First bridge

Follow `docs/designer-guide.md`. Everything runs inside Unity; nothing needs a terminal.

## Notes

- Generation takes a few seconds on CPU. Do not enter Play mode while it runs.
- FBX export needs the Unity package `com.unity.formats.fbx` (Package Manager). On Linux the FBX SDK also needs the system library `libxml2.so.2`; on Arch-like systems link `libxml2.so.16` as `libxml2.so.2` in a folder on `LD_LIBRARY_PATH` before starting Unity.
