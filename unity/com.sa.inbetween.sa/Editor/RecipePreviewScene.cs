using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public struct SkeletonLayer
    {
        public Vector3[] World;
        public Color Color;
        public bool Ghost;
    }

    public static class PreviewMath
    {
        public static float FrameSeconds(ClipFrame frame, float duration)
        {
            switch (frame.Where)
            {
                case FrameWhere.End: return duration;
                case FrameWhere.Start: return 0f;
                default: return Mathf.Clamp(frame.AnchorSeconds, 0f, duration);
            }
        }

        public static float Wrap(double clock, float duration) => duration <= 0f ? 0f : (float)(clock - System.Math.Floor(clock / duration) * duration);

        public static bool IsFinger(int bone) => (bone >= 14 && bone <= 28) || (bone >= 33 && bone <= 47);

        public static float JointRadius(int bone) => bone == SaClipPose.Head ? 0.09f : IsFinger(bone) ? 0f : bone == SaClipPose.Hips ? 0.05f : 0.035f;

        public static string Label(float seconds, float duration) => seconds.ToString("0.00") + " / " + duration.ToString("0.00") + " s";
    }

    public sealed class SkeletonClip
    {
        byte[] loaded;
        public SaClipPose Pose { get; private set; }
        public string Error { get; private set; } = "";
        public readonly Vector3[] World = new Vector3[SaClipPose.BoneCount];

        public float Duration => Pose == null ? 0f : Pose.Duration;

        public bool Bind(byte[] bytes)
        {
            if (ReferenceEquals(bytes, loaded))
                return Pose != null;
            loaded = bytes;
            Pose = null;
            Error = "";
            if (bytes == null || bytes.Length == 0)
                return false;
            if (!SaClipPose.TryLoad(bytes, out var pose, out var error))
            {
                Error = error;
                return false;
            }

            Pose = pose;
            return true;
        }

        public bool BindFile(string path)
        {
            if (loaded != null && Pose != null)
                return true;
            return File.Exists(path) && Bind(File.ReadAllBytes(path));
        }

        public bool Evaluate(float seconds) => Pose != null && Pose.Sample(seconds, World);

        public bool Evaluate(float seconds, Pose root) => Pose != null && Pose.Sample(seconds, root, World);
    }

    public sealed class SkeletonPreview
    {
        public static readonly Color ResultColor = new Color(0.30f, 0.75f, 1f, 1f);
        public static readonly Color StartColor = new Color(0.45f, 1f, 0.45f, 0.35f);
        public static readonly Color GoalColor = new Color(1f, 0.6f, 0.25f, 0.35f);

        readonly SkeletonClip result = new SkeletonClip();
        readonly SkeletonClip start = new SkeletonClip();
        readonly SkeletonClip goal = new SkeletonClip();
        readonly List<SkeletonLayer> layers = new List<SkeletonLayer>();

        public float Time { get; private set; }
        public float Duration => result.Duration;
        public bool Ready => result.Pose != null;
        public string Error => result.Error;

        public IReadOnlyList<SkeletonLayer> Layers => layers;

        public void Bind(InbetweenRecipe recipe)
        {
            result.Bind(recipe.Output == null ? null : recipe.Output.Bytes);
            start.Bind(recipe.Start.Clip == null ? null : recipe.Start.Clip.Bytes);
            var last = recipe.Goals.Count == 0 ? default : recipe.Goals[recipe.Goals.Count - 1];
            goal.Bind(last.Clip == null ? null : last.Clip.Bytes);
        }

        public void Evaluate(InbetweenRecipe recipe, float seconds, bool ghosts)
        {
            layers.Clear();
            if (!Ready)
                return;
            Time = Mathf.Clamp(seconds, 0f, Duration);
            if (ghosts && start.Pose != null && start.Evaluate(PreviewMath.FrameSeconds(recipe.Start, start.Duration), UnityEngine.Pose.identity))
                layers.Add(new SkeletonLayer { World = start.World, Color = StartColor, Ghost = true });
            var last = recipe.Goals.Count == 0 ? default : recipe.Goals[recipe.Goals.Count - 1];
            if (ghosts && goal.Pose != null && goal.Evaluate(PreviewMath.FrameSeconds(last, goal.Duration), result.Pose.RootAt(Duration)))
                layers.Add(new SkeletonLayer { World = goal.World, Color = GoalColor, Ghost = true });
            if (result.Evaluate(Time))
                layers.Insert(0, new SkeletonLayer { World = result.World, Color = ResultColor });
        }
    }

    [InitializeOnLoad]
    public static class SkeletonSceneDrawer
    {
        static readonly List<SkeletonLayer> Current = new List<SkeletonLayer>();
        static object owner;

        static SkeletonSceneDrawer()
        {
            SceneView.duringSceneGui += OnScene;
        }

        public static bool Showing => owner != null;

        public static void Show(object by, IReadOnlyList<SkeletonLayer> layers)
        {
            owner = by;
            Current.Clear();
            foreach (var layer in layers)
                Current.Add(new SkeletonLayer { World = (Vector3[])layer.World.Clone(), Color = layer.Color, Ghost = layer.Ghost });
            SceneView.RepaintAll();
        }

        public static void Hide(object by)
        {
            if (owner != by)
                return;
            owner = null;
            Current.Clear();
            SceneView.RepaintAll();
        }

        static void OnScene(SceneView view)
        {
            if (owner == null || Event.current.type != EventType.Repaint)
                return;
            for (var i = Current.Count - 1; i >= 0; i--)
                Draw(Current[i]);
        }

        public static void Draw(SkeletonLayer layer)
        {
            var world = layer.World;
            var old = Handles.color;
            Handles.color = layer.Color;
            var thickness = layer.Ghost ? 2f : 4f;
            for (var i = 1; i < world.Length; i++)
            {
                var parent = SaClipPose.Parent(i);
                if (parent <= 0)
                    continue;
                Handles.DrawLine(world[parent], world[i], PreviewMath.IsFinger(i) ? 1f : thickness);
            }

            for (var i = 1; i < world.Length; i++)
            {
                var radius = PreviewMath.JointRadius(i);
                if (radius > 0f)
                    Handles.SphereHandleCap(0, world[i], Quaternion.identity, radius * 2f, EventType.Repaint);
            }

            if (!layer.Ghost)
            {
                Handles.DrawWireDisc(world[0], Vector3.up, 0.3f);
                var hips = world[SaClipPose.Hips];
                Handles.DrawDottedLine(world[0], new Vector3(hips.x, world[0].y, hips.z), 3f);
            }

            Handles.color = old;
        }
    }
}
