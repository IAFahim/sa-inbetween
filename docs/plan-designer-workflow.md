# Plan: designer workflow (issue #1)

Mixamo/Synty Humanoid character plus FBX, `.anim` or pose in; a bridge clip out as `.saclip`, `.anim` and FBX.
Research only. Line numbers are from the working trees on 2026-11-09:
sa-inbetween (v0.6.0), sa-motion, com.sa.unity, and ~/TestingAssets (read only). Nothing here has been run in Unity.

## Summary

- **Designers have no tool today that turns their Unity clip into a `.saclip`.** The only `.saclip` writer is
  `sa-soma-bake` (sa-motion `tools/SA.Soma.Bake/Program.cs:46`), and it reads only the generator's own `.saib`.
  The three `.saclip` files in TestingAssets (`Assets/InbetweenDemo/Walk.saclip`, `Dance.saclip`,
  `Walk_to_Dance.saclip`) all came from generated motion.
- **The conversion code already exists, but only in memory.** com.sa.unity
  `Editor/SharedFamilyBaker.cs` samples any `AnimationClip` on a character (`SampleRigClip`, 952-1019) and maps it
  to the 56-bone humanoid-v1 canonical rig (`BakeClip`, 839-918). It then encodes it with
  `SharedClipEncoder.Encode` through `SharedFamilyBake.EncodeCanonicalSamples` (339-397). That byte array *is*
  `.saclip` content, but it goes only into an ECS blob (`AddBlobAsset`, 937) and never reaches a file. It also
  drops root travel (`RootPolicy.None`, 359-361; warning at 856) and maps bones by **name scoring**, not by the
  Humanoid avatar.
- The canonical rig is **56 bones**, with names identical to Unity `HumanBodyBones` plus `Root`. The 27-bone
  figure is the SOMA *source* rig (`SomaRig.SourceCount = 27`, `src/SA.Soma/SomaRig.cs:10`). Going from
  `HumanBodyBones` to canonical is a name lookup, so no table needs to be written. A Mixamo-specific table exists
  only in a test (`tests/SA.PilotSweep.Tests/RealCorpusSweep.cs:923-974`).
- **Export is feasible with built-in pieces.** `AnimationUtility.SetEditorCurves` and `HumanPoseHandler` are in
  the 7000.0.0a12 managed DLLs. `com.unity.formats.fbx` 5.1.6 and `com.autodesk.fbx` 5.1.3 (with the Linux
  `UnityFbxSdkNative.so`) ship inside the editor
  (`/home/os/Unity/Hub/Editor/7000.0.0a12/Editor/Data/Resources/PackageManager/Editor/`). Neither package is in
  the TestingAssets manifest.
- **Two latent bugs matter for imported clips:**
  1. The `e-3f` / `s+1f` tokens in `RecipeRunner.Times` (`RecipeRunner.cs:228-229`) count the *clip's own*
     sample step (`Program.cs:75,144`). That step is 1/30 s only for generated clips.
  2. `sa-soma-bake pose` drops the root track (`Program.cs:104` calls `ToSoma` without `saRoot`). Every context
     pose therefore sits at hips x = z = 0 in its own heading, so the generator gets no horizontal velocity or
     yaw rate from the start clip.
- **Recommended order:**
  1. Public bake API in com.sa.unity (Humanoid-avatar mapping, root track).
  2. Unity-clip and frame-number sources plus seconds-only times.
  3. Root-preserving pose extraction.
  4. Generic `.anim` export.
  5. The real jump quality test (milestone M1).
  6. Then pose capture, Humanoid `.anim`, FBX, UI polish.

## 1. Producing a .saclip from FBX, .anim or a Humanoid AnimationClip

### What exists

| Path | Where | Input | Output | Designer-usable? |
|---|---|---|---|---|
| `sa-soma-bake <family> <in.saib> <out.saclip>` | sa-motion `tools/SA.Soma.Bake/Program.cs:17-48` | `.saib` v1, 77 SOMA joints, Y-up (`ReadMotion`, 196-235) | `.saclip`, `RootPolicy.Planar` with contacts (`SomaClip.Encode`, `src/SA.Soma/SomaConvert.cs:102`) | Only for generated motion |
| `SharedFamilyBaker` (ECS `Baker<SharedFamilyAuthoring>`) | com.sa.unity `Editor/SharedFamilyBaker.cs:511-1067` | Character with `RigAuthoring` plus `AnimationClip`s (FBX takes, `.anim`, Humanoid or Generic) | In-memory `SharedClipData` blob holding encoded bytes (924-937); no file | No: subscene bake only, and nothing is saved |
| `Tools/SA/Animation Library/Extract Canonical Samples` | com.sa.unity `Editor/CanonicalBakeExtract.cs:50-126` | Selected FBX or `.anim` plus avatar | Per-clip **rig-local JSON** to `Temp/sa/c08/*.json` (476-479), not canonical and not encoded | No: corpus extraction for the C08 gate |
| C08 corpus sweep | sa-motion `tests/SA.PilotSweep.Tests/RealCorpusSweep.cs:17,617` | That JSON (`/home/os/TestingAssets/Temp/sa/c08`, which is absent on this machine) | Encodes in a test; hard-coded Bip01 and Mixamo name tables (870-974) | No: test only |
| `.saclip` importer | sa-inbetween `unity/com.sa.inbetween.sa/Editor/SaClipImporter.cs:42-52` | `.saclip` bytes | `SaClipAsset` (raw bytes) | Yes, but only for existing files |

