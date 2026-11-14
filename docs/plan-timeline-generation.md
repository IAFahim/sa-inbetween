# Plan: generation authored and baked through Timeline

Designers lay out a generation in Unity's Timeline window: source clips, the window to fill, the poses to leave and land on, the
character to retarget to and the files to write. **Bake Timeline** turns that layout into the same `.saclip`, `.anim`,
`_Humanoid.anim` and `.fbx` files the Inbetween Bridge makes, and puts the result back on the timeline so it plays.

Branch `feat/timeline-gen`. Package `com.sa.inbetween.sa.timeline` 0.1.3 -> 0.2.0. The right-click Bridge stays as it is.

## Goals

1. A designer builds a generation with clips on tracks and Inspector values only. No code, no recipe asset to make by hand.
2. Each step is its own small clip type: a payload struct (what the designer types) plus a consumer (what the bake does with it).
   A toolbox programmer adds a step by writing those two pieces once; no registration, no edit to existing files.
3. Bake is deterministic: the same timeline gives the same plan bytes, the same recipes and the same output bytes.
4. Every problem is a plain sentence that names the clip (track, name, time), what is wrong and what to do, shown on the clip in the
   Timeline window, in the Inspector and in the bake report.
5. No logic is duplicated: generation runs through `InbetweenRecipe` + `RecipeRunner`, exports through `SaClipExport`,
   `SaClipHumanoidExport` and `SaClipFbxExport`, the tools are the ones in `Edit > Project Settings > SA Inbetween`.

## How tl maps onto this

tl (IAFahim/tl, IAFahim/tl.unity) compiles designer-authored timelines into deterministic execution: designers place clips, a toolbox
programmer writes each clip type once (payload struct + consumer), a bake turns the timeline into canonical data, and a host plays it.
The generation toolchain uses the same split and the same rules:

| tl | Timeline generation |
|---|---|
| Designer places clips on tracks, sets values, loop, next | Same, in Unity's Timeline window, on **SA Inbetween/Generation Track**s |
| Toolbox programmer writes a clip type once: payload struct + consumer | `XPayload` struct + `XClip : GenClip<XPayload>` + `XStep : GenStep<XClip>`; consumers found with `TypeCache`, no catalog |
| One `TlTrack` class carries every clip type | One `GenTrack` accepts every `GenClip` subclass (`[TrackClipType(typeof(GenClip))]`) |
| Ticks: `round(seconds * rate)`, windows half-open `[start, end)`, clips by start, tracks top to bottom, groups flattened, muted skipped | Same rules; rate fixed at 30 ticks/s = the generator's frames |
| Kind `Order`: which rule runs first | Step `Stage`: Source 0, Pose 10, Generate 20, Retarget 30, Export 40, Label 50 |
| Bake validates once, fails with location, invariant and repair | Plan pass: no files touched; faults name clip, rule and fix |
| `game.tlib`: canonical bytes, content digest, manifest sorted by name | `<Timeline>.genplan.json`: canonical JSON in tl's timeline-JSON shape, SHA-256 digest, generations sorted by name |
| Host plays `{timeline, tick}` playbacks; effects live in the game | Bake runs one job per Generate clip; effects (processes, files) live in consumers' `Finish` |
| Overlap on a track without `IBlend` fails the bake | Two Generate clips that overlap fail the plan |
| `Tl Settings` marker: When it ends = Loop / Next | Optional glue: Loop adds the closing generation back to tick 0; Next bakes the chained timeline too |

### What is used from tl, and why not more

tl is the model; it is not a hard dependency of these public packages:

- Generation is an editor-time effect (external processes, asset writes). tl's playback path is unmanaged and Burst-only by
  contract; it has no place for either.
- tl clip payloads are unmanaged structs. A Source step must hold an `AnimationClip`, `.saclip` or pose asset that the designer drags
  in; tl clips cannot (tl.unity README section 13).
- tl.unity writes Timeline `+` menu stubs only for assemblies under `Assets/` or tl packs, not for another package.
- The owner's rule: libraries never reference each other; glue lives in the host (unity-cli-skill field notes, rule 19).
- The task rule: a public package does not take a hard dependency on tl.

So: `SA.Inbetween.Timeline.Tl` is an optional editor assembly that compiles only when `com.tl.unity` is installed
(`versionDefines`). It reads the Tl Settings marker of a generation timeline (Loop, Next). Without tl everything else works.

## Data flow

```
Timeline asset (designer)                      plan (pure, no files)                    bake (effects)
 GenTrack rows of GenClips  ── GenPlanner ──>  GenPlan: sources, poses, jobs, faults ──> per job: InbetweenRecipe in <Timeline>_Generated/
  Source | Generate | Source     (stages in       + canonical JSON + digest                 RecipeRunner (sa-inbetween + sa-soma-bake)
  Start/Goal Pose overrides       tl order)                                                 consumers' Finish: exports, placement, labels
  Retarget / Bake, Export, Label                                                            Generated Animation Track on the same timeline
```

**Time is the wiring.** A Generate clip `[s, e)` makes the motion that fills that window:

- **Start** = the pose at tick `s`: a Start Pose clip covering `s`, else the Source clip covering `s` (pose at that local time),
  else the Source clip that ended last at or before `s` (its end frame). Nothing there is a fault.
- **Goal** = the pose at tick `e`: a Goal Pose clip covering `e`, else the Source clip covering `e`, else the next Source clip starting
  at or after `e` (its first frame).
