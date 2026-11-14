using System.Collections;
using System.IO;
using NUnit.Framework;
using SA.Inbetween.Clips.Editor;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SA.Inbetween.Clips.Tests
{
    public sealed class DemoReadinessTests
    {
        const string JumpResult = "Assets/InbetweenDemo/MidAirJump.saclip";

        static bool FakeToolsRun => Application.platform == RuntimePlatform.LinuxEditor || Application.platform == RuntimePlatform.OSXEditor;

        [UnityTest]
        public IEnumerator A_hung_step_is_stopped_at_the_step_limit_and_the_First_Bridge_window_says_so()
        {
            if (!FakeToolsRun) Assert.Ignore("the fake tools are shell scripts");
            PlaytestTests.Fresh();
            var pose = PlaytestTests.Pose();
            var limit = InbetweenSettings.StepLimitSeconds;
            var window = ScriptableObject.CreateInstance<DemoWindow>();
            using var tools = new PlaytestTests.FakeTools(60, 0);
            try
            {
                InbetweenSettings.StepLimitSeconds = 2f;
                var recipe = PlaytestTests.PoseRecipe("Hung", pose);
                window.Use(recipe);
                Assert.IsTrue(window.Generate(), window.Message);
                var begun = EditorApplication.timeSinceStartup;
                yield return PlaytestTests.Until(() => !RecipeRunner.IsRunning(recipe), 30.0);
                var waited = EditorApplication.timeSinceStartup - begun;
                Assert.IsFalse(RecipeRunner.IsRunning(recipe), "the hung step was never stopped");
                Assert.Less(waited, 20.0, "stopping took too long");
                StringAssert.Contains("took longer than 2 seconds", RecipeRunner.Get(recipe).Error);
                window.Refresh();
                Assert.AreEqual(MessageType.Error, window.Kind, window.Message);
                StringAssert.Contains("took longer than 2 seconds", window.Message);
                StringAssert.Contains("Nothing was written", window.Message);
                Assert.IsNull(recipe.Output);
                Assert.IsFalse(File.Exists(RecipeRunner.OutputPath(recipe)));
                var pid = tools.Pid("generate");
                yield return PlaytestTests.Until(() => !PlaytestTests.Alive(pid), 5.0);
                Assert.IsFalse(PlaytestTests.Alive(pid), "the generator process was left running");
                Assert.IsTrue(window.CanGenerate(), "Generate must be pressable again");
            }
            finally
            {
                InbetweenSettings.StepLimitSeconds = limit;
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(pose);
                AssetDatabase.DeleteAsset(PlaytestTests.Folder);
            }
        }

        [UnityTest]
        public IEnumerator The_demo_backup_puts_the_finished_result_back_in_one_click()
        {
            if (!FakeToolsRun) Assert.Ignore("the fake tools are shell scripts");
            if (AssetDatabase.IsValidFolder(DemoScene.Folder) || AssetDatabase.IsValidFolder(DemoBackup.GenerationDemoFolder) || Directory.Exists(DemoBackup.Root))
                Assert.Ignore("this project already has a demo or a demo backup; this test would replace it");
            using var tools = new PlaytestTests.FakeTools(0, 0);
            var window = ScriptableObject.CreateInstance<DemoWindow>();
            try
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(DemoScene.Folder));
                var pose = PlaytestTests.Pose();
                AssetDatabase.CreateAsset(pose, DemoScene.Folder + "/Pose.asset");
                var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
                recipe.Start = new ClipFrame { Kind = SourceKind.Pose, Pose = pose };
                recipe.Goals.Clear();
                recipe.Goals.Add(new ClipFrame { Kind = SourceKind.Pose, Pose = pose });
                AssetDatabase.CreateAsset(recipe, DemoScene.RecipePath);
                Assert.IsTrue(RecipeRunner.Start(recipe, out var error), error);
                yield return PlaytestTests.Until(() => !RecipeRunner.IsRunning(recipe), 30.0);
                Assert.AreEqual(RecipeState.UpToDate, RecipeRunner.StateOf(recipe), RecipeRunner.Get(recipe).Error);
                var result = File.ReadAllBytes(RecipeRunner.OutputPath(recipe));

                Assert.IsTrue(DemoBackup.TrySave(out var report), report);
                Assert.IsTrue(DemoBackup.Exists);
                StringAssert.StartsWith("Backup of the finished demo", DemoBackup.Describe());

                AssetDatabase.DeleteAsset(RecipeRunner.OutputPath(recipe));
                recipe.DurationSeconds = 1.5f;
                EditorUtility.SetDirty(recipe);
                AssetDatabase.CreateAsset(new AnimationClip(), DemoScene.Folder + "/Stray.anim");
                AssetDatabase.SaveAssets();
                Assert.AreNotEqual(RecipeState.UpToDate, RecipeRunner.StateOf(recipe));

                Assert.IsTrue(window.UseBackup(), window.Message);
                var restored = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(DemoScene.RecipePath);
                var outputLine = "";
                foreach (var line in File.ReadAllLines(DemoScene.RecipePath)) if (line.Contains("Output:")) outputLine = line.Trim();
                var outputPath = DemoScene.Folder + "/" + DemoScene.RecipeName + ".saclip";
                Assert.IsTrue(restored.Output != null, window.Message + "\nrecipe file: " + outputLine + "\nresult guid now: " + AssetDatabase.AssetPathToGUID(outputPath) + " loads: " + (AssetDatabase.LoadAssetAtPath<SaClipAsset>(outputPath) != null));
                Assert.AreEqual(RecipeState.UpToDate, RecipeRunner.StateOf(restored), window.Message);
                Assert.AreEqual(result, File.ReadAllBytes(RecipeRunner.OutputPath(restored)), "the result is not the one that was backed up");
                Assert.Less(restored.DurationSeconds, 1.5f, "the recipe settings were not put back");
                Assert.IsFalse(File.Exists(DemoScene.Folder + "/Stray.anim"), "a file made after the backup was left");
                Assert.AreSame(restored, window.Recipe);
                StringAssert.Contains("Up to date", window.Message);
            }
            finally
            {
                Object.DestroyImmediate(window);
                AssetDatabase.DeleteAsset(DemoScene.Folder);
                if (Directory.Exists(DemoBackup.Root)) Directory.Delete(DemoBackup.Root, true);
            }
        }

        [Test, Category("RealAssets")]
        public void Making_the_demo_scene_again_keeps_the_recipe_name_so_saved_files_keep_theirs()
        {
            if (!Application.isBatchMode) Assert.Ignore("replaces the open scene twice; run it in batch mode");
            var picks = DemoScene.Find();
            if (!picks.Ready) Assert.Ignore("no Humanoid clip and character in this project: " + picks.Problem);
            if (AssetDatabase.IsValidFolder(DemoScene.Folder)) Assert.Ignore(DemoScene.Folder + " already exists; this test would overwrite it");
            try
            {
                Assert.IsNotNull(DemoScene.Create(picks, NewSceneMode.Single, out _));
                var again = DemoScene.Create(DemoScene.Find(), NewSceneMode.Single, out _);
                Assert.AreEqual(DemoScene.RecipePath, AssetDatabase.GetAssetPath(again), "the second demo scene must reuse the recipe");
                Assert.AreEqual(AssetDatabase.AssetPathToGUID(DemoScene.RecipePath), AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(again)));
                Assert.AreEqual(DemoScene.RecipeName, again.name);
                Assert.AreEqual(DemoScene.Folder + "/" + DemoScene.RecipeName + ".saclip", RecipeRunner.OutputPath(again));
                Assert.AreEqual(DemoScene.Folder + "/" + DemoScene.RecipeName + ".anim", SaClipExport.ExportPath(again));
                Assert.AreEqual(DemoScene.Folder + "/" + DemoScene.RecipeName + "_Humanoid.anim", SaClipHumanoidExport.ExportPath(again));
                Assert.AreEqual(DemoScene.Folder + "/" + DemoScene.RecipeName + ".fbx", SaClipFbxExport.ExportPath(again));
                Assert.IsTrue(File.ReadAllText(DemoScene.RecipePath).Contains("m_Name: " + DemoScene.RecipeName), "the saved recipe lost its name");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(DemoScene.Folder);
            }
        }

        [UnityTest, Category("RealAssets")]
        public IEnumerator Play_in_SA_runtime_sets_up_one_SA_character_in_a_SubScene_and_plays_the_bridge()
        {
            if (!Application.isBatchMode) Assert.Ignore("replaces the open scene and enters Play mode; run it in batch mode");
            var picks = DemoScene.Find();
            if (!picks.Ready) Assert.Ignore("no Humanoid clip and character in this project: " + picks.Problem);
            if (AssetDatabase.LoadAssetAtPath<SaClipAsset>(JumpResult) == null) Assert.Ignore(JumpResult + " missing");
            if (AssetDatabase.IsValidFolder(DemoScene.Folder)) Assert.Ignore(DemoScene.Folder + " already exists; this test would overwrite it");
            var saFolderExisted = AssetDatabase.IsValidFolder("Assets/SA");
            var materialsExisted = AssetDatabase.IsValidFolder(DemoBackup.SaMaterialsFolder);
            var problems = "";
            try
            {
                var recipe = DemoScene.Create(picks, NewSceneMode.Single, out _);
                var bridge = AssetDatabase.LoadAssetAtPath<SaClipAsset>(JumpResult);
                recipe.Output = bridge;
                Assert.IsTrue(DemoRuntime.TryPrepare(recipe, out var error), error);
                Assert.IsTrue(DemoRuntime.TryPrepare(recipe, out error), "pressing it again must reuse the SA character: " + error);
                var main = SceneManager.GetActiveScene();
                Assert.IsNotNull(DemoRuntime.Find(main));
                var subScenes = 0;
                foreach (var root in main.GetRootGameObjects()) subScenes += root.GetComponentsInChildren<SubScene>(true).Length;
                Assert.AreEqual(1, subScenes, "exactly one SubScene");
                Assert.IsFalse(main.isDirty, "the demo scene was left unsaved");

                var sub = EditorSceneManager.OpenScene(DemoRuntime.SubScenePath, OpenSceneMode.Additive);
                try
                {
                    GameObject character = null;
                    foreach (var root in sub.GetRootGameObjects())
                        if (root.name == DemoRuntime.CharacterName) character = root;
                    Assert.IsNotNull(character, "the SA character is not in the SubScene");
                    var rig = character.GetComponent<RigAuthoring>();
                    Assert.IsNotNull(rig);
                    Assert.AreEqual(Engine.Cpu, rig.Engine);
                    var shared = character.GetComponent<SharedFamilyAuthoring>();
                    Assert.IsNotNull(shared);
                    Assert.AreSame(rig, shared.Rig);
                    Assert.AreEqual(1, shared.Clips.Count);
                    Assert.IsTrue(shared.Clips[0].humanMotion);
                    Assert.Greater(character.GetComponentsInChildren<SkinAuthoring>(true).Length, 0, "no skinned mesh was converted, so the SA character would be invisible");
                    Assert.AreEqual(DemoRuntime.SaCharacterPlace, character.transform.position);
                }
                finally
                {
                    EditorSceneManager.CloseScene(sub, true);
                }

                RecipePreview.Request(bridge);
                yield return new EnterPlayMode();
                SaClipPlayer player = null;
                var begun = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - begun < 60f)
                {
                    player = Object.FindAnyObjectByType<SaClipPlayer>();
                    if (player != null && player.IsPlaying) break;
                    yield return null;
                }

                if (player == null) problems += "no SaClipPlayer was created in Play mode. ";
                else if (!player.IsPlaying) problems += "the player did not start: " + player.Status + ". ";
                var world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                {
                    using var query = world.EntityManager.CreateEntityQuery(typeof(SharedContribution));
                    var characters = query.CalculateEntityCount();
                    if (characters != 1) problems += characters + " shared-family characters in the world, expected 1. ";
                }
                else problems += "no default world in Play mode. ";

                yield return new ExitPlayMode();
                Assert.AreEqual("", problems);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(DemoScene.Folder);
                if (!materialsExisted) AssetDatabase.DeleteAsset(DemoBackup.SaMaterialsFolder);
                if (!saFolderExisted) AssetDatabase.DeleteAsset("Assets/SA");
            }
        }
    }
}
