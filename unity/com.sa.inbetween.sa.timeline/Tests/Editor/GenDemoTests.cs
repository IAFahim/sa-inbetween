using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Tests
{
    public sealed class GenDemoTests
    {
        [UnityTest, Category("RealAssets")]
        public IEnumerator The_generation_demo_lays_out_two_timelines_and_bakes_in_its_scene()
        {
            var picks = DemoScene.Find();
            if (!picks.Ready) Assert.Ignore("no Humanoid clip and character in this project: " + picks.Problem);
            var existed = AssetDatabase.IsValidFolder(GenDemo.Folder);
            if (existed) Assert.Ignore(GenDemo.Folder + " already exists; this test would overwrite it");
            var active = EditorSceneManager.GetActiveScene();
            var replace = active.path == "";
            if (replace && active.isDirty && !Application.isBatchMode) Assert.Ignore("the open untitled scene has unsaved changes");
            Scene scene = default;
            try
            {
                var timeline = GenDemo.Create(picks, replace ? NewSceneMode.Single : NewSceneMode.Additive, out scene, out var director, out var error);
                Assert.That(timeline, Is.Not.Null, error);
                Assert.That(File.Exists(GenDemo.ScenePath), Is.True);
                Assert.That(director.playableAsset, Is.SameAs(timeline));
                var animator = scene.GetRootGameObjects().Single(r => r.name == GenDemo.CharacterName).GetComponentInChildren<Animator>(true);
                foreach (var track in timeline.GetOutputTracks())
                    Assert.That(director.GetGenericBinding(track), Is.SameAs(animator), track.name);
                foreach (var name in new[] { GenDemo.Landing, GenDemo.Takes })
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TimelineAsset>(GenDemo.TimelinePath(name));
                    var plan = GenPlanner.Plan(asset, null);
                    Assert.That(plan.Faults, Is.Empty, string.Join("\n", plan.Faults.Select(f => f.Message)));
                    Assert.That(plan.Jobs.Single().Variants.Count, Is.EqualTo(name == GenDemo.Takes ? 3 : 1));
                }

                var withDirector = GenPlanner.Plan(timeline, director);
                Assert.That(AssetDatabase.GetAssetPath(withDirector.BoundCharacter(timeline.GetOutputTracks().OfType<GenTrack>().First())), Is.EqualTo(AssetDatabase.GetAssetPath(picks.Character)));
                if (!InbetweenSettings.Tools().Complete) Assert.Ignore("inbetween tools missing; the demo was laid out");
                var run = GenBake.Start(timeline, director, out error);
                Assert.That(run, Is.Not.Null, error);
                var begun = EditorApplication.timeSinceStartup;
                while (!run.Done && EditorApplication.timeSinceStartup - begun < 400.0) yield return null;
                Assert.That(run.Error, Is.Empty, string.Join("\n", run.Report));
                var placed = GenOutputs.Track(timeline, false).GetClips().Single();
                Assert.That(((AnimationPlayableAsset)placed.asset).clip, Is.Not.Null);
                Assert.That(placed.start, Is.EqualTo(run.Jobs.Single().Job.StartSeconds).Within(1e-6));
                director.time = placed.start + placed.duration * 0.5;
                director.Evaluate();
            }
            finally
            {
                if (scene.IsValid() && replace)
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else if (scene.IsValid())
                {
                    if (active.IsValid()) SceneManager.SetActiveScene(active);
                    EditorSceneManager.CloseScene(scene, true);
                }

                if (!existed) AssetDatabase.DeleteAsset(GenDemo.Folder);
            }
        }
    }
}
