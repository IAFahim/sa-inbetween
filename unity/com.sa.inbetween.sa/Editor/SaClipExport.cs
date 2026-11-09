using System;
using System.Collections.Generic;
using System.IO;
using SA.Animation;
using SA.Animation.Content;
using SA.Editor;
using SA.Motion.Numerics;
using UnityEditor;
using UnityEngine;
using Pose = SA.Motion.Numerics.Pose;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Inbetween.Clips.Editor
{
    public static class SaClipExport
    {
        public const float Rate = 30f;

        public static string ExportPath(InbetweenRecipe recipe)
        {
            var path = AssetDatabase.GetAssetPath(recipe);
            return string.IsNullOrEmpty(path) ? "" : Path.Combine(Path.GetDirectoryName(path) ?? "Assets", recipe.name + ".anim").Replace('\\', '/');
        }

        public static GameObject CharacterFor(InbetweenRecipe recipe)
        {
            if (recipe.Character != null) return recipe.Character;
            if (recipe.Start.Kind == SourceKind.UnityClip && recipe.Start.Character != null) return recipe.Start.Character;
            foreach (var goal in recipe.Goals)
                if (goal.Kind == SourceKind.UnityClip && goal.Character != null)
                    return goal.Character;
            return null;
        }

        public static string SavedKey(InbetweenRecipe recipe)
        {
            var character = CharacterFor(recipe);
            if (character == null) return "";
            var path = AssetDatabase.GetAssetPath(character);
            return recipe.BuiltFrom + "|" + character.name + "|" + path + "|" + (path == "" ? "" : AssetDatabase.GetAssetDependencyHash(path).ToString());
        }

        public static bool SavedClipStale(InbetweenRecipe recipe) =>
            recipe.SaveAnimationClip && recipe.Output != null && CharacterFor(recipe) != null && (recipe.SavedClip == null || recipe.SavedFrom != SavedKey(recipe));

        public static bool TrySave(InbetweenRecipe recipe, out string error)
        {
            if (!TryExport(recipe, out var saved, out error)) return false;
            recipe.SavedClip = saved;
            recipe.SavedFrom = SavedKey(recipe);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
            return true;
        }

        public static void SaveAfterGenerate(InbetweenRecipe recipe)
        {
            if (!recipe.SaveAnimationClip || CharacterFor(recipe) == null) return;
            if (!TrySave(recipe, out var error))
                Debug.LogWarning("SA Inbetween: '" + recipe.name + "' was generated, but its Unity clip was not saved: " + error, recipe);
        }

        public static bool TryExport(InbetweenRecipe recipe, out AnimationClip saved, out string error)
        {
            saved = null;
            if (recipe.Output == null) { error = "Generate the motion first."; return false; }
            var character = CharacterFor(recipe);
            if (character == null) { error = "Choose the character to export for."; return false; }
            var path = ExportPath(recipe);
            if (path == "") { error = "Save the recipe as an asset first."; return false; }
            return TryExport(recipe.Output, character, path, out saved, out error);
        }

        public static bool TryExport(SaClipAsset source, GameObject character, string path, out AnimationClip saved, out string error)
        {
            saved = null;
            if (!TryBuild(source, character, out var clip, out error)) return false;
            saved = Save(clip, path);
            return true;
        }

        internal static AnimationClip Save(AnimationClip clip, string path)
        {
            clip.name = Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            return clip;
        }

        public static bool TryBuild(SaClipAsset source, GameObject character, out AnimationClip clip, out string error)
        {
            clip = null;
            if (source == null || source.Bytes == null || source.Bytes.Length == 0) { error = "The .saclip is empty."; return false; }
            if (character == null) { error = "Choose the character to export for."; return false; }
            var instance = UnityEngine.Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.name = character.name;
            try
            {
                if (!FamilyClips.TryPlan(instance, out var plan, out error)) return false;
                if (!TrySampleLocals(plan, source.Bytes, out var locals, out var frames, out error)) return false;
                clip = Curves(plan, locals, frames, source.name);
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        public static bool TrySampleLocals(FamilyRigPlan plan, byte[] bytes, out Pose[] locals, out int frames, out string error)
        {
            locals = Array.Empty<Pose>();
            frames = 0;
            var reference = SharedFamilyBake.CanonicalReferencePoses();
            var load = SharedClipReader.Load(bytes, plan.Family, reference, out var loaded);
            if (load != AnimationError.None || loaded == null)
            {
                error = "The .saclip does not load for the humanoid family (" + load + ").";
                return false;
            }

            frames = Math.Max(2, (int)Math.Round(loaded.Duration * Rate) + 1);
            var canonical = new Pose[frames * FamilyClips.CanonicalBones];
            var roots = new RigidPose[frames];
            var probe = new Pose[FamilyClips.CanonicalBones];
            for (var f = 0; f < frames; f++)
            {
                var seconds = Math.Min(f / (double)Rate, loaded.Duration);
                var sample = loaded.Sample(seconds, reference, probe);
                if (sample != AnimationError.None)
                {
                    error = "Sampling frame " + f + " failed (" + sample + ").";
                    return false;
                }

                Array.Copy(probe, 0, canonical, f * FamilyClips.CanonicalBones, FamilyClips.CanonicalBones);
                roots[f] = RootAt(loaded, seconds);
            }

            return FamilyClips.TryApply(plan, canonical, roots, out locals, out error);
        }

        public static void ApplyFrame(FamilyRigPlan plan, Pose[] locals, int frame)
        {
            var count = plan.BoneCount;
            for (var b = 1; b < count; b++)
            {
                var bone = plan.Bones[b];
                var pose = plan.CanonicalOfBone[b] > 0 ? locals[frame * count + b] : plan.Rest[b];
                bone.localRotation = new UnityEngine.Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
                if (b == plan.Hips || plan.CanonicalOfBone[b] <= 0)
                    bone.localPosition = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            }
        }

        public static RigidPose RootAt(SharedClip clip, double seconds)
        {
            if (clip.Root == null || clip.Root.Length == 0 || clip.SampleCount < 2 || clip.Duration <= 0.0) return RigidPose.Identity;
            var x = Math.Max(0.0, Math.Min(clip.SampleCount - 1, seconds * (clip.SampleCount - 1) / clip.Duration));
            var i = Math.Min(clip.SampleCount - 2, (int)Math.Floor(x));
            var a = (float)(x - i);
            var p = clip.Root[i];
            var q = clip.Root[i + 1];
            var r1 = q.Rotation;
            if (Quaternion.Dot(p.Rotation, r1) < 0f) r1 = new Quaternion(-r1.X, -r1.Y, -r1.Z, -r1.W);
            var rotation = Quaternion.Normalize(new Quaternion(
                p.Rotation.X + (r1.X - p.Rotation.X) * a,
                p.Rotation.Y + (r1.Y - p.Rotation.Y) * a,
                p.Rotation.Z + (r1.Z - p.Rotation.Z) * a,
                p.Rotation.W + (r1.W - p.Rotation.W) * a));
            return new RigidPose(p.Position + (q.Position - p.Position) * a, rotation);
        }

        static AnimationClip Curves(FamilyRigPlan plan, Pose[] locals, int frames, string name)
        {
            var clip = new AnimationClip { name = name, frameRate = Rate };
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            var count = plan.BoneCount;
            var times = new float[frames];
            for (var f = 0; f < frames; f++) times[f] = f / Rate;
            var rotation = new float[4][];
            for (var c = 0; c < 4; c++) rotation[c] = new float[frames];
            for (var b = 1; b < count; b++)
            {
                var path = plan.Paths[b];
                if (plan.CanonicalOfBone[b] > 0)
                {
                    var previous = Quaternion.Identity;
                    for (var f = 0; f < frames; f++)
                    {
                        var q = Quaternion.Normalize(locals[f * count + b].Rotation);
                        if (f > 0 && Quaternion.Dot(q, previous) < 0f) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
                        previous = q;
                        rotation[0][f] = q.X;
                        rotation[1][f] = q.Y;
                        rotation[2][f] = q.Z;
                        rotation[3][f] = q.W;
                    }

                    AddRotation(bindings, curves, path, times, rotation);
                }
                else
                {
                    var rest = plan.Rest[b].Rotation;
                    AddConstant(bindings, curves, path, "m_LocalRotation.x", rest.X, times[frames - 1]);
                    AddConstant(bindings, curves, path, "m_LocalRotation.y", rest.Y, times[frames - 1]);
                    AddConstant(bindings, curves, path, "m_LocalRotation.z", rest.Z, times[frames - 1]);
                    AddConstant(bindings, curves, path, "m_LocalRotation.w", rest.W, times[frames - 1]);
                }

                if (b == plan.Hips)
                {
                    var x = new float[frames];
                    var y = new float[frames];
                    var z = new float[frames];
                    for (var f = 0; f < frames; f++)
                    {
                        var p = locals[f * count + b].Position;
                        x[f] = p.X;
                        y[f] = p.Y;
                        z[f] = p.Z;
                    }

                    Add(bindings, curves, path, "m_LocalPosition.x", times, x);
                    Add(bindings, curves, path, "m_LocalPosition.y", times, y);
                    Add(bindings, curves, path, "m_LocalPosition.z", times, z);
                }
            }

            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        static void AddRotation(List<EditorCurveBinding> bindings, List<AnimationCurve> curves, string path, float[] times, float[][] rotation)
        {
            Add(bindings, curves, path, "m_LocalRotation.x", times, rotation[0]);
            Add(bindings, curves, path, "m_LocalRotation.y", times, rotation[1]);
            Add(bindings, curves, path, "m_LocalRotation.z", times, rotation[2]);
            Add(bindings, curves, path, "m_LocalRotation.w", times, rotation[3]);
        }

        static void AddConstant(List<EditorCurveBinding> bindings, List<AnimationCurve> curves, string path, string property, float value, float end) =>
            Add(bindings, curves, path, property, new[] { 0f, end }, new[] { value, value });

        static void Add(List<EditorCurveBinding> bindings, List<AnimationCurve> curves, string path, string property, float[] times, float[] values) =>
            Add(bindings, curves, path, typeof(Transform), property, times, values);

        internal static void Add(List<EditorCurveBinding> bindings, List<AnimationCurve> curves, string path, Type type, string property, float[] times, float[] values)
        {
            var keys = new Keyframe[times.Length];
            for (var i = 0; i < times.Length; i++)
            {
                var before = i > 0 ? (values[i] - values[i - 1]) / (times[i] - times[i - 1]) : 0f;
                var after = i < times.Length - 1 ? (values[i + 1] - values[i]) / (times[i + 1] - times[i]) : 0f;
                keys[i] = new Keyframe(times[i], values[i], i > 0 ? before : after, i < times.Length - 1 ? after : before);
            }

            bindings.Add(EditorCurveBinding.FloatCurve(path, type, property));
            curves.Add(new AnimationCurve(keys));
        }

        [MenuItem("CONTEXT/SaClipAsset/Export Animation Clip")]
        static void ExportFromInspector(MenuCommand command) => ExportSelected(command.context as SaClipAsset);

        [MenuItem("Assets/SA/Export Animation Clip", false, 311)]
        static void ExportFromProject() => ExportSelected(null);

        [MenuItem("Assets/SA/Export Animation Clip", true)]
        static bool CanExportFromProject() => Selection.GetFiltered<SaClipAsset>(SelectionMode.Assets).Length > 0;

        static void ExportSelected(SaClipAsset source)
        {
            if (source == null)
            {
                var picked = Selection.GetFiltered<SaClipAsset>(SelectionMode.Assets);
                source = picked.Length > 0 ? picked[0] : null;
            }

            GameObject character = null;
            foreach (var selected in Selection.objects)
                if (selected is GameObject go && go.GetComponentInChildren<Animator>(true) != null)
                {
                    character = go;
                    break;
                }

            if (source == null || character == null)
            {
                EditorUtility.DisplayDialog("Export Animation Clip", "Select the .saclip together with the character (model or prefab with an Animator) to export for.", "OK");
                return;
            }

            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(source))?.Replace('\\', '/') ?? "Assets";
            var path = folder + "/" + source.name + ".anim";
            if (!TryExport(source, character, path, out var saved, out var error))
            {
                EditorUtility.DisplayDialog("Export Animation Clip", error, "OK");
                return;
            }

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
        }
    }
}
