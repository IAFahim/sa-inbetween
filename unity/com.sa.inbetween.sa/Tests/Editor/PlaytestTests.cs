using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using SA.Editor;
using SA.Inbetween.Clips.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace SA.Inbetween.Clips.Tests
{
    public class PlaytestTests
    {
        internal const string Folder = "Assets/zz_playtest_test";

        static string restoreTools;

        [TearDown]
        public void RestoreTools()
        {
            if (restoreTools == null) return;
            InbetweenSettings.ToolsFolder = restoreTools;
            restoreTools = null;
            var root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "sa-fake-tools");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        internal sealed class FakeTools : IDisposable
        {
            public readonly string Root;

            public FakeTools(int generateSeconds, int bakeSeconds)
            {
                Root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "sa-fake-tools");
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
                Directory.CreateDirectory(Root);
                Script("sa-inbetween", "#!/bin/sh\nd=\"$(dirname \"$0\")\"\necho $$ > \"$d/generate.pid\"\nif [ -f \"$d/generate.slow\" ]; then sleep \"$(cat \"$d/generate.slow\")\"; fi\nexit 0\n");
                Script("sa-soma-bake", "#!/bin/sh\nd=\"$(dirname \"$0\")\"\nif [ \"$1\" = pose ]; then exit 3; fi\necho $$ > \"$d/bake.pid\"\nprintf partial > \"$3\"\nif [ -f \"$d/bake.slow\" ]; then sleep \"$(cat \"$d/bake.slow\")\"; fi\ncp \"$d/valid.saclip\" \"$3\"\nexit 0\n");
                File.WriteAllBytes(Path.Combine(Root, "mmm_v6_soma_fp32.onnx"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(Root, "humanoid-v1.safamily"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(Root, "soma_rest.pose"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(Root, "valid.saclip"), ValidClip());
                if (generateSeconds > 0) File.WriteAllText(Path.Combine(Root, "generate.slow"), generateSeconds.ToString());
                if (bakeSeconds > 0) File.WriteAllText(Path.Combine(Root, "bake.slow"), bakeSeconds.ToString());
                if (restoreTools == null) restoreTools = InbetweenSettings.ToolsFolder.Contains("sa-fake-tools") ? "" : InbetweenSettings.ToolsFolder;
                InbetweenSettings.ToolsFolder = Root;
            }

            void Script(string name, string body)
            {
                var path = Path.Combine(Root, name);
                File.WriteAllText(path, body);
                using var chmod = Process.Start(new ProcessStartInfo("chmod", "+x \"" + path + "\"") { UseShellExecute = false });
                chmod?.WaitForExit();
            }

            public int Pid(string stage)
            {
                var file = Path.Combine(Root, stage + ".pid");
                return File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var pid) ? pid : -1;
            }

            public void Dispose()
            {
                if (restoreTools != null) InbetweenSettings.ToolsFolder = restoreTools;
                restoreTools = null;
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
        }

        internal static byte[] ValidClip()
        {
            var reference = SharedFamilyBake.CanonicalReferencePoses();
            Assert.IsTrue(PoseCapture.TryEncode(reference, out var bytes, out var error), error);
            return bytes;
        }

        internal static SaPoseAsset Pose()
        {
            var pose = ScriptableObject.CreateInstance<SaPoseAsset>();
            pose.Values = new float[SaPoseAsset.FloatCount];
            for (var j = 0; j < SaPoseAsset.Joints; j++) pose.Values[j * SaPoseAsset.FloatsPerJoint + 6] = 1f;
            return pose;
        }

        internal static InbetweenRecipe PoseRecipe(string name, SaPoseAsset pose)
        {
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            recipe.Start = new ClipFrame { Kind = SourceKind.Pose, Pose = pose };
            recipe.Goals.Clear();
            recipe.Goals.Add(new ClipFrame { Kind = SourceKind.Pose, Pose = pose });
            AssetDatabase.CreateAsset(recipe, Folder + "/" + name + ".asset");
            return recipe;
        }

        internal static void Fresh()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
        }

        internal static bool Alive(int pid)
        {
            try
            {
                return pid > 0 && Directory.Exists("/proc/" + pid) && !File.ReadAllText("/proc/" + pid + "/stat").Contains(") Z ");
            }
            catch (IOException)
            {
                return false;
            }
        }

        internal static IEnumerator Until(Func<bool> done, double seconds)
        {
            var begun = EditorApplication.timeSinceStartup;
            while (!done() && EditorApplication.timeSinceStartup - begun < seconds) yield return null;
        }

        [Test]
        public void Failure_advice_names_the_fix()
        {
            Assert.AreEqual(FailureAdvice.ModelMismatch, FailureAdvice.ForExit("Generating the motion", 1, "error: model sha256 00ff does not match the pin 856419dc; use --skip-verify to override"));
            Assert.AreEqual(FailureAdvice.ModelUnreadable, FailureAdvice.ForExit("Generating the motion", 1, "error: cannot load model: OnnxLoad value 0"));
            Assert.AreEqual(FailureAdvice.RestPose, FailureAdvice.ForExit("Reading the start", 1, "error: rest pose must be 539 float32 values"));
            Assert.AreEqual(FailureAdvice.FamilyFile, FailureAdvice.ForExit("Baking the clip", 1, "error: family file: bad header"));
            StringAssert.Contains("Visual C++", FailureAdvice.ForExit("Generating the motion", -1073741515, ""));
            var unknown = FailureAdvice.ForExit("Baking the clip", 3, "starting\nerror: readback Truncated\n");
            StringAssert.Contains("readback Truncated", unknown);
            StringAssert.Contains("Copy diagnostics", unknown);
            StringAssert.Contains("Humanoid", FailureAdvice.ForConversion("Jump", "Bot", "required bones have no match on 'Bot' (Hips)."));
            StringAssert.Contains("Y = 0", FailureAdvice.ForConversion("Jump", "Bot", "hips height -1 is not positive; the character must stand on Y = 0 with +Y up."));
            Assert.AreEqual("c\nd", FailureAdvice.Tail("a\nb\n\nc\nd\n", 2));
        }

        [Test]
        public void Setup_advice_says_where_each_missing_file_goes()
        {
            var none = new StudioTools { Cli = "", Bake = "", Model = "", Family = "", Rest = "" };
            var all = InbetweenSettings.SetupAdvice(none);
            StringAssert.Contains("SaInbetween-tools", all);
            StringAssert.Contains("SaInbetween/model", all);
            StringAssert.Contains("soma_rest.pose", all);
            var model = new StudioTools { Cli = "c", Bake = "b", Model = "", Family = "f", Rest = "r" };
            var only = InbetweenSettings.SetupAdvice(model);
            StringAssert.Contains("757,864,273", only);
            StringAssert.DoesNotContain("SaInbetween-tools", only);
            Assert.AreEqual("", InbetweenSettings.SetupAdvice(new StudioTools { Cli = "c", Bake = "b", Model = "m", Family = "f", Rest = "r" }));
        }

        [Test]
        public void Diagnostics_list_versions_tools_and_the_recipe_without_the_home_folder()
        {
            Fresh();
            var pose = Pose();
            try
            {
                var recipe = PoseRecipe("Diag", pose);
                var text = InbetweenDiagnostics.Build(recipe);
                StringAssert.Contains(Application.unityVersion, text);
                StringAssert.Contains("com.sa.inbetween.sa: ", text);
                StringAssert.Contains("com.sa.unity: ", text);
                StringAssert.Contains("## Tools", text);
                StringAssert.Contains("## Recipe", text);
                StringAssert.Contains(Folder + "/Diag.asset", text);
                StringAssert.Contains("## Last error", text);
                StringAssert.DoesNotContain("(unavailable", text);
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home)) StringAssert.DoesNotContain(home + "/", text);
                Assert.AreEqual("~/x", InbetweenDiagnostics.Anonymous(home + "/x"));
                var file = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "sa-sha.txt");
                File.WriteAllText(file, "abc");
                Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", InbetweenDiagnostics.Sha256(file));
                File.Delete(file);
            }
            finally
            {
                Object.DestroyImmediate(pose);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [Test]
        public void A_frame_past_the_end_of_the_clip_is_named_with_the_last_valid_frame()
        {
            var motion = new AnimationClip { frameRate = 30f, name = "Hop" };
            motion.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var character = new GameObject("Bot");
            character.AddComponent<Animator>();
            var bare = new GameObject("NoAnimator");
            var r = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                Assert.AreEqual(1f, motion.length, 1e-4f);
                r.Start = new ClipFrame { Kind = SourceKind.UnityClip, Motion = motion, Character = character, Where = FrameWhere.Frame, Frame = 30 };
                Assert.AreEqual("", RecipeRunner.PastEnd(r.Start, "The start"));
                r.Start.Frame = 45;
                var past = RecipeRunner.Problem(r);
                StringAssert.Contains("past the end of 'Hop'", past);
                StringAssert.Contains("up to 30", past);
                r.Start.Where = FrameWhere.Time;
                r.Start.Seconds = 1.5f;
                StringAssert.Contains("1.00 s", RecipeRunner.Problem(r));
                r.Start.Where = FrameWhere.End;
                Assert.AreEqual("", RecipeRunner.PastEnd(r.Start, "The start"));
                r.Start.Character = bare;
                Assert.AreEqual("The goal has no clip.", RecipeRunner.Problem(r), "the start is fine: a Generic rig without an Animator is read by bone names");
                motion.legacy = true;
                StringAssert.Contains("Legacy clip", RecipeRunner.Problem(r));
                motion.legacy = false;
                var sixty = new AnimationClip { frameRate = 60f };
                sixty.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 12f, 1f));
                var late = new ClipFrame { Kind = SourceKind.UnityClip, Motion = sixty, Character = character, Where = FrameWhere.Frame, Frame = 600 };
                Assert.AreEqual(10f, late.AnchorSeconds, 1e-5f);
                Assert.AreEqual("", RecipeRunner.PastEnd(late, "The start"));
                Assert.AreEqual("9.9,9.9333,9.9667,10", RecipeRunner.Times(late, true));
                late.Frame = 721;
                StringAssert.Contains("up to 720", RecipeRunner.PastEnd(late, "The start"));
                Object.DestroyImmediate(sixty);
            }
            finally
            {
                Object.DestroyImmediate(r);
                Object.DestroyImmediate(bare);
                Object.DestroyImmediate(character);
                Object.DestroyImmediate(motion);
            }
        }

        [Test]
        public void Source_key_survives_moving_and_renaming_the_clip_but_not_editing_it()
        {
            Fresh();
            AssetDatabase.CreateFolder(Folder, "moved");
            var body = new GameObject("Body");
            try
            {
                body.AddComponent<Animator>();
                var character = PrefabUtility.SaveAsPrefabAsset(body, Folder + "/Body.prefab");
                var motion = new AnimationClip { name = "Walk" };
                motion.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                AssetDatabase.CreateAsset(motion, Folder + "/Walk.anim");
                var frame = new ClipFrame { Kind = SourceKind.UnityClip, Motion = motion, Character = character, Where = FrameWhere.End };
                var before = RecipeRunner.SourceKey(frame);
                Assert.AreEqual("", AssetDatabase.MoveAsset(Folder + "/Walk.anim", Folder + "/moved/Stroll.anim"));
                Assert.AreEqual("", AssetDatabase.MoveAsset(Folder + "/Body.prefab", Folder + "/moved/Body.prefab"));
                Assert.AreEqual(before, RecipeRunner.SourceKey(frame), "moving or renaming the source or the character must not make the recipe out of date");
                motion.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 2f));
                EditorUtility.SetDirty(motion);
                AssetDatabase.SaveAssets();
                Assert.AreNotEqual(before, RecipeRunner.SourceKey(frame), "editing the source clip must make the recipe out of date");
            }
            finally
            {
                Object.DestroyImmediate(body);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [Test]
        public void A_recipe_named_like_its_source_clip_or_character_refuses_to_overwrite_them()
        {
            Fresh();
            var body = new GameObject("Hero");
            try
            {
                body.AddComponent<Animator>();
                var character = PrefabUtility.SaveAsPrefabAsset(body, Folder + "/Hero.prefab");
                var motion = new AnimationClip { name = "Leap" };
                motion.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                AssetDatabase.CreateAsset(motion, Folder + "/Leap.anim");
                var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
                recipe.Start = new ClipFrame { Kind = SourceKind.UnityClip, Motion = motion, Character = character, Where = FrameWhere.End };
                recipe.Goals.Clear();
                recipe.Goals.Add(new ClipFrame { Kind = SourceKind.UnityClip, Motion = motion, Character = character, Where = FrameWhere.Start });
                AssetDatabase.CreateAsset(recipe, Folder + "/Leap.asset");
                StringAssert.Contains("overwrite that file", RecipeRunner.Problem(recipe));
                Assert.AreEqual(RecipeState.Incomplete, RecipeRunner.StateOf(recipe));
                Assert.AreEqual("", AssetDatabase.RenameAsset(Folder + "/Leap.asset", "Leap_bridge"));
                Assert.AreEqual("", RecipeRunner.Overwrites(recipe));
            }
            finally
            {
                Object.DestroyImmediate(body);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [UnityTest]
        public IEnumerator Renaming_a_recipe_takes_its_result_files_along()
        {
            Fresh();
            var pose = Pose();
            try
            {
                var recipe = PoseRecipe("Leap", pose);
                File.WriteAllBytes(Folder + "/Leap.saclip", ValidClip());
                AssetDatabase.ImportAsset(Folder + "/Leap.saclip");
                var other = new AnimationClip { name = "Leap" };
                AssetDatabase.CreateAsset(other, Folder + "/Leap.anim");
                recipe.Output = AssetDatabase.LoadAssetAtPath<SaClipAsset>(Folder + "/Leap.saclip");
                recipe.SavedClip = other;
                EditorUtility.SetDirty(recipe);
                AssetDatabase.SaveAssets();
                Assert.AreEqual("", AssetDatabase.RenameAsset(Folder + "/Leap.asset", "Bound"));
                yield return Until(() => File.Exists(Folder + "/Bound.saclip") && File.Exists(Folder + "/Bound.anim"), 5.0);
                Assert.IsTrue(File.Exists(Folder + "/Bound.saclip"), "the result clip did not follow the recipe");
                Assert.IsTrue(File.Exists(Folder + "/Bound.anim"), "the Unity clip did not follow the recipe");
                Assert.IsFalse(File.Exists(Folder + "/Leap.saclip"));
                Assert.AreEqual(Folder + "/Bound.saclip", AssetDatabase.GetAssetPath(recipe.Output));
                Assert.AreEqual(Folder + "/Bound.saclip", RecipeRunner.OutputPath(recipe));
            }
            finally
            {
                Object.DestroyImmediate(pose);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [UnityTest]
        public IEnumerator Generate_bakes_outside_Assets_and_one_undo_takes_it_back()
        {
            if (Application.platform != RuntimePlatform.LinuxEditor && Application.platform != RuntimePlatform.OSXEditor) Assert.Ignore("the fake tools are shell scripts");
            Fresh();
            var pose = Pose();
            using var tools = new FakeTools(0, 3);
            try
            {
                var recipe = PoseRecipe("Fake", pose);
                var output = RecipeRunner.OutputPath(recipe);
                Assert.IsTrue(RecipeRunner.Start(recipe, out var error), error);
                yield return Until(() => tools.Pid("bake") > 0, 20.0);
                Assert.Greater(tools.Pid("bake"), 0, "the bake step never started");
                Assert.IsFalse(File.Exists(output), "the baker writes into Assets while it runs");
                yield return Until(() => !RecipeRunner.IsRunning(recipe), 60.0);
                var run = RecipeRunner.Get(recipe);
                Assert.AreEqual("", run.Error, run.Log);
                Assert.IsTrue(File.Exists(output));
                Assert.IsNotNull(recipe.Output);
                Assert.AreEqual(RecipeState.UpToDate, RecipeRunner.StateOf(recipe));
                Assert.IsFalse(Directory.GetFiles(Folder, "*.partial", SearchOption.AllDirectories).Length > 0 || Directory.GetFiles(Folder, ".*partial").Length > 0);

                Undo.PerformUndo();
                yield return null;
                Assert.IsNull(recipe.Output, "undo kept the result");
                Assert.IsFalse(File.Exists(output), "undo kept the result file");
                Undo.PerformRedo();
                yield return null;
                Assert.IsTrue(File.Exists(output), "redo did not bring the file back");
                Assert.IsNotNull(recipe.Output, "redo did not bring the result back");
                Assert.AreEqual(RecipeState.UpToDate, RecipeRunner.StateOf(recipe));

                Assert.IsTrue(AssetDatabase.DeleteAsset(output));
                Assert.IsTrue(recipe.Output == null);
                Assert.AreEqual(RecipeState.NotGenerated, RecipeRunner.StateOf(recipe), "a deleted result must ask for Generate");
                Assert.IsTrue(RecipeRunner.Start(recipe, out error), error);
                yield return Until(() => !RecipeRunner.IsRunning(recipe), 60.0);
                Assert.AreEqual("", RecipeRunner.Get(recipe).Error);
                Assert.IsTrue(File.Exists(output), "generating again after deleting the result wrote nothing");
                Assert.AreEqual(RecipeState.UpToDate, RecipeRunner.StateOf(recipe));
            }
            finally
            {
                RecipeRunner.Cancel(AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(Folder + "/Fake.asset"));
                Object.DestroyImmediate(pose);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [UnityTest]
        public IEnumerator Cancel_stops_the_tool_and_leaves_nothing_in_Assets()
        {
            if (Application.platform != RuntimePlatform.LinuxEditor && Application.platform != RuntimePlatform.OSXEditor) Assert.Ignore("the fake tools are shell scripts");
            Fresh();
            var pose = Pose();
            using var tools = new FakeTools(0, 30);
            try
            {
                var recipe = PoseRecipe("Stop", pose);
                Assert.IsTrue(RecipeRunner.Start(recipe, out var error), error);
                yield return Until(() => tools.Pid("bake") > 0, 20.0);
                var pid = tools.Pid("bake");
                Assert.Greater(pid, 0);
                RecipeRunner.Cancel(recipe);
                yield return Until(() => !Alive(pid), 5.0);
                Assert.IsFalse(Alive(pid), "the baker still runs after Cancel");
                Assert.AreEqual(RecipeState.Failed, RecipeRunner.StateOf(recipe));
                StringAssert.StartsWith("Cancelled", RecipeRunner.Get(recipe).Error);
                Assert.IsFalse(File.Exists(RecipeRunner.OutputPath(recipe)), "a cancelled run left a clip in Assets");
                Assert.IsNull(recipe.Output);
            }
            finally
            {
                Object.DestroyImmediate(pose);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [UnityTest]
        public IEnumerator A_script_reload_while_generating_stops_the_tool_and_says_so_afterwards()
        {
            if (Application.platform != RuntimePlatform.LinuxEditor && Application.platform != RuntimePlatform.OSXEditor) Assert.Ignore("the fake tools are shell scripts");
            Fresh();
            var pose = Pose();
            using var tools = new FakeTools(30, 0);
            try
            {
                var recipe = PoseRecipe("Reload", pose);
                Assert.IsTrue(RecipeRunner.Start(recipe, out var error), error);
                yield return Until(() => tools.Pid("generate") > 0, 20.0);
                var pid = tools.Pid("generate");
                Assert.Greater(pid, 0);
                Assert.IsTrue(RecipeRunner.AnyRunning());
                RecipeRunner.Interrupt();
                RecipeRunner.ForgetAll();
                RecipeRunner.RestoreInterrupted();
                yield return Until(() => !Alive(pid), 5.0);
                Assert.IsFalse(Alive(pid), "the generator still runs after the reload");
                Assert.AreEqual(RecipeState.Failed, RecipeRunner.StateOf(recipe));
                Assert.AreEqual(RecipeRunner.InterruptedMessage, RecipeRunner.Get(recipe).Error);
                Assert.IsFalse(File.Exists(RecipeRunner.OutputPath(recipe)));
                Assert.IsFalse(RecipeRunner.AnyRunning());
                Assert.AreEqual("", SessionState.GetString(RecipeRunner.InterruptedKey, ""));
            }
            finally
            {
                Object.DestroyImmediate(pose);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [UnityTest, Category("RealAssets")]
        public IEnumerator Demo_scene_finds_a_jump_starts_mid_air_and_saves_playable_clips()
        {
            var picks = DemoScene.Find();
            if (!picks.Ready || picks.Start == null || picks.Start.name.IndexOf("jump", StringComparison.OrdinalIgnoreCase) < 0) Assert.Ignore("no Humanoid jump clip and character in this project: " + picks.Problem);
            Assert.IsTrue(picks.MidAir, "the jump never leaves the ground: " + picks.Height);
            Assert.Greater(picks.StartFrame, 0);
            Assert.LessOrEqual(picks.StartFrame / picks.Start.frameRate, picks.Start.length + 1e-3f);
            Assert.IsNotNull(picks.Character.GetComponentInChildren<SkinnedMeshRenderer>(true));
            Debug.Log("demo picks: " + DemoScene.Describe(picks));
            var existed = AssetDatabase.IsValidFolder(DemoScene.Folder);
            if (existed) Assert.Ignore(DemoScene.Folder + " already exists; this test would overwrite it");
            var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            var replace = active.path == "";
            if (replace && active.isDirty) Assert.Ignore("the open untitled scene has unsaved changes");
            UnityEngine.SceneManagement.Scene scene = default;
            try
            {
                var recipe = DemoScene.Create(picks, replace ? UnityEditor.SceneManagement.NewSceneMode.Single : UnityEditor.SceneManagement.NewSceneMode.Additive, out scene);
                Assert.IsNotNull(recipe);
                Assert.AreEqual(DemoScene.RecipePath, AssetDatabase.GetAssetPath(recipe));
                Assert.AreEqual("", RecipeRunner.Problem(recipe));
                Assert.IsTrue(File.Exists(DemoScene.ScenePath));
                var found = false;
                foreach (var root in scene.GetRootGameObjects()) found |= root.name == DemoScene.CharacterName;
                Assert.IsTrue(found, "the demo character is not in the scene");
                if (!InbetweenSettings.Tools().Complete) Assert.Ignore("inbetween tools missing; the scene and recipe were made");
                var window = ScriptableObject.CreateInstance<DemoWindow>();
                Assert.IsTrue(RecipeRunner.Start(recipe, out var error), error);
                yield return Until(() => !RecipeRunner.IsRunning(recipe), 300.0);
                Assert.AreEqual("", RecipeRunner.Get(recipe).Error, RecipeRunner.Get(recipe).Log);
                Assert.AreEqual(RecipeState.UpToDate, RecipeRunner.StateOf(recipe));
                Assert.IsTrue(DemoScene.TrySave(recipe, out var saved, out error), error);
                StringAssert.Contains(".anim", saved);
                Assert.IsTrue(File.Exists(SaClipHumanoidExport.ExportPath(recipe)));
                if (SaClipFbxExport.CanExport(out var fbxProblem)) Assert.IsTrue(File.Exists(SaClipFbxExport.ExportPath(recipe)), "Save did not write the FBX: " + saved);
                else StringAssert.Contains("FBX not written", saved, fbxProblem);
                Assert.IsTrue(File.Exists(DemoScene.Folder + "/" + DemoScene.ControllerName + ".controller"));
                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == DemoScene.CharacterName)
                        Assert.IsNotNull(root.GetComponentInChildren<Animator>(true).runtimeAnimatorController);
                Assert.IsFalse(scene.isDirty);
                Object.DestroyImmediate(window);
            }
            finally
            {
                if (scene.IsValid() && replace)
                    UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                else if (scene.IsValid())
                {
                    if (active.IsValid()) UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }

                if (!existed) AssetDatabase.DeleteAsset(DemoScene.Folder);
            }
        }
    }
}
