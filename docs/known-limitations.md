# Known limitations (0.9.3)

What SA Inbetween does not do, or does differently from what you might expect. Each item says what you see and what
to do about it.

## Rigs and clips

- **Humanoid characters only.** The model generates human motion on one fixed skeleton (SOMA, 77 joints) and maps it
  to your character through Unity's Humanoid avatar. Animals, props, vehicles and creatures with extra limbs are not
  supported. The recipe says so when it cannot find a humanoid skeleton on the character.
- **Generic rigs work only by bone names.** A character imported as Generic (Mixamo's default) is read by matching
  bone names such as `mixamorig:LeftArm`. That works for Mixamo and similar names, but **Export > Humanoid clip** stays
  unavailable and unusual names may not be found. Set the FBX's **Rig > Animation Type** to **Humanoid** and press
  **Apply**; the First Bridge window has a **Set to Humanoid** button for this.
- **Legacy clips are not read.** Switch the FBX to Humanoid or Generic.
- **Fingers and hand ends** hold the last pose before the bridge (up to 0.36 degrees from NVIDIA's reference mid-clip).
  The neck is one bone on the SA rig, so the head can move up to about 2 cm at a seam.
- **Duration is 0.6 to 1.93 s per move**, in steps of 4 frames (about 0.13 s). Longer transitions need more goals.
- **The result is always 30 fps.** A 60 fps source is read at 30 fps; a start frame is counted at the clip's own rate.

## Export and playback

- **Humanoid export and twist.** `Export > Humanoid clip` goes through Unity's avatar, which spreads arm and leg twist
  its own way: bones point the same way as the Generic clip (within 0.6 degrees on Synty), but the roll of the upper
  arm and forearm can differ by up to 35 degrees.
- **Rukhanka plays twist differently from Unity.** Rukhanka treats every avatar twist setting as 1 (each twist muscle
  turns its own bone), while Unity uses the avatar's setting (0.5 by default). On the 1382-clip sweep in com.sa.unity
  (`docs/clip-sweep.md`) SA matches Unity on every clip, and Rukhanka differs from Unity by a median of 25.6 degrees
  (worst 89.9) on the upper arm, forearm, upper leg and lower leg about the bone's own axis. If your game plays clips
  through Rukhanka, set `SharedFamilyAuthoring.Twist` to **PerBone** to match it (1379 of 1382 clips within 1 degree;
  the other 3 are Rukhanka's own resampling and loop-pose differences). This is a Rukhanka limitation, not a bridge bug.
- **Export > FBX** reimports as a Generic take only (the exporter adds a root node, which breaks Humanoid
  reimport). For Humanoid retargeting use the Humanoid `.anim`. On Linux it needs the `libxml2.so.2` link.
- **Preview on character (Play mode)** needs one SA shared-family character in a SubScene. The First Bridge window
  and the Inspector preview do not.
- The Generic `.anim` starts at the origin facing +Z; place it where the previous clip left off.
- **The Humanoid `.anim` keeps its up-and-down motion in the pose** (Root Transform Position Y: Bake Into Pose, based upon
  Original), so a jump lands on the ground when an Animator plays it with root motion off, as the demo does. Sideways motion
  and turning stay root motion: with root motion off the character jumps in place.
- **4 Play in SA runtime** (First Bridge window) needs a Humanoid character and a demo scene with no other SubScene; it adds
  one SubScene with the SA character and leaves the Animator character as it is.
- **Generated track on a Timeline**: clips are lined up on the hips sideways and in facing; each keeps its own height, so a
  small vertical step can show at a seam (Synty jump: 3.6 cm at the top, 6.1 cm at touchdown). Matching needs the timeline's
  director in an open scene; without it the report says the hips were not matched.
- The very first preview frame after the Editor starts can render dark for an instant while shaders load; the next frame is
  lit.

## Workflow

- **A Generate step that takes longer than two minutes is stopped** with a plain message (nothing is written). A very
  slow PC can raise the limit in the Editor preferences key `SA.Inbetween.stepLimitSeconds`.
- **Nothing regenerates by itself.** A recipe or Timeline bridge says *out of date* when its settings or a source
  clip change; press **Generate** (or **Regenerate Inbetween bridge**).
- **Generating stops if Unity reloads scripts** (a script change, or Play mode with domain reload). The recipe then
  says so and nothing is written; press Generate again. Play mode does not start while a recipe is generating.
- **Recipes from before 0.9.3 say *out of date* once** after the upgrade (the source fingerprint now survives moving and
  renaming clips). Press Generate once.
- **Recipes made with the old embedded copy in TestingAssets** (`Assets/zz_inbetween_pkg`) load as *missing script* in a
  project that uses the released package. Make them again with **Assets > Create > SA > Inbetween Bridge**.
- **Undo of Generate and Keep** restores the result clip and the saved `.anim`; it does not bring back the Humanoid
  `.anim` or the FBX, which are only written by their Export buttons.
- **SA.Shape (SH4 physical sync, wardrobe validation) is not part of this playtest.** Its component is hidden from
  Add Component in com.sa.unity 0.3.7; do not use it for the pilot.

## Platforms

- **Windows has not been run by a person.** The tools zip is checked statically and started on GitHub's Windows
  runner (`scripts/windows-smoke.ps1`), but no Generate has run on Windows. A Windows tester follows
  `docs/windows-tester-checklist.md`. The model runtime needs the Microsoft Visual C++ Redistributable 2015-2022 (x64).
- **macOS is not built.**
- **CPU only.** Generation does not use the GPU; a slower CPU takes longer (see `docs/compatibility.md`).

## Model licence

- The motion model `mmm_v6_soma_fp32.onnx` is NVIDIA's MotionBricks model under the **NVIDIA Open Model Agreement**. It
  is not in this repository or in the tools archive; each user obtains it from NVIDIA (MotionBricks
  `assets/onnx/soma/`) and keeps NVIDIA's notices (`NOTICE`). The code here is Apache-2.0.
- NVIDIA's sample motions and test recordings are evaluation-only and are never shipped. `soma_rest.pose` is made from
  them with `tools/make_pose_library.py`, so it is not redistributed either; a technical artist makes it once.
- The engine embeds the 77-entry SOMA skeleton parent table (topology only, no motion or bind data). Whether the NVIDIA
  licence allows that table in source is still to be confirmed by the owner (`docs/status.md`).
