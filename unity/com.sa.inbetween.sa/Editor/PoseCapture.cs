using System.Collections.Generic;
using System.IO;
using SA.Animation;
using SA.Animation.Content;
using SA.Editor;
using SA.Motion.Numerics;
using UnityEditor;
using UnityEngine;
using Pose = SA.Motion.Numerics.Pose;

namespace SA.Inbetween.Clips.Editor
{
    public static class PoseCapture
    {
        public const float Rate = 30f;

        public static GameObject CharacterOf(GameObject selected)
        {
            if (selected == null || EditorUtility.IsPersistent(selected)) return null;
            var animator = selected.GetComponentInParent<Animator>(true);
            if (animator == null) animator = selected.GetComponentInChildren<Animator>(true);
            return animator != null ? animator.gameObject : null;
        }

        public static bool TryCanonical(GameObject posed, out Pose[] canonical, out string error)
        {
            canonical = System.Array.Empty<Pose>();
            if (posed == null)
            {
                error = "Select the character in the scene (a GameObject with an Animator).";
                return false;
            }

            var rest = RestInstance(posed);
            try
            {
                if (!FamilyClips.TryPlan(rest, out var plan, out error)) return false;
                for (var b = 1; b < plan.BoneCount; b++)
                {
                    var live = posed.transform.Find(plan.Paths[b]);
                    if (live == null)
                    {
                        error = "'" + posed.name + "' has no bone '" + plan.Paths[b] + "'.";
                        return false;
                    }

                    plan.Bones[b].localPosition = live.localPosition;
                    plan.Bones[b].localRotation = live.localRotation;
                }

                return FamilyClips.TryCapture(plan, out canonical, out error);
            }
            finally
            {
                Object.DestroyImmediate(rest);
            }
        }

        static GameObject RestInstance(GameObject posed)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(posed);
            var copy = Object.Instantiate(source != null && source.transform.parent == null ? source : posed);
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.name = posed.name;
            if (source != null && source.transform.parent == null) return copy;
            var animator = copy.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return copy;
            var skeleton = new Dictionary<string, SkeletonBone>();
            foreach (var bone in animator.avatar.humanDescription.skeleton)
                if (!skeleton.ContainsKey(bone.name)) skeleton.Add(bone.name, bone);
            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
                if (t != copy.transform && skeleton.TryGetValue(t.name, out var bone))
                {
                    t.localPosition = bone.position;
                    t.localRotation = bone.rotation;
                }

            return copy;
        }

        public static bool TryEncode(Pose[] canonical, out byte[] bytes, out string error)
        {
            var samples = new Pose[canonical.Length * 2];
            canonical.CopyTo(samples, 0);
            canonical.CopyTo(samples, canonical.Length);
            var root = new[] { RigidPose.Identity, RigidPose.Identity };
            return SharedFamilyBake.EncodeCanonicalSamples(samples, 2, 1.0 / Rate, false, RootPolicy.Planar, root, System.Array.Empty<PoseMarker>(), true, out bytes, out error);
        }

        public static bool TryCapture(GameObject posed, string path, out SaClipAsset asset, out string error)
        {
            asset = null;
            if (!TryCanonical(posed, out var canonical, out error) || !TryEncode(canonical, out var bytes, out error)) return false;
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            asset = AssetDatabase.LoadAssetAtPath<SaClipAsset>(path);
            if (asset != null) return true;
            error = "Unity did not import " + path;
            return false;
        }

        public static string PathFor(InbetweenRecipe recipe, string role)
        {
            var path = AssetDatabase.GetAssetPath(recipe);
            var folder = string.IsNullOrEmpty(path) ? "Assets" : Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            return folder + "/" + recipe.name + "_" + role + "_pose.saclip";
        }

        [MenuItem("Tools/SA/Inbetween/Capture Pose From Selection")]
        static void CaptureSelection()
        {
            var posed = CharacterOf(Selection.activeGameObject);
            if (posed == null)
            {
                EditorUtility.DisplayDialog("Capture pose", "Select the posed character in the scene (a GameObject with an Animator), then try again.", "OK");
                return;
            }

            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/" + posed.name + "_pose.saclip");
            if (!TryCapture(posed, path, out var asset, out var error))
            {
                EditorUtility.DisplayDialog("Capture pose", error, "OK");
                return;
            }

            EditorGUIUtility.PingObject(asset);
        }

        [MenuItem("Tools/SA/Inbetween/Capture Pose From Selection", true)]
        static bool CanCaptureSelection() => CharacterOf(Selection.activeGameObject) != null;
    }
}
