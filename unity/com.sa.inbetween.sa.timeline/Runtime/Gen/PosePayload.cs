using System;
using SA.Inbetween.Clips;
using UnityEngine;

namespace SA.Inbetween.Timeline.Gen
{
    public enum GenPoseFrom
    {
        Source,
        SourceFrame,
        PoseAsset,
    }

    [Serializable]
    public struct PosePayload
    {
        [Tooltip("Source: the source under this pose's time. Source Frame: frame N of that source. Pose Asset: a .pose asset.")]
        public GenPoseFrom From;

        [Min(0), Tooltip("The frame of the source, at the source's own frame rate. Used when From is Source Frame.")]
        public int Frame;

        [Tooltip("The pose. Used when From is Pose Asset.")]
        public SaPoseAsset Pose;
    }
}