### How `SharedFamilyBaker` converts a clip today

| Step | Where | What it does |
|---|---|---|
| Skeleton | `RigBuilder.Skeleton` (585) | Builds the skeleton |
| Calibration | 695-705 | Rest locals of the scene/prefab transforms. This assumes the rig's rest pose is the canonical T-pose: `RetargetTable.Compile` "assumes source and target share one anatomical pose" (sa-motion `docs/soma.md`, "Where the existing primitives do not fit") |
| Mapping | `SolveMap` (768-795) | `ScoreBoneName` token scorer (172-311) plus `MappingAssignment.Solve` and topology pruning (797-829). The Humanoid avatar is used only to find the hips (`FindHips`, 1044-1060) |
| Sampling | `SampleRigClip` (952-1019) | `clip.SampleAnimation` on a hidden copy at `min(clip.frameRate, 60)` Hz (957). This works for Humanoid clips, because Unity applies the muscle clip through the copy's Animator and avatar |
| Target to canonical | 861-917 | Inverse of the retarget rows. The hips position is rescaled by `HeightRatio = hips rest Y` (750). Unmapped bones keep the canonical reference |
| Encode | `EncodeCanonicalSamples` | `RootPolicy.None`, identity roots, no contacts, readback verify at 3 frames within 0.01 rad / 0.01 m (373-394) |
| Family id | `PublishedFamilyId` (22) | `90cb280f…d099`, equal to `families/humanoid-v1.safamily` (sa-motion `docs/shared-family-contract.md:86`), so the bytes load in `sa-soma-bake pose` |

### Rig assumptions and gaps

- **Mapping is by name, not by avatar.** Synty names its upper arm `Shoulder_L` and its clavicle `Clavicle_L`
  (`A_Jump_Idle_Masc.fbx.meta`: `Clavicle_L -> LeftShoulder`, `Shoulder_L -> LeftUpperArm`). The scorer
  handles this with special aliases (270-271).
