# Play-test readiness (designer pilot)

sa-inbetween **0.9.3** and com.sa.unity **0.3.7**, from "works end to end" (0.9.2 / 0.3.6) to ready for a first designer
pilot. Branch `feat/playtest` in both repositories. Linux, Unity 7000.0.0a12, 2026-11-14.

Read with: `docs/designer-guide.md` ("Your first 10 minutes"), `docs/compatibility.md`, `docs/known-limitations.md`,
`docs/windows-tester-checklist.md`.

## Scope

In: the designer path in `com.sa.inbetween.sa` (demo, recipes, Generate, preview, export, failure messages), the
Timeline bridge package as it is, and the tools archive on Linux and, for Windows, static checks plus a start check in CI.

**Out: SA.Shape (SH4 physical sync, wardrobe validation).** It is a half-built com.sa.unity add-on and not part of
this pilot. com.sa.unity 0.3.7 hides `ShapeAuthoring` from **Add Component**, so a designer setting up a character
cannot add it by accident; existing scenes and code are unchanged. Nothing in sa-inbetween shows Shape UI. Do not use
it in the pilot.

## Decisions

The owner's decisions, carried out:

1. **The G3 foot gate is relative to the source.** In `RealJumpTests` the limit is the deepest sole penetration of the
   source at the start anchor frames (the four context frames, anchor-3 to anchor) + 1 cm, never below 3 cm
   (`FootPenetrationLimitCm`, `PenetrationCm`). Nothing else was loosened. Synty: source frames 57..60 sit 2.53 cm under
   the ground, so the gate is 3.53 cm; the bridge reaches 3.45 cm and passes. Tests cover the limit rule (0, negative,
   2, 2.5 and 4.25 cm) and the measure (deepest over all frames and both feet, scaled by the height ratio).
2. SA.Shape is out of scope (above).
3. Windows cannot be run here: `scripts/windows-smoke.ps1` and a one-page tester checklist, validated statically.
4. The `~/GitHub/sa` workspace is not pushed; its submodule pointers are committed locally.

Decisions I made (senior-dev calls the owner delegated):

