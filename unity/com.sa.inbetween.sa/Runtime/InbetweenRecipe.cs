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

    [Serializable]
    public struct ClipFrame
    {
        public const float ClipFrameRate = 30f;

        public float AnchorSeconds => Where == FrameWhere.Frame ? Frame / ClipFrameRate : Seconds;

        public SaClipAsset Clip;
        public FrameWhere Where;
        public float Seconds;
        public int Frame;
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
    }
}
