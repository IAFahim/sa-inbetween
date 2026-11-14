# Timeline generation: the toolbox programmer's guide

Designers lay out a generation on an **SA Inbetween Generation Track** in Unity's Timeline window and press **Bake Timeline**
(`docs/designer-guide.md`, "Author a generation on a Timeline"). This page is for the programmer who writes clip types for them. The
design record is `docs/plan-timeline-generation.md` (issue #2).

The model is tl's (IAFahim/tl): the designer places clips and sets values and never writes code; the toolbox programmer writes each
clip type **once**, as a payload struct plus a consumer; the bake turns the timeline into canonical data and runs the consumers in a
fixed order. Nothing here references tl. With `com.tl.unity` installed, one optional glue assembly reads tl's Tl Settings marker.

## Assemblies

| Assembly | Folder | Holds | Engine |
|---|---|---|---|
| `SA.Inbetween.Timeline` | `Runtime/` | `GenRules` (ticks, windows, which source meets an edge, length rules), `BridgePlan` | none (`noEngineReferences`) |
| `SA.Inbetween.Timeline.Gen` | `Runtime/Gen/` | `GenTrack`, `GenClip<TPayload>`, the payload structs and clip classes | runtime (Timeline needs a MonoScript); empty playables |
| `SA.Inbetween.Timeline.Editor` | `Editor/`, `Editor/Gen/` | `GenPlanner`, the steps, `GenBake`, `GenOutputs`, `GenPlanJson`, Inspectors, menus, demo, `GenBatch`; the Bridge | Editor |
| `SA.Inbetween.Timeline.TlGlue` | `Editor/TlGlue/` | `TlTimelineEnd`: Loop and Next from tl's `TlTimelineSettings` | Editor; compiled only when `com.tl.unity` is installed |
| `SA.Inbetween.Timeline.Tests`, `.PlayTests` | `Tests/` | EditMode and PlayMode tests | |

## How a bake runs

1. **Collect.** `GenPlanner.Plan(timeline, director)` takes `timeline.GetOutputTracks()` top to bottom (groups flattened, muted
   tracks skipped), and on each `GenTrack` its clips by start. Each becomes a `GenFrame`: the `TimelineClip`, its track, its authored
   order and its window in ticks (`GenRules.Window`: 30 ticks per second, `round(seconds * 30)`, half-open `[start, end)`, at least one
   tick; the same rule as tl's packer).
2. **Plan, by stage.** Clips run grouped by their clip type's `Stage` (Source 0, Pose 10, Generate 20, Retarget 30, Export 40,
   Label 50), in authored order inside a stage. Each clip's step gets `Plan(frame, clip, plan)`. A step reads and adds to the plan:
   sources and poses register themselves; Generate makes a `GenJob`; later stages change the jobs whose window they overlap.
   **Plan writes no file and changes no asset.** It runs on every repaint of the Timeline window (cached for 0.25 s), so keep it cheap.
3. **Seal.** The planner fills in what needs every stage: the character of each job (Retarget, else the track's bound character,
   else a source's), the job name (the Generate clip's name, or `<start>_to_<goal>` while it is still called *Generate*), and the
   cross-clip faults (two jobs with one name, overlapping jobs, pose clips that touch nothing).
4. **Bake.** `GenBake.Start` refuses before touching anything if the plan has a fault, the timeline is not saved, the tools are
   missing or Play mode is on. Then per job and per version it creates or updates one `InbetweenRecipe` in `<Timeline>_Generated/`
   and runs it through `RecipeRunner` unless `recipe.BuiltFrom == RecipeRunner.Hash(recipe)`; one job at a time, polled on
   `EditorApplication.update`.
5. **Finish, by stage.** When a job's recipes are up to date, every clip overlapping the job gets `Finish(frame, clip, context)`, again
   by stage then authored order. Export writes the files through `SaClipExport`, `SaClipHumanoidExport` and `SaClipFbxExport` and
   places the clip on the **Generated** Animation Track (`GenOutputs`); a job no Export clip covers gets `ExportPayload.Default`.
   Label labels what Export wrote. After all jobs, `GenOutputs.PlaceSources` puts the Unity source clips around each placed clip
   on the Generated track (named `... (source)`, when the job's Export has `PlaceSources`), recomputes the clip holds and matches
   the hips along the track (`TimelineBridge.MatchChain`: XZ and yaw, heights kept) on a hidden copy of the bound character.
6. **Plan file.** `<Timeline>_Generated/<Timeline>.genplan.json` (`GenPlanJson`): canonical JSON in tl's timeline-JSON shape
   (`name`, `tickRate`, `duration`, `tracks[].clips[]` with `namespace`, `type`, `start`, `end`, `data`), then `generations` sorted by
   name with their start, goals, length, character, exports and recipe hashes, then `faults` and a `sha256:` digest of everything
   before it. Invariant culture, fixed key order, written only when it changed.

Determinism: the plan depends only on the timeline's data, so the same layout gives the same JSON bytes and the same recipes;
the generator gives the same bytes for the same recipe. Do not add hash-ordered output, `DateTime`, unseeded random or
culture-dependent formatting to a step.

## The contract

```csharp
public abstract class GenClip : PlayableAsset, ITimelineClipAsset
{
    public abstract int Stage { get; }
    public abstract string DefaultName { get; }
    public virtual ClipCaps clipCaps => ClipCaps.None;
    public abstract void ResetPayload();
}

public abstract class GenClip<TPayload> : GenClip where TPayload : struct
{
    public TPayload Payload;
    protected virtual TPayload DefaultPayload => default;
    public override void ResetPayload() => Payload = DefaultPayload;
}

public abstract class GenStep<TClip> : IGenStep where TClip : GenClip
{
    public virtual void Plan(GenFrame frame, TClip clip, GenPlan plan) { }
    public virtual bool Finish(GenFrame frame, TClip clip, GenBakeContext context, out string error) { error = ""; return true; }
}
```

- `Payload` is what the designer types. The clip Inspector draws its fields directly. `DefaultPayload` is set on every new clip
  (`GenTrack.OnCreateClip` calls `ResetPayload`), and `PlayableAsset.duration` is a new clip's length.
- `GenFrame`: `Clip` (the `TimelineClip`), `Track`, `Order`, `Span` (ticks), `StartSeconds`, `EndSeconds`, `Where` (the clip's name,
  track and time, for messages).
- `GenPlan`: `Timeline`, `Director` (null when baked from the Project window or batch), `Sources`, `Poses`, `Jobs`,
  `JobsOverlapping(span)`, `BoundCharacter(track)`, `Fault(frame, text)`, `Note(frame, text)`, `Loop`, `Next`.
- `GenJob`: `Name`, `Frame`, `Payload` (the Generate clip's), `From`, `Goals` (recipe `ClipFrame`s), `Moves`, `MoveSeconds`,
  `Variants`, `Character`, `Rig`, `FootIK`, `Export`, `HasExport`, `Labels`.
- `GenBakeContext` (Finish only): `Job`, `Recipe` (the kept version, generated), `Alternates`, `Files` (written so far),
  `Wrote(path)`, `Note(text)`.

Steps are found with `TypeCache`: any non-abstract class with a public parameterless constructor that derives from `GenStep<TClip>`.
There is no list to edit. One step per clip type; two make a fault that names both classes. A clip type with no step, or a clip whose
script is missing, is a fault on that clip.

### Messages

A fault is a sentence the designer can act on. `plan.Fault(frame, text)` puts the clip's name, track and time in front, so write
the rest as *what is wrong. What to do.*: `"it covers no Generate clip, so it is not used. Stretch it over the Generate clips it is
for."`. It shows in red on the clip in the Timeline window, in the clip's Inspector, in the track's Inspector and as the reason a bake
refuses. `plan.Note` is for something that works but may surprise (shown in blue in the Inspector). A `Finish` that returns false
with an error stops that job and reports `"<clip>: <error>"`; the other jobs still bake.

## Add a clip type

Two files: the clip (data, in a runtime assembly) and its step (behaviour, in an Editor assembly). In a project without asmdefs put
the clip under `Assets/` (for example `Assets/Inbetween/`) and the step under an `Editor` folder; both
`SA.Inbetween.Timeline.Gen` and `SA.Inbetween.Timeline.Editor` are auto-referenced. With your own asmdefs, the clip's references
`SA.Inbetween.Timeline.Gen` and `Unity.Timeline`; the step's (Editor only) adds `SA.Inbetween.Timeline.Editor` and whatever it calls.
The Timeline window needs the clip class in a file named after it.

The **Label** clip ships with the package and is the whole pattern: it puts an asset label on the files of the generations it covers.

`LabelClip.cs`:

```csharp
using System;
using System.ComponentModel;
using UnityEngine;

namespace SA.Inbetween.Timeline.Gen
{
    [Serializable]
    public struct LabelPayload
    {
        [Tooltip("An asset label put on every file the Generate clips under this clip write. Find them with l:<label> in the Project window search.")]
        public string Label;
    }

    [DisplayName("Label")]
    public sealed class LabelClip : GenClip<LabelPayload>
    {
        public const double DefaultSeconds = 2.0;

        public override int Stage => GenStage.Label;

        public override string DefaultName => "Label";

        protected override LabelPayload DefaultPayload => new LabelPayload { Label = "Inbetween" };

        public override double duration => DefaultSeconds;
    }
}
```

`LabelStep.cs`:

```csharp
using System.Linq;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class LabelStep : GenStep<LabelClip>
    {
        public override void Plan(GenFrame frame, LabelClip clip, GenPlan plan)
        {
            var label = (clip.Payload.Label ?? "").Trim();
            if (label == "")
            {
                plan.Fault(frame, "type the label to put on the files in the Inspector.");
                return;
            }

            var jobs = plan.JobsOverlapping(frame.Span);
            if (jobs.Count == 0) plan.Fault(frame, "it covers no Generate clip, so it is not used. Stretch it over the Generate clips it is for.");
            foreach (var job in jobs)
                if (!job.Labels.Contains(label))
                    job.Labels.Add(label);
        }

        public override bool Finish(GenFrame frame, LabelClip clip, GenBakeContext context, out string error)
        {
            error = "";
            var label = clip.Payload.Label.Trim();
            foreach (var path in context.Files)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null) continue;
                var labels = AssetDatabase.GetLabels(asset);
                if (labels.Contains(label)) continue;
                AssetDatabase.SetLabels(asset, labels.Concat(new[] { label }).ToArray());
            }

            return true;
        }
    }
}
```

What each piece does:

- `[DisplayName("Label")]` is the name in a Generation Track's right-click **Add** menu. Every `GenClip` subclass appears there by
  itself: `GenTrack` accepts the `GenClip` base type.
- The payload's public fields are the clip's Inspector; `[Tooltip]` is the help line. Use the types Unity serializes (numbers, bool,
  enums, strings, `float3`-like structs, asset references). Asset references are fine here (a Source holds an `AnimationClip`); a
  reference to a scene object is not (it is lost on save): take scene objects from the track binding through `plan.BoundCharacter`.
- `Stage` decides when the step runs relative to the others; pick the stage of what you read. A step that changes how a job is
  generated must run before Generate's recipes are made, so it changes the `GenJob` in `Plan` (Retarget does). A step that uses the
  result runs in `Finish` at stage 40 or later.
- `Plan` validates and records; `Finish` does the work. Both get the clip itself, so its time (`frame.Span`) is how it finds what
  it applies to (`plan.JobsOverlapping`). No clip points at another clip.

Then:

1. Add a test next to `Tests/Editor/GenPlanTests.cs`: build a `TimelineAsset` in memory, add `GenTrack`s and clips with
   `track.CreateClip<YourClip>()`, call `GenPlanner.Plan(timeline, null)` and assert the jobs and the fault texts.
   `Every_clip_type_has_one_step` already fails if your clip has no step.
2. A step whose `Finish` writes files needs the tools; follow `GenBakeTests` and `Assert.Ignore` when they or the assets are missing.

## Bake from a script or CI

```sh
Unity -batchmode -nographics -projectPath <project> -executeMethod SA.Inbetween.Timeline.Editor.GenBatch.Bake \
  -genTimeline "Assets/Moves/Jump to Landing.playable" -logFile <abs>/bake.log
```

No `-quit`: the bake runs on the Editor's update loop and `GenBatch` exits with 0, or 1 when anything failed. Repeat `-genTimeline`
for more timelines. From C#: `GenBake.Start(timeline, director, out error)`, then poll `run.Done` (or `GenBake.Finished`); read
`run.Report`, `run.Generated`, `run.Reused`, `run.Digest`.

## With tl installed (optional)

tl.unity is installed by git URL, `https://github.com/IAFahim/tl.unity.git?path=/com.tl.unity#main` (newest Unity 7, URP; see its
`docs/USING-ECS.md`). This package does not depend on it. When it is present, `versionDefines` turns on `SA_TL` and
`SA.Inbetween.Timeline.TlGlue` compiles; it implements `IGenTimelineEnd` from tl's `TlTimelineSettings` marker:

| Tl Settings, When it ends | Generation |
|---|---|
| Stop, Hold | nothing changes |
| Loop | the timeline wraps at its end, as in tl: a Generate clip with no source after it lands on the first source; one at tick 0 with no source before it starts from the last |
| Next | after a bake that succeeds, the timeline in **Play next** is baked too (each timeline once per chain) |

Any other provider can implement `IGenTimelineEnd` the same way; the first one that answers wins.
