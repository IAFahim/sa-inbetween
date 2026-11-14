using System.Collections.Generic;
using System.IO;
using SA.Editor;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public static class SaClipHumanoidExport
    {
        static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };

        public static string ExportPath(InbetweenRecipe recipe)
        {
            var path = SaClipExport.ExportPath(recipe);
            return path == "" ? "" : path.Substring(0, path.Length - ".anim".Length) + "_Humanoid.anim";
        }

        public static string CurveName(string muscle)
        {
            foreach (var side in new[] { "Left", "Right" })
                foreach (var finger in Fingers)
                {
                    var prefix = side + " " + finger + " ";
                    if (!muscle.StartsWith(prefix)) continue;
                    var rest = muscle.Substring(prefix.Length);
                    return side + "Hand." + finger + "." + rest;
                }

            return muscle;
        }

        public static bool TryExport(InbetweenRecipe recipe, out AnimationClip saved, out string error)
        {
            saved = null;
            var character = SaClipExport.CharacterFor(recipe);
            if (recipe.Output == null) { error = "Generate the motion first."; return false; }
            if (character == null) { error = "Choose the character to export for."; return false; }
            var path = ExportPath(recipe);
            if (path == "") { error = "Save the recipe as an asset first."; return false; }
            if (!TryBuild(recipe.Output, character, out var clip, out error)) return false;
            saved = SaClipExport.Save(clip, path);
            return true;
        }

        public static bool TryBuild(SaClipAsset source, GameObject character, out AnimationClip clip, out string error)
        {
            clip = null;
            if (source == null || source.Bytes == null || source.Bytes.Length == 0) { error = "The .saclip is empty."; return false; }
            if (character == null) { error = "Choose the character to export for."; return false; }
            var instance = Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.name = character.name;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    error = "'" + character.name + "' has no Humanoid avatar; export the Generic clip instead.";
                    return false;
                }

                if (!FamilyClips.TryPlan(instance, out var plan, out error)) return false;
                if (!SaClipExport.TrySampleLocals(plan, source.Bytes, out var locals, out var frames, out error)) return false;
                clip = Curves(plan, animator, locals, frames, source.name);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static AnimationClip Curves(FamilyRigPlan plan, Animator animator, SA.Motion.Numerics.Pose[] locals, int frames, string name)
        {
            var muscles = HumanTrait.MuscleCount;
            var times = new float[frames];
            var root = new float[7][];
            for (var c = 0; c < 7; c++) root[c] = new float[frames];
            var values = new float[muscles][];
            for (var m = 0; m < muscles; m++) values[m] = new float[frames];
            var pose = new HumanPose();
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            try
            {
                var previous = Quaternion.identity;
                for (var f = 0; f < frames; f++)
                {
                    times[f] = f / SaClipExport.Rate;
                    SaClipExport.ApplyFrame(plan, locals, f);
                    handler.GetHumanPose(ref pose);
                    var q = pose.bodyRotation;
                    if (f > 0 && Quaternion.Dot(q, previous) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    previous = q;
                    root[0][f] = pose.bodyPosition.x;
                    root[1][f] = pose.bodyPosition.y;
                    root[2][f] = pose.bodyPosition.z;
                    root[3][f] = q.x;
                    root[4][f] = q.y;
                    root[5][f] = q.z;
                    root[6][f] = q.w;
                    for (var m = 0; m < muscles; m++) values[m][f] = pose.muscles[m];
                }
            }
            finally
            {
                handler.Dispose();
            }

            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            string[] rootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (var c = 0; c < 7; c++) SaClipExport.Add(bindings, curves, "", typeof(Animator), rootNames[c], times, root[c]);
            for (var m = 0; m < muscles; m++) SaClipExport.Add(bindings, curves, "", typeof(Animator), CurveName(HumanTrait.MuscleName[m]), times, values[m]);
            var clip = new AnimationClip { name = name, frameRate = SaClipExport.Rate };
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            settings.loopBlendPositionY = true;
            settings.keepOriginalPositionY = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }
    }
}
