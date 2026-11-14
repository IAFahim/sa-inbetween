using System.Collections.Generic;
using System.Linq;
using SA.Inbetween.Clips;
using SA.Inbetween.Timeline.Gen;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public readonly struct GenFault
    {
        public readonly TimelineClip Clip;
        public readonly string Message;

        public GenFault(TimelineClip clip, string message)
        {
            Clip = clip;
            Message = message;
        }
    }

    public sealed class GenSource
    {
        public GenFrame Frame;
        public SourceClip Asset;
        public double Length;
        public bool Loops;
        public float Rate;
        public string Name = "";

        public SourcePayload Payload => Asset.Payload;

        public double Start => Frame.Clip.start;

        public double ClipIn => Frame.Clip.clipIn;

        public double TimeScale => Frame.Clip.timeScale;

        public double Duration => Frame.Clip.duration;
    }

    public sealed class GenPose
    {
        public GenFrame Frame;
        public bool IsStart;
        public PosePayload Payload;
        public bool Used;
    }

    public sealed class GenJob
    {
        public GenFrame Frame;
        public GeneratePayload Payload;
        public string Name = "";
        public int Moves = 1;
        public double MoveSeconds;
        public GenSource StartSource;
        public GenSource GoalSource;
        public ClipFrame From;
        public string FromText = "";
        public readonly List<ClipFrame> Goals = new List<ClipFrame>();
        public readonly List<string> GoalTexts = new List<string>();
        public readonly List<GenVariant> Variants = new List<GenVariant>();
        public GameObject Character;
        public GenRig Rig;
        public bool FootIK;
        public bool HasRetarget;
        public ExportPayload Export = ExportPayload.Default;
        public bool HasExport;
        public readonly List<string> Labels = new List<string>();

        public GenSpan Span => Frame.Span;

        public double StartSeconds => Frame.Clip.start;

        public double EndSeconds => Frame.Clip.end;

        public double WindowSeconds => Frame.Clip.duration;
    }

    public sealed class GenPlan
    {
        public TimelineAsset Timeline;
        public PlayableDirector Director;
        public bool Loop;
        public TimelineAsset Next;
        public int Duration;
        public readonly List<GenFrame> Clips = new List<GenFrame>();
        public readonly List<GenSource> Sources = new List<GenSource>();
        public readonly List<GenPose> Poses = new List<GenPose>();
        public readonly List<GenJob> Jobs = new List<GenJob>();
        public readonly List<GenFault> Faults = new List<GenFault>();
        public readonly List<GenFault> Notes = new List<GenFault>();

        public bool Ready => Faults.Count == 0 && Jobs.Count > 0;

        public List<GenSpan> SourceSpans() => Sources.Select(s => s.Frame.Span).ToList();

        public void Fault(GenFrame frame, string message) => Faults.Add(new GenFault(frame.Clip, frame.Where + ": " + message));

        public void Fault(string message) => Faults.Add(new GenFault(null, message));

        public void Note(GenFrame frame, string message) => Notes.Add(new GenFault(frame.Clip, frame.Where + ": " + message));

        public List<GenJob> JobsOverlapping(GenSpan span) => Jobs.Where(j => j.Span.Overlaps(span)).ToList();

        public IEnumerable<string> FaultsFor(TimelineClip clip) => Faults.Where(f => f.Clip == clip).Select(f => f.Message);

        public IEnumerable<string> NotesFor(TimelineClip clip) => Notes.Where(f => f.Clip == clip).Select(f => f.Message);

        public GameObject BoundCharacter(TrackAsset track)
        {
            if (Director == null || track == null) return null;
            var animator = TimelineBridge.BoundAnimator(Director, track);
            return animator != null ? TimelineBridge.CharacterAsset(animator.gameObject) : null;
        }
    }
}
