using System.IO;
using SA.Editor;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public static class SaClipConvert
    {
        public const float Rate = 30f;

        public static bool TryConvert(GameObject character, AnimationClip motion, out byte[] bytes, out string error)
        {
            bytes = System.Array.Empty<byte>();
            if (character == null || motion == null)
            {
                error = "Choose a clip and the character it plays on.";
                return false;
            }

            var instance = Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.name = character.name;
            try
            {
                return FamilyClips.TryPlan(instance, out var plan, out error)
                    && FamilyClips.TrySampleFixed(plan, motion, Rate, out var canonical, out var root, out _, out _, out error)
                    && FamilyClips.TryEncode(plan, canonical, root, Rate, out bytes, out error);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [MenuItem("Assets/SA/Convert to .saclip", false, 310)]
        static void Convert()
        {
            if (!Pick(out var motion, out var character))
            {
                EditorUtility.DisplayDialog("Convert to .saclip", "Select an animation clip and the character (model or prefab with an Animator) it plays on, then try again.", "OK");
                return;
            }

            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(motion))?.Replace('\\', '/') ?? "Assets";
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + BridgeMenu.TakeName(motion) + ".saclip");
            if (!TryConvert(character, motion, out var bytes, out var error))
            {
                EditorUtility.DisplayDialog("Convert to .saclip", error, "OK");
                return;
            }

            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path);
            var asset = AssetDatabase.LoadAssetAtPath<SaClipAsset>(path);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        [MenuItem("Assets/SA/Convert to .saclip", true)]
        static bool CanConvert() => Pick(out _, out _);

        static bool Pick(out AnimationClip motion, out GameObject character)
        {
            motion = null;
            character = null;
            foreach (var selected in Selection.objects)
            {
                if (selected is AnimationClip clip && motion == null)
                    motion = clip;
                else if (selected is GameObject go && character == null && go.GetComponentInChildren<Animator>(true) != null)
                    character = go;
            }

            return motion != null && character != null;
        }
    }
}
