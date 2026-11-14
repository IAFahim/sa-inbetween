using SA.Inbetween.Timeline.Gen;
using UnityEditor;

namespace SA.Inbetween.Timeline.Editor
{
    [CustomEditor(typeof(GenTrack))]
    public sealed class GenTrackInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var timeline = GenPlanCache.TimelineOf(target);
            if (timeline == null) return;
            var director = GenPlanCache.DirectorOf(timeline);
            var plan = GenPlanCache.For(timeline, director);
            EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);
            GenBakeGUI.Summary(plan);
            GenBakeGUI.Bake(timeline, director, plan);
        }
    }
}
