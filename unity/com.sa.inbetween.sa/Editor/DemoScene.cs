using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SA.Animation;
using SA.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using SaQuaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class DemoPicks
    {
        public GameObject Character;
        public AnimationClip Start;
        public GameObject StartModel;
        public AnimationClip Goal;
        public GameObject GoalModel;
        public int StartFrame;
        public bool MidAir;
        public int Jumps;
        public float Height;
        public readonly List<string> NotHumanoid = new List<string>();
        public string Problem = "";

        public bool Ready => Problem == "";
    }

    public static class DemoScene
    {
        public const string Folder = "Assets/SA Inbetween Demo";
        public const string SceneName = "Inbetween Demo";
        public const string RecipeName = "Jump_MidAir_to_Landing";
        public const string ControllerName = "Inbetween Demo";
        public const string CharacterName = "Inbetween Demo Character";
        public const float Duration = 0.8f;
        public const float MinJumpMetres = 0.15f;
        public const int MaxJumpsTried = 8;
        const float Rate = 30f;

        static readonly string[] JumpWords = { "jump", "leap", "hop" };
        static readonly string[] GoalWords = { "land", "idle", "stand" };

        public static string ScenePath => Folder + "/" + SceneName + ".unity";
        public static string RecipePath => Folder + "/" + RecipeName + ".asset";

        public static DemoPicks Find(string root = "Assets")
        {
            try
            {
                return Search(root);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static DemoPicks Search(string root)
        {
            var picks = new DemoPicks();
            var humanClips = new List<(AnimationClip Clip, GameObject Model, string Path, Avatar Source)>();
            var characters = new List<(GameObject Model, int Clips)>();
            var models = AssetDatabase.FindAssets("t:Model", new[] { root });
            for (var m = 0; m < models.Length; m++)
            {
                var path = AssetDatabase.GUIDToAssetPath(models[m]);
                if (m % 20 == 0) EditorUtility.DisplayProgressBar("SA Inbetween demo", "Looking for a Humanoid character and a jump: " + Path.GetFileName(path), (float)m / models.Length);
                if (path.StartsWith(Folder + "/", StringComparison.Ordinal)) continue;
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
                var model = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
                if (model == null) continue;
                var clips = Takes(path);
                var skinned = model.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    var exported = AssetDatabase.GetMainAssetTypeAtPath(Path.ChangeExtension(path, ".asset")) == typeof(InbetweenRecipe);
                    if ((clips.Count > 0 || skinned) && !exported) picks.NotHumanoid.Add(path);
                    continue;
                }

                var own = InbetweenDiagnostics.IsHumanoid(model) ? model : null;
                var source = importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther ? importer.sourceAvatar : null;
                foreach (var clip in clips) humanClips.Add((clip, own, path, source));
                if (skinned && own != null) characters.Add((model, clips.Count));
            }

            humanClips.Sort((a, b) => string.CompareOrdinal(a.Path + "/" + a.Clip.name, b.Path + "/" + b.Clip.name));
            characters.Sort((a, b) => a.Clips != b.Clips ? a.Clips.CompareTo(b.Clips) : string.CompareOrdinal(AssetDatabase.GetAssetPath(a.Model), AssetDatabase.GetAssetPath(b.Model)));
            if (characters.Count == 0)
            {
                picks.Problem = picks.NotHumanoid.Count > 0
                    ? picks.NotHumanoid.Count + " model file(s) are not set to Humanoid yet. Press Set to Humanoid (it sets Rig > Animation Type to Humanoid on them and reimports), then press Make demo scene again."
                    : "No Humanoid character with a mesh is in the project. Drag a character FBX (for example from Mixamo, with skin) and an animation FBX with a jump into the Project window, then press Make demo scene again.";
                return picks;
            }

            if (humanClips.Count == 0)
            {
                picks.Character = characters[0].Model;
                picks.Problem = picks.NotHumanoid.Count > 0
                    ? "Your animation files are not set to Humanoid yet. Press Set to Humanoid, then press Make demo scene again."
                    : "No Humanoid animation clip is in the project. Drag an animation FBX (a jump works best, for example Mixamo \"Jump\") into the Project window, then press Make demo scene again.";
                return picks;
            }

            var jumps = new List<int>();
            for (var i = 0; i < humanClips.Count; i++)
                if (Named(Label(humanClips[i].Clip, humanClips[i].Path), JumpWords)) jumps.Add(i);
            jumps.Sort((a, b) => humanClips[b].Clip.length.CompareTo(humanClips[a].Clip.length));
            var chosen = jumps.Count > 0 ? jumps[0] : 0;
            var apex = 0;
            var height = 0f;
            var error = "";
            var measured = false;
            for (var k = 0; k < Math.Min(MaxJumpsTried, Math.Max(1, jumps.Count)); k++)
            {
                var index = jumps.Count > 0 ? jumps[k] : 0;
                var candidate = humanClips[index];
                var model = candidate.Model != null ? candidate.Model : CharacterFor(null, candidate.Source, characters);
                if (!TryApex(model, candidate.Clip, out var top, out var up, out var failed))
                {
                    if (!measured) error = FailureAdvice.ForConversion(candidate.Clip.name, model.name, failed);
                    continue;
                }

                if (!measured || up > height)
                {
                    chosen = index;
                    apex = top;
                    height = up;
                    measured = true;
                }

                if (up >= MinJumpMetres) break;
            }

            if (!measured)
            {
                picks.Character = characters[0].Model;
                picks.Problem = error;
                return picks;
            }

            var start = humanClips[chosen];
            picks.Character = CharacterFor(start.Model, start.Source, characters);
            picks.Start = start.Clip;
            picks.StartModel = start.Model != null ? start.Model : picks.Character;
            picks.Jumps = jumps.Count;
            var goal = -1;
            var near = Path.GetDirectoryName(start.Path);
            foreach (var sameFolder in new[] { true, false })
            {
                foreach (var word in GoalWords)
                {
                    goal = humanClips.FindIndex(c => c.Clip != start.Clip && (!sameFolder || Path.GetDirectoryName(c.Path) == near) && Named(Label(c.Clip, c.Path), new[] { word }) && !Named(Label(c.Clip, c.Path), JumpWords));
                    if (goal >= 0) break;
                }

                if (goal >= 0) break;
            }

            var end = goal < 0 ? start : humanClips[goal];
            picks.Goal = end.Clip;
            picks.GoalModel = end.Model != null ? end.Model : picks.Character;
            picks.Height = height;
            picks.MidAir = jumps.Count > 0 && height >= MinJumpMetres;
            var seconds = picks.MidAir ? apex / Rate : picks.Start.length * 0.5f;
            picks.StartFrame = Mathf.Clamp(Mathf.RoundToInt(seconds * picks.Start.frameRate), 0, Mathf.Max(0, Mathf.RoundToInt(picks.Start.length * picks.Start.frameRate)));
            return picks;
        }

        static string Label(AnimationClip clip, string path) => clip.name + " " + Path.GetFileNameWithoutExtension(path);

        static GameObject CharacterFor(GameObject own, Avatar source, List<(GameObject Model, int Clips)> characters)
        {
            if (own != null && own.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return own;
            if (source != null)
                foreach (var c in characters)
                    if (c.Model.GetComponentInChildren<Animator>(true) is Animator animator && animator.avatar == source)
                        return c.Model;
            return characters[0].Model;
        }

        static bool Named(string text, string[] words)
        {
            foreach (var word in words)
                if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        public static List<AnimationClip> Takes(string modelPath)
        {
            var takes = new List<AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal) && clip.length > 0f)
                    takes.Add(clip);
            return takes;
        }

        public static bool TryApex(GameObject model, AnimationClip clip, out int frame, out float metresAboveStart, out string error)
        {
            frame = 0;
            metresAboveStart = 0f;
            var instance = Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.name = model.name;
            try
            {
                if (!FamilyClips.TryPlan(instance, out var plan, out error)) return false;
                using (plan)
                {
                    if (!FamilyClips.TrySampleFixed(plan, clip, Rate, out var canonical, out var root, out _, out var frames, out error)) return false;
                    var lowest = float.MaxValue;
                    var highest = float.MinValue;
                    var first = 0f;
                    for (var f = 0; f < frames; f++)
                    {
                        var hipsLocal = canonical[f * FamilyClips.CanonicalBones + 1];
                        if (!RigidMath.TryCompose(root[f], new RigidPose(hipsLocal.Position, SaQuaternion.Normalize(hipsLocal.Rotation)), out var hips)) continue;
                        var y = hips.Position.Y * plan.HeightRatio;
                        if (f == 0) first = y;
                        lowest = Mathf.Min(lowest, y);
                        if (y > highest)
                        {
                            highest = y;
                            frame = f;
                        }
                    }

                    metresAboveStart = highest - Mathf.Min(first, lowest);
                    return true;
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        public static int SetHumanoid(IEnumerable<string> modelPaths)
        {
            var changed = 0;
            foreach (var path in modelPaths)
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer) || importer.animationType == ModelImporterAnimationType.Human) continue;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
                changed++;
            }

            return changed;
        }

        public static InbetweenRecipe BuildRecipe(DemoPicks picks)
        {
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            recipe.Start = new ClipFrame { Kind = SourceKind.UnityClip, Motion = picks.Start, Character = picks.StartModel, Where = FrameWhere.Frame, Frame = picks.StartFrame };
            recipe.Goals.Clear();
            recipe.Goals.Add(new ClipFrame { Kind = SourceKind.UnityClip, Motion = picks.Goal, Character = picks.GoalModel, Where = FrameWhere.Start });
            recipe.DurationSeconds = Duration;
            recipe.LandExactly = true;
            recipe.Character = picks.Character;
            recipe.SaveAnimationClip = true;
            return recipe;
        }

        public static string Describe(DemoPicks picks)
        {
            if (picks.Start == null) return "";
            var start = picks.MidAir
                ? "'" + BridgeMenu.TakeName(picks.Start) + "' at frame " + picks.StartFrame + ", the top of the jump (hips " + picks.Height.ToString("0.00", CultureInfo.InvariantCulture) + " m up)"
                : "'" + BridgeMenu.TakeName(picks.Start) + "' at frame " + picks.StartFrame + ", its middle (" + (picks.Jumps == 0 ? "no clip named jump was found" : "no jump clip lifts the hips " + (MinJumpMetres * 100f).ToString("0", CultureInfo.InvariantCulture) + " cm") + ", so this starts mid-clip)";
            var goal = picks.Goal == picks.Start ? "the first frame of the same clip" : "the first frame of '" + BridgeMenu.TakeName(picks.Goal) + "'";
            return "From " + start + " to " + goal + ", in " + Duration.ToString("0.0", CultureInfo.InvariantCulture) + " s, on " + picks.Character.name + ".";
        }

        public static InbetweenRecipe Create(DemoPicks picks, NewSceneMode mode, out Scene scene)
        {
            scene = default;
            if (!picks.Ready) return null;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            var existing = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(RecipePath);
            var fresh = BuildRecipe(picks);
            InbetweenRecipe recipe;
            if (existing != null)
            {
                fresh.Output = existing.Output;
                fresh.BuiltFrom = existing.BuiltFrom;
                fresh.SavedClip = existing.SavedClip;
                fresh.SavedFrom = existing.SavedFrom;
                fresh.name = RecipeName;
                EditorUtility.CopySerialized(fresh, existing);
                Object.DestroyImmediate(fresh);
                recipe = existing;
                EditorUtility.SetDirty(recipe);
            }
            else
            {
                AssetDatabase.CreateAsset(fresh, RecipePath);
                recipe = fresh;
            }

            AssetDatabase.SaveAssets();
            var characterPath = AssetDatabase.GetAssetPath(picks.Character);
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, mode);
            if (mode == NewSceneMode.Additive) SceneManager.SetActiveScene(scene);
            recipe = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(RecipePath);
            var model = recipe != null && recipe.Character != null ? recipe.Character : AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
            SceneManager.MoveGameObjectToScene(ground, scene);
            var character = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            character.name = CharacterName;
            character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = character.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.applyRootMotion = false;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<Camera>(out var camera))
                {
                    camera.transform.position = new Vector3(1.6f, 1.5f, 4.2f);
                    camera.transform.LookAt(new Vector3(0f, 0.9f, 0f));
                }

            EditorSceneManager.SaveScene(scene, ScenePath);
            return recipe;
        }

        public static bool TrySave(InbetweenRecipe recipe, out string saved, out string error)
        {
            saved = "";
            if (recipe == null || recipe.Output == null) { error = "Press Generate first."; return false; }
            if (!SaClipExport.TrySave(recipe, out error)) return false;
            var files = new List<string> { AssetDatabase.GetAssetPath(recipe.SavedClip) };
            var playable = recipe.SavedClip;
            var character = SaClipExport.CharacterFor(recipe);
            if (InbetweenDiagnostics.IsHumanoid(character))
            {
                if (!SaClipHumanoidExport.TryExport(recipe, out var human, out error)) return false;
                files.Add(AssetDatabase.GetAssetPath(human));
                playable = human;
            }

            var fbxNote = "";
            if (SaClipFbxExport.TryExport(recipe, out var fbx, out var fbxError)) files.Add(fbx);
            else fbxNote = "FBX not written (the Unity clips are fine): " + fbxError;
                        var controllerPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(recipe)).Replace('\\', '/') + "/" + ControllerName + ".controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, playable);
            var state = controller.layers[0].stateMachine.defaultState;
            var loop = state.AddTransition(state);
            loop.hasExitTime = true;
            loop.exitTime = 1f;
            loop.duration = 0f;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            files.Add(controllerPath);
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded)
                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == CharacterName && root.GetComponentInChildren<Animator>(true) is Animator animator)
                    {
                        animator.runtimeAnimatorController = controller;
                        animator.applyRootMotion = false;
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                        files.Add(ScenePath);
                    }

            saved = string.Join("\n", files) + (fbxNote == "" ? "" : "\n" + fbxNote);
            error = "";
            return true;
        }

        [MenuItem("SA/Inbetween/Create Demo Scene", false, 0)]
        public static void CreateFromMenu()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("SA Inbetween demo", "Leave Play mode first (press the Play button again), then choose SA > Inbetween > Create Demo Scene.", "OK");
                return;
            }

            DemoWindow.Open(true);
        }

        [MenuItem("SA/Inbetween/First Bridge Window", false, 1)]
        static void OpenWindow() => DemoWindow.Open(false);
    }
}
