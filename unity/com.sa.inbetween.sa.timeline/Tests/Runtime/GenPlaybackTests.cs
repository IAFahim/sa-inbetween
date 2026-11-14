using System.Collections;
using NUnit.Framework;
using SA.Inbetween.Timeline.Gen;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.PlayTests
{
    public sealed class GenPlaybackTests
    {
        TimelineAsset timeline;
        AnimationClip motion;
        GameObject character;
        GameObject bystander;
        GameObject holder;

        static Transform Rig(GameObject root)
        {
            root.AddComponent<Animator>();
            var hips = new GameObject("Hips").transform;
            hips.SetParent(root.transform, false);
            return hips;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(holder);
            Object.Destroy(character);
            Object.Destroy(bystander);
            Object.Destroy(timeline);
            Object.Destroy(motion);
        }

        [UnityTest]
        public IEnumerator Generation_tracks_play_as_nothing_and_the_generated_track_moves_the_character()
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var steps = timeline.CreateTrack<GenTrack>(null, "Steps");
            var source = steps.CreateClip<SourceClip>();
            source.start = 0;
            source.duration = 1;
            var generate = steps.CreateClip<GenerateClip>();
            generate.start = 1;
            generate.duration = 1;
            var bake = timeline.CreateTrack<GenTrack>(null, "Bake");
            bake.CreateClip<RetargetClip>().duration = 2;
            bake.CreateClip<ExportClip>().duration = 2;
            var generated = timeline.CreateTrack<AnimationTrack>(null, "Generated");
            motion = new AnimationClip { name = "Bridge", frameRate = 30f };
            motion.SetCurve("Hips", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 2, 4));
            var placed = generated.CreateClip(motion);
            placed.start = 0;
            placed.duration = 2;

            character = new GameObject("Character");
            var hips = Rig(character);
            bystander = new GameObject("Bystander");
            var still = Rig(bystander);
            holder = new GameObject("Director");
            var director = holder.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.playableAsset = timeline;
            director.SetGenericBinding(generated, character.GetComponent<Animator>());
            director.SetGenericBinding(steps, bystander.GetComponent<Animator>());
            director.SetGenericBinding(bake, bystander.GetComponent<Animator>());

            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.RebuildGraph();
            director.time = 1.5;
            director.Evaluate();
            Assert.That(hips.localPosition.x, Is.EqualTo(3f).Within(0.05f));
            Assert.That(still.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(generate.asset, Is.InstanceOf<GenClip>());

            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            director.time = 0;
            director.Play();
            var begun = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - begun < 0.5f) yield return null;
            Assert.That(director.state, Is.EqualTo(PlayState.Playing));
            Assert.That(hips.localPosition.x, Is.GreaterThan(0.2f));
            Assert.That(still.localPosition, Is.EqualTo(Vector3.zero));
        }
    }
}
