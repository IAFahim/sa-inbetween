using System.ComponentModel;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Gen
{
    [DisplayName("SA Inbetween/Generation Track")]
    [TrackColor(0.29f, 0.71f, 0.55f)]
    [TrackClipType(typeof(GenClip))]
    [TrackBindingType(typeof(Animator))]
    public sealed class GenTrack : TrackAsset
    {
        protected override void OnCreateClip(TimelineClip clip)
        {
            if (!(clip.asset is GenClip step)) return;
            step.ResetPayload();
            clip.displayName = step.DefaultName;
        }
    }
}
