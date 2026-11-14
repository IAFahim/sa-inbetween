using System;
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
        static Vector3 Hips(TimelineAsset timeline, PlayableDirector sceneDirector, double time)
        {
            var bound = TimelineBridge.BoundAnimator(sceneDirector, GenOutputs.Track(timeline, false));
            var preview = EditorSceneManager.NewPreviewScene();
            var holder = new GameObject("Hips probe");
            SceneManager.MoveGameObjectToScene(holder, preview);
            var copy = UnityEngine.Object.Instantiate(bound.gameObject, holder.transform);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            var animator = copy.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var director = holder.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.playableAsset = timeline;
            foreach (var track in timeline.GetOutputTracks()) director.SetGenericBinding(track, animator);
            try
            {
                director.RebuildGraph();
                director.time = time;
                director.Evaluate();
                return TimelineBridge.HipsOf(animator).position;
            }
            finally
            {
                if (director.playableGraph.IsValid()) director.playableGraph.Destroy();
                UnityEngine.Object.DestroyImmediate(holder);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

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
                Assert.That(GenPlanCache.DirectorOf(timeline), Is.SameAs(director), "the bake menus must find the scene's director even when the Timeline window shows another one");
                var fbx = SaClipFbxExport.CanExport(out _);
                var export = (ExportClip)timeline.GetOutputTracks().OfType<GenTrack>().SelectMany(t => t.GetClips()).Single(c => c.asset is ExportClip).asset;
                Assert.That(export.Payload.Fbx, Is.EqualTo(fbx), "the demo's Export clip writes an FBX when the FBX Exporter can");
                Assert.That(export.Payload.PlaceSources, Is.True);
                for (var bake = 0; bake < 2; bake++)
                {
                    var run = GenBake.Start(timeline, director, out error);
                    Assert.That(run, Is.Not.Null, error);
                    var begun = EditorApplication.timeSinceStartup;
                    while (!run.Done && EditorApplication.timeSinceStartup - begun < 400.0) yield return null;
                    var report = string.Join("\n", run.Report);
                    Assert.That(run.Error, Is.Empty, report);
                    Assert.That(report, Does.Contain("hips matched at 2 seams"), report);
                    var job = run.Jobs.Single().Job;
                    var stem = GenRecipes.Folder(timeline) + "/" + job.Name;
                    if (fbx) Assert.That(AssetDatabase.LoadAllAssetsAtPath(stem + ".fbx").OfType<AnimationClip>().Any(c => !c.name.StartsWith("__preview__") && c.length > 0f), Is.True, "the FBX has no take: " + report);
                    var clips = GenOutputs.Track(timeline, false).GetClips().OrderBy(c => c.start).ToList();
                    var names = string.Join(", ", clips.Select(c => c.displayName + " [" + c.start.ToString("0.000") + ", " + c.end.ToString("0.000") + ")"));
                    Assert.That(clips.Count, Is.EqualTo(3), "bake " + (bake + 1) + ": " + names);
                    Assert.That(GenOutputs.IsSourceCopy(clips[0]) && !GenOutputs.IsSourceCopy(clips[1]) && GenOutputs.IsSourceCopy(clips[2]), Is.True, names);
                    var placed = clips[1];
                    Assert.That(placed.displayName, Is.EqualTo(job.Name));
                    Assert.That(((AnimationPlayableAsset)placed.asset).clip, Is.Not.Null);
                    Assert.That(placed.start, Is.EqualTo(job.StartSeconds).Within(1e-6));
                    Assert.That(clips[0].end, Is.EqualTo(placed.start).Within(1e-6), names);
                    Assert.That(clips[2].start, Is.EqualTo(placed.end).Within(1e-6), names);
                    foreach (var clip in clips)
                    {
                        Assert.That(((AnimationPlayableAsset)clip.asset).position.y, Is.EqualTo(0f), clip.displayName + " keeps its own height above the ground");
                        foreach (var other in clips.Where(o => o != clip))
                        {
                            var holdEnd = clip.extrapolatedStart + clip.extrapolatedDuration;
                            var overlap = Math.Min(holdEnd, other.end) - Math.Max(clip.extrapolatedStart, other.start);
                            Assert.That(overlap, Is.LessThan(1e-6), "bake " + (bake + 1) + ": '" + clip.displayName + "' also plays (held) under '" + other.displayName + "'");
                        }
                    }

                    foreach (var seam in new[] { placed.start, placed.end })
                    {
                        var before = Hips(timeline, director, seam - 1e-4);
                        var after = Hips(timeline, director, seam + 1e-4);
                        var across = new Vector2(after.x - before.x, after.z - before.z).magnitude;
                        Assert.That(across, Is.LessThan(0.01f), "hips jump " + across + " m sideways at " + seam + " s");
                        Assert.That(Mathf.Abs(after.y - before.y), Is.LessThan(0.10f), "hips jump " + (after.y - before.y) + " m up or down at " + seam + " s");
                    }
                }
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