- **Mixamo is at risk under name scoring** (INFERRED, not run). In `mixamorig:LeftArm`, `LeftForeArm`,
  `LeftUpLeg` and `LeftLeg`, "Leg" is the shin. The L4 note in sa-motion `docs/soma.md` ("the name scorer
  confuses SOMA 'LeftLeg' with the thigh") is the same failure.
- **Calibration** uses rest transforms. It is wrong for A-pose binds, and for any rig whose prefab rest is not
  the avatar T-pose.
- **No root track.** Travel is dropped and the clip plays in place.
- **No file output**, and the API is not public: the baker class is `internal` (511), and
  `SA.Inbetween.Clips.Editor` does not reference `SA.Editor`.
- **No contacts.** The generator does not need them (contexts are poses), so this is acceptable.

### TestingAssets setup (read only)

- **Characters.** `Assets/Synty/AnimationBaseLocomotion/Meshes/PolygonSyntyCharacter.fbx` and
  `SidekickSyntyCharacter.fbx`: Humanoid, `animationType: 3`, `avatarSetup: 1` (own avatar).
- **Clips.** `Animations/Polygon|Sidekick/{Masculine,Feminine,Neutral}/...`: Humanoid with the avatar copied
  from another model (`avatarSetup: 2`, `lastHumanDescriptionAvatarSource` guid `bfa42ba6…`), several takes per
  FBX (for example `A_Jump_Idle_Masc.fbx` has `A_Jump_Idle_Masc` 19-32 and `A_Land_Idle_Masc` 39-67).
- **`.saclip` sources.** Only `Assets/InbetweenDemo/*.saclip`, made by sa-inbetween plus `sa-soma-bake`.
  `Walk_to_Dance.asset` is an `InbetweenRecipe`: Start `Where: 0` (End), Goal `Where: 1` (Start).
- **`Assets/ShapeWalk`** is a shape demo (`Masc.sashape`, menu `Tools/SA/Shape Workshop/*`). It produces no clips.
- **Manifest packages.** com.sa.unity @1aa591e and com.sa.motion.unity @056f8c4, Entities 6.7.0 and Timeline
  6.7.0. There is no FBX exporter and no Animation Rigging.

### What is missing (to build)

A public editor API, `FamilyClips`, in com.sa.unity: Unity `AnimationClip` + character → canonical samples +
planar root → `.saclip` bytes. It maps through `Animator.GetBoneTransform(HumanBodyBones)` and calibrates from
the avatar T-pose. Plus a recipe source kind that uses it (W1, W3).

## 2. SA side: canonical humanoid, family, SOMA retargets

### Canonical humanoid-v1

`src/SA.Animation/Retarget/CanonicalHumanoid.cs`:
- 56 bones, Y-up, +Z forward, left side at -X.
- T-pose with identity local rotations, hips at 1.0 m (`Position`, 39-62).
- Parents are at 25-36.
- 15 required roles: `SharedFamilyBaker.cs:44` = Hips, Spine, Head, both arm chains, both leg chains to the foot.
- The names (`SharedFamilyBaker.cs:25-42`) are exactly the `HumanBodyBones` enum names, plus `Root` at index 0.

### SOMA retargets

| Piece | What it does |
|---|---|
| `SomaRetarget.Create` (`src/SA.Soma/SomaRetarget.cs:23`) | Compiles SOMA (30 model joints / 77 skeleton, 27-joint source rig) to canonical with a fixed table (`docs/soma.md`, "Fixed table"): A-pose calibration, mirror, height ratio 0.9922, hip reference 0.9864 m |
| `SomaUnretarget.ToSoma` (`src/SA.Soma/SomaUnretarget.cs:9-12`) | Inverse, round trip at 0.00002 deg |
| What is lost | The Neck1/Neck2 split (up to 21.5 mm head position), non-rest bone translations, and fingers (`Static` rows) |

### What a Mixamo skeleton needs

- **No SOMA table.** Mixamo never meets SOMA directly. The chain is
  Mixamo rig → canonical (Unity side, per-character `RetargetTable`) → SOMA (`SomaUnretarget`, inside `sa-soma-bake pose`).
- **The Mixamo leg is a per-character `RetargetTable`,** compiled exactly like `SharedFamilyBaker.PlanFamily`
  (658-766) does it. It needs:
  - **Bone map** from `HumanBodyBones`: for each canonical c in 1..55,
    `Enum.TryParse<HumanBodyBones>(CanonicalNames[c])`, then `animator.GetBoneTransform(...)`. The 55 names match
    one to one, so no Mixamo-specific table is needed. The Mixamo strings in
    `RealCorpusSweep.cs:923-974` can serve as the expected answer in a test.
  - **Calibration** from the avatar's T-pose (`avatar.humanDescription.skeleton`, the "Enforce T-Pose" result)
    for Humanoid avatars, with prefab rest as the fallback. Mixamo binds are T-pose, so both agree for Mixamo.
    Synty must be checked.
- **No existing `HumanBodyBones` → canonical mapping in code.** `HumanBodyBones` appears only in
  `FindHips` (`SharedFamilyBaker.cs:1048`) and in the sa-motion IK, reactions and ragdoll editors.

## 3. Export: .saclip → Unity AnimationClip (.anim) and FBX

### Available in 7000.0.0a12 (checked by string presence in the managed DLLs)

| API or package | Where |
|---|---|
| `HumanPoseHandler`, `GetHumanPose`, `SetHumanPose`, `HumanTrait.MuscleName` | `Editor/Data/Managed/UnityEngine/UnityEngine.AnimationModule.dll` |
| `AnimationUtility.SetEditorCurve(s)`, `GameObjectRecorder` | `UnityEditor.CoreModule.dll` |
| `com.unity.formats.fbx-5.1.6.tgz`, depends on `com.autodesk.fbx 5.1.3` and `com.unity.timeline 1.7.1` (the project has 6.7.0) | Editor package cache |
| `com.autodesk.fbx` Linux native plugin | `package/Editor/Plugins/UnityFbxSdkNative.so` |
| Public export API | `ModelExporter.ExportObject(string, Object, ExportModelOptions)` (`FbxExporter.cs:4860`), with `ExportModelOptions.ExportFormat` and `ModelAnimIncludeOption` (`ExportModelSettings.cs:479-538`) |

How the FBX exporter collects clips:
- It takes clips from an `Animation` or `Animator` on the object (`AnimationUtility.GetAnimationClips`, `FbxExporter.cs:3561`).
- It exports **transform curves** (`GetCurveBindings`, 2454). Humanoid muscle clips need a Generic transform clip first.

### Recommended approach, simplest first

1. **Generic `.anim` on the designer's character (exact; do this first).**
   - Sample the `.saclip` at 30 Hz.
   - Map canonical → character with `RetargetTable.Apply` (sa-motion `src/SA.Animation/Retarget`, 116),
     using the same compiled table as for import.
   - Write `localRotation` curves for every mapped bone, plus `localPosition` for the hips (and the top bone if
     travel is wanted), with `AnimationUtility.SetEditorCurves`. Keys at every frame, compression off. Save with
     `AssetDatabase.CreateAsset`.
   - Limits: it plays only on that rig, or rigs with identical paths. Fingers and unmapped bones need a policy
     (see Risks).
2. **FBX = the Generic clip exported by the FBX exporter.**
   - Instantiate the character, attach an `Animator` with a temporary `AnimatorController` holding the clip, and
     call `ModelExporter.ExportObject(path, instance, new ExportModelOptions { ExportFormat = Binary, ModelAnimIncludeOption = Include.Anim })`.
   - Make it an optional dependency: asmdef `versionDefines` on `com.unity.formats.fbx` → `SA_FBX`. The menu item
     says "install FBX Exporter (built in)" when the define is missing.
   - On reimport, the designer sets Rig = Humanoid, Copy From Other Avatar, and gets a retargetable clip by the
     normal Unity route.
3. **Humanoid `.anim` (lossy; do it after 1 and 2).**
   - Pose a hidden copy per frame and call `HumanPoseHandler.GetHumanPose`.
   - Write `RootT.*`, `RootQ.*` and the muscle curves.
   - Attribute names: `HumanTrait.MuscleName` gives body muscles directly. Finger muscles in clips use the
     `LeftHand.Thumb.1 Stretched` form, which needs a rename table.
   - Limits: muscle ranges clamp, twist is distributed by the avatar (`armTwist: 0.5` in the Synty meta), and
     non-human bones are lost.

Recommended default: **Generic `.anim` always, FBX when the package is present, Humanoid as a checkbox.**

## 4. Inputs in the recipe and how they reach the generator

### Today

- `InbetweenRecipe` (`Runtime/InbetweenRecipe.cs:7-34`): `ClipFrame { SaClipAsset Clip; FrameWhere {End, Start, Time}; float Seconds }`.
- `RecipeRunner.AddPoses` (`Editor/RecipeRunner.cs:215-222`) runs one `sa-soma-bake pose` per frame source
  (`StudioCommands.TryPoses`, `StudioCommands.cs:115-127`). Each run writes four `.pose` files
  (539 float32 = 77 × 7; `Program.cs:80-81`, CLI `PoseFloats = 539`, `apps/SA.Inbetween.Cli/Program.cs:17`).
- `RecipeRunner.Times` (224-239) builds the four times:

| Mode | Role | Times |
|---|---|---|
| End | start or goal | `e-3f,e-2f,e-1f,e` |
| Start | start | `s,s,s,s` (frozen context, zero velocity) |
| Start | goal | `s,s+1f,s+2f,s+3f` |
| Time t | start | `t-3/30 .. t` |
| Time t | goal | `t .. t+3/30` |

- Contexts go to `generate --context a --context b ...` (four poses); each goal goes to `--target a,b,c,d`
  (`StudioCommands.cs:85-87`; CLI help, `apps/SA.Inbetween.Cli/Program.cs:61-63`). The generator runs at 30 fps
  (`StudioCommands.FrameRate`, 37), between 0.6 and 1.93 s (`InbetweenRecipe.cs:24-25`).

### Defects for new sources

1. **Frame tokens.** `s+Nf` and `e-Nf` use `clip.Duration / (SampleCount - 1)` (`Program.cs:75,141-145`).
   For a `.saclip` baked at 24 or 60 Hz, the context would not be spaced at 1/30 s.
   Fix: `Times` always emits seconds (end = `Duration - k/30`), and imported sources are baked at exactly 30 Hz.
2. **Root dropped.** `ExtractPose` calls `SomaUnretarget.ToSoma(retarget, sa, soma)` (`Program.cs:104`).
   `saRoot` is identity, so the hips are at x = z = 0 and in the pose's own heading (`docs/soma.md`, "What is lost").
   The four context poses therefore carry no horizontal travel or turn. The vertical hips motion survives,
   which matters most for a jump.
   Fix: an option to express all requested times relative to the root of the **last** time, using
   `clip.Root[]` (`SharedClip.Root`, `SharedClip.cs:32`) and the `ToSoma(..., saRoot, ...)` overload. Measure
   before making it the default.

### Proposed sources (one `ClipFrame` union, backward compatible)

```csharp
public enum SourceKind { SaClip, UnityClip, Pose }      // new field; default SaClip keeps old assets valid
public enum FrameWhere { End, Start, Time, Frame }      // Frame appended so serialized ints are stable
public struct ClipFrame {
    public SourceKind Kind; public SaClipAsset Clip; public FrameWhere Where; public float Seconds;
    public int Frame;                 // 0-based, at the source clip's own frame rate
    public AnimationClip Motion;      // UnityClip: FBX take or .anim (Humanoid or Generic)
    public GameObject Character;      // UnityClip/capture: prefab or model with Animator (+ Humanoid avatar)
    public SaPoseAsset Pose;          // Pose: imported .pose (539 floats)
}
```

How each source flows (all end in four `.pose` files, so `generate` is unchanged):

- **UnityClip.** `FamilyClips.Bake(Character, Motion, 30 Hz)` writes
  `Library/SaInbetween/<recipe guid>/src_<hash>.saclip`, keyed by clip, character and
  com.sa.unity version. Then the existing `pose` step runs.
  Frame N becomes `t = N / Motion.frameRate`, clip-local, as the Animation window shows it.
- **SaClip + Frame.** `t = N × Duration / (SampleCount - 1)`.
- **Pose.** The file is copied four times: `s,s,s,s` semantics for a start, `a,a,a,a` for a target. No process runs.
- **Capture pose (menu or button).**
  1. The selected scene object with an Animator is read with `FamilyClips.Capture` (target → canonical, the same
     math as `SharedFamilyBaker.cs:861-917`).
  2. It is written as a 2-frame static `.saclip` asset `<name>_pose.saclip` next to the recipe, which the designer
     can see and reuse.
  3. The recipe source becomes `SaClip` with Where = Start.
  This needs no new format and no SA.Soma inside Unity: `com.sa.motion.unity/Runtime/Plugins` ships `SA.Motion`,
  `SA.Animation`, `SA.Animation.Content` and `SA.Shape`, not `SA.Soma`.
- **"Frame 60 of this jump clip" as a start.** The contexts are `t60 - 3/30, -2/30, -1/30, t60` from the baked
  source, so the generator sees the real mid-air velocity (vertical now; horizontal after the root fix).
- **Hash.** `RecipeRunner.Hash` (93-110) must add `Kind`, `Frame`, and the content hashes of the `Motion`
  asset, `Character` asset and `Pose` file, or the recipe will not go out of date when they change.

## 5. Quality risk on Mixamo-like bodies and the early test

### The chain

Character rig → canonical (per-character table) → SOMA (fixed table) → generator → SOMA → canonical
(`sa-soma-bake`) → character rig (export).

Losses, measured where already known:

| Loss | Size | Where measured |
|---|---|---|
| SOMA ↔ canonical | Exact except Neck1/Neck2 split (≤ 21.5 mm head), fingers flat, non-rest translations (≤ 5 mm) | sa-motion `docs/soma.md` |
| Twist on arm, forearm and hand | Unmeasured | sa-inbetween `docs/status.md:21` |
| Character ↔ canonical | Unmeasured for Synty and Mixamo; calibration mismatch shows as a constant per-bone offset | — |

### Jump data on this machine (none are in a git repo; never commit them)

| Asset | Rig | Use |
|---|---|---|
| `~/TestingAssets/Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/InAir/A_Jump_Walking_RootMotionVertical_Masc.fbx`: takes 51-63, 30-45, full 1-93 | Synty, Humanoid (copied avatar) | **Primary: full jump, includes "frame 60"** |
| `.../InAir/A_Jump_Idle_RootMotion_Masc.fbx` (19-32 jump, 39-67 land, full 1-67) | Synty Humanoid | Standing jump; the land take is a goal source |
| `.../InAir/A_Jump_Running_RootMotionVertical_Masc.fbx` (full 1-72), `A_Jump_Sprinting_RootMotionVertical_Masc.fbx` (full 1-82) | Synty Humanoid | Horizontal-velocity cases (tests the root fix) |
| `.../InAir/A_InAir_FallShort_Masc.fbx` (1-30), `A_InAir_FallLarge_Masc.fbx` (1-42); `A_Land_IdleSoft/Medium/Hard_Masc.fbx` | Synty Humanoid | Mid-air start and landing goals |
| Femn, Neutral and `Sidekick/*/InAir/A_MOD_BL_Jump_*` | Synty Humanoid | Proportion variety |
| `~/TestingAssets/Assets/Samples/Rukhanka Animation System 2/2.10.0/.../Ellen/AnimationClips/@EllenJump.fbx` (EllenJumpTakeOff 1-19, GoesUp 42-61, ...) | Ellen | Second style |
| `/home/os/ee2-corpus/Packages/com.agni.neophyte/Neophyte_RIG.fbx` (+ prefab) | **Real `mixamorig`**, Humanoid (`animationType: 3`) | Mixamo body. Its clips are Frisbee Throw, Stabbing and Sword Slash: **no Mixamo jump exists on disk** |
| `/home/os/sa-demo-sh5/Assets/Synty/...` | Same Synty set | Duplicate |
| MotionBricks library "Jump 0.5 m" (`tools/make_pose_library.py:28`) | SOMA native | Control: generator quality with no rig retarget |

### Early test J1 (run in TestingAssets, EditMode, `[Category("RealAssets")]`, skipped when assets are missing)

Setup:
- Character: `PolygonSyntyCharacter`, then Neophyte (copied locally; Synty clips play on it through Humanoid retarget).
- Source: `A_Jump_Walking_RootMotionVertical_Masc_Full`.
- Start = Frame 60, and separately the automatically detected apex frame (argmax hips Y).
- Goal = `A_Land_IdleSoft_Masc` frame 0.
- Duration = the source's own time from N to its touchdown frame, clamped to at least 0.6 s.
- Ground truth: the source's own frames N..N+D on the same character.

Gates (measured on body joints, excluding fingers, Neck1/Neck2 and the jaw and eyes):

| # | Measure | Gate |
|---|---|---|
| I1 | Import round trip: Unity clip → canonical → `RetargetTable.Apply` → same character vs direct sample, all frames | ≤ 1.0 deg world rotation; ≤ 1.0 cm hand and foot position |
| I2 | Context fidelity: `.pose` of frame N, mapped back to the character | ≤ 2.0 deg bone direction; hips height within 1.0 cm |
| I3 | Mapping: `HumanBodyBones` map assigns all 15 required roles on Synty and Neophyte; the Neophyte map equals the `RealCorpusSweep.cs:923-974` table | 0 differences |
| G1 | Seam continuity, output frame 0 vs source frame N | ≤ 3 cm joint position jump; hips velocity difference ≤ 0.6 m/s |
| G2 | End: last frame vs goal (LandExactly) | ≤ 1.0 deg |
| G3 | Plausibility vs ground truth | Hips Y trajectory RMSE ≤ 8 cm; touchdown frame (first foot below 3 cm) within ±3 frames; foot penetration ≤ 3 cm. Report the mean per-joint position error (MPJPE) as a soft gate ≤ 12 cm |
| G4 | Root fix effect (Running and Sprinting sources) | Horizontal hips velocity at the seam within 0.5 m/s of the source with the fix; report without |
| E1 | Export: Generic `.anim` played on the character vs `RetargetTable.Apply` of the `.saclip` | ≤ 0.1 deg; ≤ 1 mm |
| E2 | Humanoid `.anim` vs Generic | ≤ 3 deg body (report per bone) |
| E3 | FBX reimport (Humanoid, same avatar) vs Generic | ≤ 0.5 deg |

Report: JSON to `Library/SaInbetween/reports/j1.json`; the numbers alone go into `docs/reports/`. Run all gates
on Synty, then Neophyte, then on the MotionBricks control (G1-G3 only), to separate retarget loss from generator
quality.

## Work breakdown (dependency order)

Sizes: S ≤ 1 day, M 2-4 days, L > 1 week.

**W1. Public `FamilyClips` editor API with avatar mapping and root (com.sa.unity, M)**
- Goal: one public static editor class:
  - `TryPlan(GameObject character, out FamilyRigPlan, out string error)`: `HumanBodyBones` map when
    `avatar.isHuman`, else the existing name scorer; calibration from `humanDescription.skeleton`, else rest.
  - `TrySample(plan, AnimationClip, float hz, out Pose[] canonical, out RigidPose[] root, out int frames)`:
    planar root from the hips ground projection and leg-line heading, the same convention as `SomaClip.Convert`.
  - `TryEncode(..., RootPolicy.Planar)` returning `.saclip` bytes.
  - `TryCapture(plan, out Pose[] canonical)`.
  - `TryApply(plan, canonical, root, out target locals)`.
- Leave `SharedFamilyBaker` behaviour unchanged. It may call the shared helpers but keeps the name map until I3
  shows the avatar map is equal or better.
- Files:
  - New `Editor/FamilyClips.cs`.
  - Refactor helpers out of `Editor/SharedFamilyBaker.cs:658-1019`.
  - Extend `EncodeCanonicalSamples` (339) with root and policy parameters.
  - Tests in `Tests/Editor/`.
- Verification:
  - Synthetic rig test: a generated T-pose skeleton with Mixamo names; I1 ≤ 0.01 deg.
  - Then I1 and I3 on Synty and Neophyte in TestingAssets.

**W2. Seconds-only times and frame sources (sa-inbetween, S)**
- Goal: `FrameWhere.Frame` plus `int Frame`. `Times()` emits seconds for every mode. `PreviewMath.FrameSeconds`
  (`RecipePreviewScene.cs:17`) and the inspector show "frame N of M (t s)".
- Files: `Runtime/InbetweenRecipe.cs`, `Editor/RecipeRunner.cs:224-239`, `Editor/InbetweenRecipeEditor.cs:178-190`,
  `Tests/Editor/RecipeTests.cs:10-22` (update the expected strings).
- Verification: unit tests. Regenerate `InbetweenDemo/Walk_to_Dance` and check that the output bytes equal those
  from v0.6.0. All demo clips are 30 Hz, so seconds and frame tokens agree.

**W3. Unity clip source (sa-inbetween, M; needs W1, W2)**
- Goal: `SourceKind.UnityClip`. Before the pose steps, `RecipeRunner` bakes each Unity source in process through
  `FamilyClips` at 30 Hz into a `Library` cache. Add the menu `Assets/SA/Convert to .saclip` for designers who
  want the asset.
- Files:
  - `Runtime/InbetweenRecipe.cs`.
  - `Editor/RecipeRunner.cs` (`AddPoses`, `Problem`, `Hash`).
  - `Editor/SA.Inbetween.Clips.Editor.asmdef`: add `SA.Editor`, `SA.Runtime`, and the precompiled
    `SA.Motion.dll`, `SA.Animation.dll`, `SA.Animation.Content.dll`.
  - `package.json`: com.sa.unity version bump.
  - `Editor/RecipeVariations.cs` (shares `PrepareSources`).
- Verification: J1 steps I1 and I2 on `A_Jump_Walking_RootMotionVertical_Masc_Full` frame 60.

**W4. Root-preserving context extraction (sa-motion, S-M; parallel with W3)**
- Goal: an option on `sa-soma-bake pose`, for example `--root last` (a trailing flag; the positional form stays
  valid). It samples `clip.Root` at each time and passes `inv(root(t_last)) * root(t)` as `saRoot`, so the four
  poses share one frame.
- Files: `tools/SA.Soma.Bake/Program.cs:8-9,63-118`; `docs/soma.md`, section "Extracting a pose from a clip";
  tests in `tests/SA.Soma.Tests`. In sa-inbetween, `StudioCommands.TryPoses` (115-127) passes the flag for
  contexts only.
- Verification:
  - On the req1/req2 eval clips (`testdata/eval-only`), the hips displacement between the four extracted context
    poses equals the original SOMA displacement within 1 mm.
  - The existing "0.0 deg reproduction" check still passes.
  - J1 G4.

**W5. Generic `.anim` export (sa-inbetween, M; needs W1)**
- Goal: an **Export → Animation Clip** button on the recipe result, plus a context menu on `SaClipAsset`.
  - Needs a character (recipe `Character`, or a picker).
  - Bakes at 30 Hz through `FamilyClips.TryApply`.
  - Fingers and unmapped bones take the start source frame's local rotations (held), not the canonical flat hand.
- Files: new `Editor/SaClipExport.cs`; `Editor/InbetweenRecipeEditor.cs` (result section near 219); tests.
- Verification: E1 on the J1 output; the designer plays the clip in the Animation window on `PolygonSyntyCharacter`.

**W6. Real-data quality harness J1 (TestingAssets-local test plus sa-inbetween test code, M; needs W3-W5)**
- Goal: an automated J1 (I1-I3, G1-G4, E1) with a JSON report; a skip, not a fail, when assets are missing.
- Files: `unity/com.sa.inbetween.sa/Tests/Editor/RealJumpTests.cs` (asset paths from an env var or
  `EditorPrefs`); `docs/reports/j1-<date>.md` with the numbers only.
- Verification: the run itself. Gates as listed. Any failure goes into Risks before UI work continues.

**W7. Pose assets and capture (sa-inbetween, M; needs W1)**
- Goal:
  - `.pose` `ScriptedImporter` → `SaPoseAsset` (539 floats; validated finite and unit quaternions with the CLI's
    tolerance 1e-3).
  - `SourceKind.Pose`.
  - `Tools/SA/Inbetween/Capture Pose From Selection`, writing a 2-frame `.saclip`.
- Files: new `Editor/SaPoseImporter.cs`, `Runtime/SaPoseAsset.cs`, `Editor/PoseCapture.cs`; `RecipeRunner.AddPoses`.
- Verification: pose the Synty character by hand to frame 60 of the jump, capture it, and compare the capture's
  `.pose` with the W3 frame-60 `.pose`: ≤ 0.5 deg body joints.

**W8. FBX export (sa-inbetween, S-M; needs W5)**
- Goal: an **Export → FBX** button through `ModelExporter.ExportObject`, behind `SA_FBX`. Without the package,
  the inspector tells the designer to install FBX Exporter 5.1.6 (built in).
- Files: `Editor/SaClipFbxExport.cs`; asmdef `versionDefines`.
- Verification: E3 in TestingAssets with the package added locally.

**W9. Humanoid `.anim` export (sa-inbetween, M; needs W5)**
- Goal: `HumanPoseHandler` muscle export with `RootT` and `RootQ`; finger muscle attribute rename table.
- Files: `Editor/SaClipExport.cs`.
- Verification: E2 on Synty; then play the Humanoid clip on Neophyte (cross-character) and inspect it visually.

**W10. Recipe UI pass (sa-inbetween, S; needs W2, W3, W7)**
- Goal: source-kind dropdown; character slot; frame field with a scrub preview of the real character pose
  instead of the skeleton; Export section. Update `docs/designer-guide.md:69` ("planned" items).
- Verification: `PreviewTests` and `RecipeTests`; a manual walkthrough of milestone M1.

**W11. Later (out of scope): Timeline bridge track (issue item 7).**
- Uses W3 sources: adjacent Timeline clips become Start (End of A) and Goal (Start of B).

## Risks and open decisions (each with a recommended default)

| # | Risk or decision | Default |
|---|---|---|
| R1 | Name scorer mis-maps Mixamo `LeftArm`/`LeftForeArm`/`LeftLeg` | Use the Humanoid avatar map in `FamilyClips`; scorer only for Generic rigs. Keep `SharedFamilyBaker` unchanged until I3 passes, then switch it in a separate com.sa.unity release |
| R2 | Calibration: prefab rest is not the canonical T-pose (A-pose rigs, Synty unknown) | Avatar `humanDescription.skeleton`. Log the angle difference against rest per bone; warn above 5 deg |
| R3 | Twist on arm, forearm and hand unverified (`status.md:21`) | Gate I1 measures world rotation, which includes twist; E1 is visual on the skinned Synty character. Fix in `SomaCalibration` secondaries only if the error is the constant-roll kind |
| R4 | Fingers: generated `.saclip` fingers are flat (SOMA fingers `Static`) | On export, hold the start source frame's finger locals; checkbox "Flat hands" for the canonical option |
| R5 | Context loses horizontal velocity and yaw (root dropped) | Ship W4 with the flag; make it default for UnityClip sources once G4 shows it helps; keep the old behaviour for `.saclip` sources until the demo is re-verified |
| R6 | Remaining air time below 0.6 s (generator minimum, `InbetweenRecipe.cs:24`) | Clamp to 0.6 s. Inspector warns "the goal is reached later than in the source". Suggest a later goal frame |
| R7 | Stylized game jumps (long hang time) are out of the MotionBricks distribution | Measure with G3 against the MotionBricks control. If G3 fails only on Synty, document it and offer "LandExactly" plus a longer duration; no model work |
| R8 | Clip frame rate is not 30 Hz | Always bake sources at 30 Hz and use seconds only (W2). Frame N is converted with the source's own `frameRate` |
| R9 | Root alignment of the exported bridge: it starts at the origin, facing the context heading | Export relative to the start frame (origin, +Z), and document Timeline "Match Offsets". Option "world space from source frame N" later |
| R10 | Where conversion code lives | com.sa.unity `FamilyClips` (public), referenced by sa-inbetween. No copy in sa-inbetween |
| R11 | Source cache vs visible asset | `Library/SaInbetween/<guid>/src_<hash>.saclip` cache; explicit menu for a visible `.saclip` |
| R12 | Output shape: bridge only vs source plus bridge | Bridge only (Timeline-friendly; matches v0.6.0). "Stitched" later |
| R13 | Export format default | Generic `.anim` always; FBX if the package is present; Humanoid behind a checkbox |
| R14 | FBX dependency | Optional (`versionDefines`). Never a hard dependency; the package's Timeline 1.7.1 requirement is satisfied by 6.7.0 |
| R15 | SA.Soma inside Unity | Not for M1; use the CLI (`sa-soma-bake pose`). Revisit if per-click latency matters (one process per source today) |
| R16 | Licences of test assets (Synty commercial, Mixamo terms, Neophyte package unknown, MotionBricks evaluation only) | Never commit them. Tests skip when the assets are absent; only numbers are committed |
| R17 | Humanoid clip sampling via `SampleAnimation` on a hidden copy also moves the animator root (root motion) | Derive the root from the hips projection (SOMA convention), not from the animator transform, so in-place and RootMotion variants give the same `.saclip` body motion |

## Minimal first milestone M1: the jump example end to end

Scope: W1, W2, W3, W4 (flag on), W5, and W6 gates I1, I2, I3, G1, G2, G3, E1.

Designer steps in TestingAssets:
1. Create an Inbetween recipe.
2. Start: Kind = Unity Clip, Character = `PolygonSyntyCharacter`, Motion = `A_Jump_Walking_RootMotionVertical_Masc_Full`, Frame = 60.
3. Goal: Kind = Unity Clip, Motion = `A_Land_IdleSoft_Masc`, Where = Start.
4. Duration 0.8 s. Generate. Keep one of the 4 variations.
5. **Export → Animation Clip.** `<recipe>.anim` appears next to the recipe and plays on the Synty character in
   the Animation window and in Timeline after the jump clip.

Done when:
- Gates I1-I3, G1-G3 and E1 pass on Synty.
- The same recipe with Character = Neophyte (Mixamo) passes I1, I3 and E1. G1-G3 are reported for it.
- Walk_to_Dance regenerates byte-identical (W2 regression).

Not in M1: FBX, Humanoid `.anim`, pose capture and `.pose` assets, UI polish, Timeline.
