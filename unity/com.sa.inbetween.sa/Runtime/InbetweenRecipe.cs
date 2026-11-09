using System;
using System.Collections.Generic;
using UnityEngine;

namespace SA.Inbetween.Clips
{
    public enum FrameWhere
    {
        End,
        Start,
        Time,
        Frame,
    }

    public enum SourceKind
    {
        SaClip,
        UnityClip,
    }

    [Serializable]
    public struct ClipFrame
    {
        public const float ClipFrameRate = 30f;

        public float FrameRate => Kind == SourceKind.UnityClip && Motion != null && Motion.frameRate > 0f ? Motion.frameRate : ClipFrameRate;

        public float AnchorSeconds => Where == FrameWhere.Frame ? Frame / FrameRate : Seconds;

        public bool HasSource => Kind == SourceKind.UnityClip ? Motion != null && Character != null : Clip != null;

        public SaClipAsset Clip;
        public FrameWhere Where;
        public float Seconds;
        public int Frame;
        public SourceKind Kind;
        public AnimationClip Motion;
        public GameObject Character;
    }

    public sealed class InbetweenRecipe : ScriptableObject
    {
        public const float MinSeconds = 0.6f;
        public const float MaxSeconds = 1.93f;
        public const int MaxGoals = 4;

        public ClipFrame Start = new ClipFrame { Where = FrameWhere.End };
        public List<ClipFrame> Goals = new List<ClipFrame> { new ClipFrame { Where = FrameWhere.Start } };
        public float DurationSeconds = 1.0f;
        public bool LandExactly = true;
        public SaClipAsset Output;
        public string BuiltFrom = "";
        public GameObject Character;
    }
}
