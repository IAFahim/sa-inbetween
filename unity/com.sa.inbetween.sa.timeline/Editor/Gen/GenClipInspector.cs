using SA.Inbetween.Timeline.Gen;
using UnityEditor;

namespace SA.Inbetween.Timeline.Editor
{
    [CustomEditor(typeof(GenClip), true)]
    public sealed class GenClipInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var payload = serializedObject.FindProperty("Payload");
            if (payload != null)
            {
                var it = payload.Copy();
                var end = it.GetEndProperty();
                if (it.NextVisible(true))
                    do
                    {
                        if (SerializedProperty.EqualContents(it, end)) break;
                        EditorGUILayout.PropertyField(it, true);
                    }
                    while (it.NextVisible(false));
            }

            if (serializedObject.ApplyModifiedProperties()) GenPlanCache.Forget();
            var timeline = GenPlanCache.TimelineOf(target);
            if (timeline == null) return;
            var director = GenPlanCache.DirectorOf(timeline);
            var plan = GenPlanCache.For(timeline, director);
            var clip = Find(timeline);
            if (clip != null)
            {
                var job = plan.Jobs.Find(j => j.Frame.Clip == clip);
                if (job != null)
                    EditorGUILayout.HelpBox("Writes '" + job.Name + "': from " + job.FromText + " to " + string.Join(", then ", job.GoalTexts) + ", " + GenRules.Text(job.WindowSeconds) + " s" + (job.Moves > 1 ? " in " + job.Moves + " moves" : "") + (job.Character != null ? ", on " + job.Character.name : "") + ".", MessageType.None);
                GenBakeGUI.Faults(plan, new System.Collections.Generic.List<string>(plan.FaultsFor(clip)), new System.Collections.Generic.List<string>(plan.NotesFor(clip)));
            }

            GenBakeGUI.Bake(timeline, director, plan);
        }

        UnityEngine.Timeline.TimelineClip Find(UnityEngine.Timeline.TimelineAsset timeline)
        {
            foreach (var track in timeline.GetOutputTracks())
                foreach (var clip in track.GetClips())
                    if (clip.asset == target) return clip;
            return null;
        }
    }
}
