# Clean-install designer test, 2026-11-14

A new Unity project, installed only by `docs/install.md` and used only by `docs/designer-guide.md`, on Linux.
Unity 7000.0.0a12 (batchmode Editor with graphics, driven through the Unity CLI; Inspector buttons were invoked through
the same editor methods they call). Packages from the Git URLs, tools from the GitHub release. NVIDIA model and rest pose
copied in by hand, as the guide says. Assets: a Mixamo character and two Mixamo animation FBX files (Neophyte, `mixamorig:`
bones), not committed.

## Result

The final run, from a fresh copy of the template-created project with sa-inbetween **v0.9.2**, went end to end with
0 compile errors, 0 `CS` warnings and no package or baking errors:

| Step (designer guide) | Result |
|---|---|
| New project | `-createProject -cloneFromTemplate com.unity.template.urp-blank-17.3.0` (Universal 3D) |
| Packages (install.md 1) | 4 manifest lines; com.sa.motion.unity `e56b12f`, com.sa.unity `v0.3.6` (`35947af`), com.sa.inbetween.sa `v0.9.2`, com.unity.formats.fbx 5.1.6 resolve and compile |
| Tools (install.md 2) | `SHA256SUMS` OK for both tools archives; `tar xzf ... -C <project>` gives `<project>/SaInbetween` |
| NVIDIA files (install.md 3) | model 757,864,273 bytes, SHA-256 `856419dc...60f4fb`; `soma_rest.pose` 2,156 bytes |
| Project Settings > SA Inbetween | 5/5 found; generator is the self-contained `cli/sa-inbetween` launcher |
| Import Mixamo FBX | imported as Generic, no avatar; Rig > Humanoid > Apply gives valid Humanoid avatars on all 3 |
| Select 2 takes + character, Create > SA > Inbetween Bridge | `Assets/Mixamo/Stabbing_to_Stable Sword Outward Slash.asset`, start and goal are Unity clip sources on their own FBX, Character = Neophyte_RIG |
| Start at frame 40, Generate | Up to date within 5 s: 30 frames (1.0 s asked, 0.967 s), 12,224-byte `.saclip`, `.anim` saved |
| Seams (bone directions in hips space, 10 limb segments) | Generic `.anim` on the character: first frame vs stab frame 40 0.02 deg worst, last frame vs slash frame 0 0.00 deg; Humanoid `.anim` 0.03 / 0.04 deg worst; the two source frames differ by 38.2 deg mean, 89.9 deg worst |
| Preview (Inspector) | Skeleton preview ready, 3 layers (result, start ghost, goal ghost); character preview bound to Neophyte; rendered offscreen at 0, 0.48, 0.97 s; scene untouched (0 roots, not dirty) |
| Export > Animation Clip | 279 curves, 0.967 s |
| Export > Humanoid clip | 102 curves, 0.967 s |
| Export > FBX | written and reimported as Generic with take `Stabbing_to_Stable Sword Outward Slash`, 0.967 s; open scenes stay clean |
| Assets > SA > Convert to .saclip | `Stabbing.saclip`, 51,676 bytes (30 Hz) |
| Preview on character (Play mode) | after Set Up Character, Engine Cpu, Shared Family Authoring, SubScene: player Status *Playing Stabbing_to_Stable Sword Outward Slash*, 1 character, the bridge contribution at weight 1 loops 0..0.967 s, 69 bone entities move; scene not dirty after Play |

## What failed on the way, and the fix (all in v0.9.2 unless noted)

1. **install.md missed a package.** With only the two documented lines Unity stops: `Package [com.sa.motion.unity@0.1.0] cannot be found`. Git packages cannot pull Git dependencies. install.md now lists the sa-motion line (pinned commit) and says why.
2. **Windows would launch the generator through `dotnet`.** Discovery took `cli/sa-inbetween.dll` when it was listed before `sa-inbetween(.exe)`; on Linux it found the dll here, on Windows (alphabetical listing) the dll always wins, so a PC without a global .NET could not generate. Discovery now prefers the self-contained launcher (test `Discovery_prefers_the_self_contained_launcher_over_its_dll`).
3. **Unity clip sources were sampled adaptively.** com.sa.unity 0.3.6 made `FamilyClips.TrySample` add frames (2-8x) where a clip moves fast. The converter encoded those as the source clip, so `e-1f` / `s+1f` context steps were 1/60-1/240 s instead of 1/30 s, and frame indices no longer meant 30 Hz frames (the Mixamo stab converted to 97,100 bytes, now 51,676). The converter samples at a fixed 30 Hz again (`TrySampleFixed`), and the source cache key changed so old caches are not reused. The real-asset tests caught it once they ran (see below).
4. **Create > SA > Inbetween Bridge ignored Unity clips.** With two FBX takes selected it made an empty `Assets/Inbetween.asset`. It now takes FBX takes, `.anim` clips and model files, fills the character, and names single-take FBX sources after the file (Mixamo takes are all named `mixamo.com` or `Scene`).
5. **Tools archive folder name.** It unpacked as `SaInbetween-linux-x64/`, so "unpack into the project root" did not give `SaInbetween/`. The archives now hold `SaInbetween/` directly.
6. **Unclear FBX diagnostic.** Without `libxml2.so.2` the message was `Unable to load shared library 'UnityFbxSdkNative' ... consider setting LD_DEBUG`, which never names libxml2. It now says libxml2.so.2 is missing and how to link it (test `Linux_library_probe_finds_libc_and_misses_an_absent_library`). install.md has the exact commands.
7. **Missing designer steps.** The guide never said to set Mixamo FBX files to Humanoid, nor how to make the SA character that **Preview on character** needs. Without Engine Cpu the SubScene bake logs `RigAuthoring Engine must be Cpu for timeline rigs` and the player waits forever. Both are now numbered steps.
8. **Smaller fixes.** Baker published with invariant globalization (no libicu needed on Linux); hints for a non-executable launcher (`chmod +x`), Windows DLL-not-found exit `-1073741515` (Visual C++ Redistributable), and the missing FBX package (exact manifest line); two `FindFirstObjectByType` obsolete warnings removed.