- **Chained goals**: Goal Pose clips that start strictly inside `(s, e)` are intermediate goals, in time order, up to 4 in all.
- **Duration** of each move = window / moves, which must be 0.6 to 1.93 s (the generator's range).
- Local time in a source follows the Timeline clip: `clipIn + (tick - start) / 30 * timeScale`, held at the clip's end; within half a
  frame of the first or last frame it snaps to Start or End (`BridgePlan.Anchor`), so a layout like the Bridge's gives the Bridge's recipe.

## Clip types (first slice)

| Clip | Stage | Payload | Consumer |
|---|---|---|---|
| **Source** | 0 | Kind (Unity clip, `.saclip`, Pose), Motion / Clip / Pose, Character (optional) | Plan: a source span in ticks |
| **Start Pose** | 10 | From (source at this time, source frame N, pose asset, `.saclip`), Frame, Pose, Clip | Plan: start override at its window |
| **Goal Pose** | 10 | same | Plan: goal override, or an intermediate goal inside a window |
| **Generate** | 20 | Land Exactly, Variant (As set, Shorter, Longer, Other landing), Variations (1-4 alternates) | Plan: a job; Bake: one recipe per variation |
| **Retarget / Bake** | 30 | Character (model or prefab; empty = the track's bound character), Rig (Auto, Humanoid, Generic), Foot IK | Plan: target character and rig of the jobs it covers |
| **Export** | 40 | `.anim`, Humanoid `.anim`, FBX, Place on Timeline | Finish: the export functions above, then the Generated track |
| **Label** | 50 | Label | Finish: `AssetDatabase.SetLabels` on the files; the worked example in the toolbox guide |

A job not covered by an Export clip writes the defaults: `.saclip` and `.anim`, placed on the timeline.

## Baked vs generated at edit time

- **Plan** (on every change, shown on clips and in the track Inspector): reads the timeline, writes nothing.
- **Bake** (button, menu, Timeline window action, or `-executeMethod` for batch): writes
  `<Timeline>_Generated/<Generate clip name>.asset` (the recipe), `.saclip`, `.anim`, `_Humanoid.anim`, `.fbx`,
  `<Timeline>.genplan.json`, and the clip on the timeline's **Generated** Animation Track. A recipe already up to date is not
  generated again (the recipe hash covers every input), so a second bake only exports what is stale.
- Nothing runs at game time: GenClips are empty playables, so a game timeline that carries them plays as before.

## Config surface

Inspector fields on the clips (above), the Generate clip's name (names its files), the Timeline window layout (times),
`Edit > Project Settings > SA Inbetween` (tools folder, as today). No new settings asset.

## Test plan

EditMode (`SA.Inbetween.Timeline.Tests`): tick and anchor rules; start/goal resolution (cover, ended before, starts after, half-open
edges, chained goals); faults with their wording (no start, no goal, too long, too short, overlapping Generate clips, two sources at
one tick, missing character, duplicate names, unknown step); stage and authored order; consumer discovery (every clip type has one
step); plan JSON byte-stable across two runs and under `de-DE`; **Bridge equivalence**: the same two clips laid out as source,
Generate, source give a recipe with the same `RecipeRunner.Hash` as `TimelineBridge.Configure`; bake refuses with the setup advice
when tools are missing and writes nothing; Label step; real bake on Synty when the assets and tools are present (`.saclip` bytes equal
the Bridge recipe's, `.anim` and `_Humanoid.anim` written, clip placed, second bake regenerates nothing).

PlayMode (`SA.Inbetween.Timeline.PlayTests`): a timeline holding GenTracks plays in Play mode without errors, and its Generated
Animation Track moves the bound character.

Run in a fresh URP project on the newest Unity 7 editor, batchmode, under `flock /tmp/sa-unity.lock`, packages as `file:` from the
worktree with `testables`; judged by the NUnit XML counts and `error CS` = 0.

## Risks

| Risk | Handling |
|---|---|
| Timeline `+` menu needs a MonoScript per track class | GenTrack and every GenClip are hand-written files named after the class (tl.unity's stub lesson) |
| Generation is asynchronous (`EditorApplication.update`) | The bake is a queue polled on update, like `TimelineBridge`; tests wait on wall-clock time; a script reload stops it with RecipeRunner's message |
| Generated length differs from the window (whole tokens) | Placed at the window start with its real length; the report says asked vs got |
| Generic `.anim` starts at the origin | Placed as is; root matching across seams is the Bridge's `MatchOffsets`, a follow-up for the Generated track |
| "Model picks" variation needs a token range, RecipeRunner fixes the count from seconds | Not in this slice; needs a recipe field |
| tl renames before 1.0 (tl.unity README 13) | Only the optional glue touches tl types (`TlTimelineSettings`, `TlEnd`) |
| Third-party and NVIDIA data cannot ship | Sample timelines are built by `SA > Inbetween > Create Generation Demo` from the user's own Humanoid character and clips |

## Decisions

1. tl is the model and an optional glue, not a dependency (reasons above).
2. One track class for every step clip type, as `TlTrack`; designers use rows for readability, not for meaning.
3. Ticks at 30/s, the generator's frame rate; tl's project tick rate is not used because the plan never plays on tl's clock.
4. Time is the wiring between steps; no step references another step.
5. One recipe per Generate clip, named after the clip, in `<Timeline>_Generated/`; a rename makes a new recipe.
6. Step clips live in a runtime assembly (Timeline needs a MonoScript) and are empty playables at run time.
7. Samples are built from the user's own assets by a menu, like the existing demo scene.