5. **"Anchor frames" means the start's four context frames**, not the goal's. Counting the goal frames too would have
   raised the limit to 8.74 cm (goal frame 0, measured against an identity root, is not on the output's ground), far
   looser than the owner meant.
6. **The first-10-minutes path is a menu, not a sample**: `SA > Inbetween > Create Demo Scene` opens a **First Bridge**
   window. A `Samples~` copy would need a character and clips the package cannot ship (NVIDIA data, third-party assets),
   so the demo is built from the user's own Humanoid files. It picks the longest clip named jump, leap or hop whose hips
   rise at least 15 cm (up to 8 tried), starts at its apex frame, and lands on the first frame of a clip named land,
   idle or stand (same folder first, never a jump), in 0.8 s, on a skinned Humanoid character (the clip's own model,
   else the character whose avatar the clip copies, else the first one). Generic model files get a one-click
   **Set to Humanoid**; FBX files exported from recipes are never offered for it.
7. **Preview runs inside the window** (the same offscreen character preview as the Inspector), so it never touches a
   scene. **Save** writes the Generic and Humanoid `.anim`, makes `Inbetween Demo.controller` with the clip looping and
   assigns it to the demo character, so pressing Play shows the result without the ECS setup.
8. **A top-level `SA` menu** holds the designer entry points (Create Demo Scene, First Bridge Window, Copy Diagnostics,
   Report a Bug, Project Settings, Designer Guide). The existing `Tools/SA/...` items stay where they are.
9. **Generate bakes into `Library/SaInbetween/<recipe guid>/`** and copies the finished clip into `Assets` in one step,
   through a hidden `.partial` file. A cancel, a crash or a script reload can no longer leave a half-written clip in
   `Assets`.
10. **A script reload stops a running Generate**: the tools are killed in `beforeAssemblyReload` and the recipe says so
    afterwards. Before, the run was silently lost while the tool kept writing. **Play mode does not start while a
    recipe is generating**: it is cancelled in `ExitingEditMode` with a warning and a Scene view notice.
11. **Generate is one Undo step that includes the files**: ctrl-Z restores the previous `.saclip` and the saved `.anim`
    (or deletes them after a first Generate), and redo brings them back. Undo snapshots live in
    `Library/SaInbetween/undo` (named by content), not in the undo record: 0.9.2's Keep stored the clip bytes in the
    record, and a large `.anim` in a record overflowed Unity's undo stack. Only records that this undo step changed are
    written back, so an undo can no longer bring back files of other recipes.
12. **Renaming or moving a recipe takes its result files along** (the `.saclip` if it is the Output, the `.anim` if it
    is the saved clip, `_Humanoid.anim`, `.fbx`, capture poses), never a source. Duplicating a recipe still writes its
    own files, because results stay named after their recipe.
13. **The source fingerprint survives moving and renaming clips**: GUID and local id plus a content hash (ignoring the
    `m_Name` lines of YAML assets and `timeCreated` in `.meta` files) instead of path and name. Recipes made with 0.9.2
    say *out of date* once.
14. **A recipe named like one of its sources is refused** ("so saving its result would overwrite that file") for the
    `.anim`, `_Humanoid.anim` and `.fbx` it writes. The sweep found this: a recipe named `Long60` next to `Long60.anim`
    overwrote its own source clip on the first Generate.
15. **A frame past the end of a Unity clip is refused, naming the last valid frame**; Legacy clips are refused with the
    fix. A Generic rig without an Animator is allowed, because it converts by bone names.
16. **Diagnostics shorten the home folder to `~`**, so a pasted bug report does not carry the user name. The model's
    SHA-256 is computed once per file size and time.
17. **The Windows smoke test runs in the release workflow** on GitHub's Windows runner (Windows PowerShell 5.1) for every
    tag, with the zip from the release, so "Windows untested" becomes "tools start on Windows, Generate untested". The
    model cannot be used in CI, so steps 6 and 7 skip there. The script ships as a release asset.

## Done

### 1. First 10 minutes

- `DemoScene` (find, apex, Set to Humanoid, scene and recipe, save) and `DemoWindow` (checklist with fix buttons,
  **1 Generate**, **2 Preview**, **3 Save**, Cancel, status message, Copy diagnostics, Report a bug, Designer guide).
- On the test project (Mixamo + Synty) it picked `A_MOD_BL_Jump_Walking_RMV_Masc_Full` frame 41 (hips 0.80 m up) to
  `A_MOD_BL_Land_IdleHard_Masc` on SidekickSyntyCharacter in 2.7 s. Generate, Save (Generic `.anim`, Humanoid `.anim`,
  controller, scene) and the preview (rendered offscreen at 0, 0.42 and 0.83 s: mid-air tuck, arms up, landing pose)
  all worked through the window's own methods. The scene was saved and clean, with the character's root motion off.
- Test `Demo_scene_finds_a_jump_starts_mid_air_and_saves_playable_clips`.

### 2. Failure UX

- `FailureAdvice` gives a plain message and a fix for: a wrong or truncated model (SHA-256 pin), an unreadable model,
  out of memory, a damaged rest pose or family file, missing tool files, the Windows DLL exit code, a launcher that
  will not start, and any other exit (the last `error:` line, then "Copy diagnostics"). Conversion failures say whether
  the rig is not humanoid, not upright or has scaled bones.
- Missing tools: the recipe, Project Settings and the window say which file is missing and where it goes.
- Wrong rig: the yellow note on Generic characters, the conversion advice, and **Set to Humanoid** in the window.
  Unsupported clip: the Legacy and past-the-end checks. FBX native library missing: the 0.9.2 message, unchanged (it
  names libxml2). Stale recipe: the *out of date* message. Deleted result: *Ready, press Generate*.
- **Copy diagnostics** is in the recipe Inspector (next to a failure and always at the bottom), in Project Settings, in
  the First Bridge window and under `SA > Inbetween > Copy Diagnostics`. It copies: Unity, OS, CPU, memory, GPU; package
  versions with Git hashes; the tools folder and every tool file with size and SHA-256, and whether the model matches
  the pin; the FBX package, FBX SDK native library and libxml2; the recipe (sources with length, fps and Humanoid or
  not, state, problem, result); the last error with the last 40 log lines; Editor log lines from SA Inbetween.
- `.github/ISSUE_TEMPLATE/bug-report.md` (steps, expected, actual, frequency, assets, diagnostics block).

### 3. Robustness sweep

Linux, `~/playtest-project` (the clean-install project with Synty and InbetweenDemo copied in), real tools and model.

| Case | Result |
|---|---|
| Undo / redo of a real Generate (Mixamo Generic rig, with `.anim`) | undo: `.saclip` and `.anim` gone, Output and saved clip null, *NotGenerated*; redo: both back, *UpToDate* |
| Domain reload during a real Generate (`EditorUtility.RequestScriptReload` in *Generating the motion*) | after the reload: *Failed* with the reload message, no file in Assets, no tool process left |
| Generate again after that, twice | *UpToDate* both times, identical bytes (SHA-1 `EFA087894FE9`), about 6 s each |
| Cancel during the bake | tool process gone, *Cancelled*, nothing in Assets (test) |
| Play pressed while generating | Play mode not entered, warning logged, Generate finished *UpToDate* |
| Delete the result, then Generate | *NotGenerated*, then generated again (test) |
| Rename a recipe | result `.saclip` and `.anim` follow, Output reference intact (test); sources never moved |
| Move and rename a source clip and the character | recipe stays up to date; editing the clip makes it out of date (test) |
| Clip of 12.27 s at 60 fps (synthetic, from the Synty jump), start at frame 682 (11.37 s) | converted in 0.25 s to 369 frames; Generate *UpToDate* |
| Frame past the end, Legacy clip | refused, naming the last valid frame or the fix (test) |
| Root-motion clips | Synty `RootMotionVertical` and `RootMotion` jumps in `SyntyJumpGates` and the demo |
| Character without a Humanoid avatar (Mixamo copy set to Generic, the default import) | Generate *UpToDate*, 30 frames (12,224 bytes, as the Humanoid version); Humanoid export disabled, as designed. With no avatar and no Animator the clip still converts by bone names (41,692 bytes) |
| Recipe named like its source `.anim` | overwrote the source on Generate; now refused (test) |

Bugs the sweep found, all fixed: the source overwrite (decision 14); an undo stack overflow from clip bytes in undo
records, and undo bringing back other recipes' files (decision 11); the demo scene build losing its recipe reference
when the new scene unloaded unused assets; `GetComponent` returning Unity's fake null in the demo camera setup; a
tools-folder setting leaking from one test into the next (tests now restore it in `TearDown`).

### 4. Docs

`docs/compatibility.md` (versions, Unity, OS, generation time, rigs), `docs/known-limitations.md` (Rukhanka twist,
model licence, non-Humanoid clips, Windows untested, workflow limits), `docs/windows-tester-checklist.md`, the
designer guide's "Your first 10 minutes" and failure table, and install.md for 0.9.3 / 0.3.7.

`scripts/windows-smoke.ps1` was parsed with PowerShell 7.6 (0 errors) and run here against the v0.9.2 Linux tarball
with the model and rest pose: all 7 checks PASS (generate 4.1 s, 26 frames; bake 0.2 s). Against the v0.9.2 Windows
zip, the hash, layout (415 files) and PE32+ x64 checks PASS and the run steps skip off Windows. CI parses it with
PowerShell 7 on both runners and with Windows PowerShell 5.1.

### 5. Tests (fresh Editor)

Copy project `~/playtest-project`, packages as `file:` from the two `feat/playtest` worktrees, `testables`
com.sa.unity, com.sa.inbetween.sa and com.sa.inbetween.sa.timeline. Editor quit and started fresh in batchmode on
Vulkan with the libxml2 link; tools v0.9.2 with the NVIDIA model; then the three suites in a row with no restart:

| Suite | Result |
|---|---|
| `SA.Inbetween.Clips.Tests` | **62/62** passed, 0 skipped (49 in 0.9.2 + 2 G3 rule tests + 11 playtest tests). The 4 real-asset tests ran: `SyntyJumpGates` passes every enforced gate (I1 0.0086 deg, G1 0.0001 cm, G2 0.0000 deg, G3 hips Y RMSE 7.14 cm, touchdown 0 frames, **foot penetration 3.45 cm against 3.53 cm**, source start frames 2.53 cm), `RealExportTests` x2, `RealPoseTests` |
| `SA.Inbetween.Timeline.Tests` | **27/27** |
| `SA.Tests` (com.sa.unity 0.3.7) | **111/111**, 0 skipped (on Vulkan, so the GPU readback tests ran) |

An earlier fresh-Editor run had 61/62: a wrong assertion in a new test (the recipe's empty goal was reported first),
fixed in the test; the other two suites were the same.

## Remaining

- **A Windows run by a person**: `docs/windows-tester-checklist.md`. CI starts the tools on Windows; nobody has
  generated on Windows yet.
- **The IMGUI itself was not clicked**: the First Bridge window and the Inspector were driven through the methods
  their buttons call, and previews were rendered offscreen. A designer's first session is the real check.
- **SA.Shape** stays out of scope (IAFahim/com.sa.unity#4 item 2).
- **The NVIDIA licence question for the 77-entry parent table** (`docs/status.md`) is the owner's call.
- Unity 7000.0.0a16 is installed, but this release was tested on a12 only.