## Tests (copy project, not the owner's Editor)

`~/inbetween-test-project`: the clean project plus `Assets/Synty` and `Assets/InbetweenDemo` copied from TestingAssets,
the v0.9.1 tools with the NVIDIA model, `testables` com.sa.inbetween.sa and com.sa.inbetween.sa.timeline, libxml2 link on.

- `SA.Inbetween.Clips.Tests`: 49 total, 48 passed, 1 failed, 0 skipped (before the fixed-rate fix: 47 passed, 2 failed).
  The 4 real-asset tests run: `RealExportTests` x2 pass (E2 bone direction 0.554 deg, E3 FBX vs .anim 1.074 deg, 7.05 mm),
  `RealPoseTests` passes (capture vs frame 41 0.0043 deg), `RealJumpTests.SyntyJumpGates` passes every enforced gate but one:
  **G3 foot penetration 3.45 cm against a 3 cm gate.** The start frame itself already sits 2.5 cm below the ground
  (`start anchor lowest sole -2.5 cm`), and the Synty height ratio moved from 0.8763 (J1, com.sa.unity 8f5f8ba) to 0.9043
  with com.sa.unity 0.3.6's avatar T-pose calibration; the bridge adds under 1 cm. The gate was not loosened; deciding
  whether it should be measured relative to the start frame, or whether the calibration needs a ground correction, is open.
  Other gates: I1 0.0086 deg, I2 0.0010 deg, G1 0.0001 cm, G2 0.0000 deg, G3 hips Y RMSE 7.14 cm (gate 8). G3 MPJPE (soft)
  14.4 cm and G4 running speed 0.72 m/s are reported-only, as in J1.
- `SA.Inbetween.Timeline.Tests`: 27/27.
- Ghosts without a character (`Walk.saclip` to `Dance.saclip`, no Character): skeleton view renders 3 layers; ghost pixels
  green 3,588 and orange 1,274 with ghosts on, 0 and 0 with them off.

TestingAssets note: its recipes in `Assets/InbetweenDemo` reference the script GUID of the old embedded copy
`Assets/zz_inbetween_pkg` (`65cc5203...`), not the package's `InbetweenRecipe` (`c7bb52c8...`), so they load as null in a
project that uses the released package. The `.saclip` files import fine (same importer GUID).

## Windows, checked statically (no Windows machine here)

`SaInbetween-tools-v0.9.2-win-x64.zip`: 419 entries, one top folder `SaInbetween/` (`cli/` 211 files, `bake/` 202,
`model/` empty, `humanoid-v1.safamily`, `README.md`). `cli/sa-inbetween.exe` and `bake/sa-soma-bake.exe` are PE32+ x86-64
console apphosts, self-contained .NET 10.0.15 (`includedFrameworks`), invariant globalization in both. No scripts
(`.cmd`, `.bat`, `.ps1`, `.sh`), so line endings cannot break anything; text files are LF. Longest entry is 70 characters.
Only `cli/onnxruntime.dll` (MSVCP140, MSVCP140_1, VCRUNTIME140, VCRUNTIME140_1) and `cli/onnxruntime_providers_shared.dll`
(VCRUNTIME140) need the Visual C++ Redistributable 2015-2022 x64, which is not in the zip.

A Windows tester runs:

1. New Universal 3D project in Unity 7000.0.0a12; Git for Windows on the PATH.
2. `docs/install.md` steps 1-3 (four manifest lines, `Get-FileHash` against `SHA256SUMS`, Extract All, move `SaInbetween` next to `Assets`, add the two NVIDIA files).
3. `Edit > Project Settings > SA Inbetween`: 5/5 found, Generator `sa-inbetween.exe`.
4. The Mixamo walk of `docs/designer-guide.md` ("Use your own character and clips"): Generate, Export > Animation Clip, Humanoid clip, FBX, then Preview on character.
5. Report: Generate time, the recipe state, any exit code (`-1073741515` means the Visual C++ Redistributable is missing).

## Not verified here

- A Windows run (static checks only, above).
- The Inspector itself (IMGUI): its buttons were driven through the methods they call, and the previews were rendered offscreen through the same renderers.
- A first-time Package Manager download on a machine with no Git credentials (the repositories are public; this machine has Git configured).
