using System.Globalization;
using System.Linq;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Timeline.Editor
{
    [CustomEditor(typeof(ChainTrack))]
    public sealed class ChainTrackInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("One row, many blocks: put Source clips on this track (run, jump, land, ...). Every gap between two blocks is an in-between that is generated and baked; move, add or delete a block and the gaps follow. The Generated track plays the result.", MessageType.None);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Chain"), new GUIContent("Chain"), true);
            if (serializedObject.ApplyModifiedProperties())
            {
                GenPlanCache.Forget();
                ChainAuto.Forget();
            }

            var timeline = GenPlanCache.TimelineOf(target);
            if (timeline == null) return;
            var director = GenPlanCache.DirectorOf(timeline);
            var plan = GenPlanCache.For(timeline, director);
            var track = (ChainTrack)target;
            var gaps = ChainSync.Gaps(track);
            EditorGUILayout.LabelField("Chain", ChainSync.Blocks(track).Count + " blocks, " + gaps.Count + " gaps", EditorStyles.boldLabel);
            if (gaps.Count != track.GetClips().Count(c => c.asset is GenerateClip) && GUILayout.Button("Fill the gaps with in-betweens"))
            {
                ChainSync.Sync(timeline);
                GenPlanCache.Forget();
            }

            GenBakeGUI.Summary(plan);
            var running = GenBake.Running(timeline);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(running != null))
            {
                if (GUILayout.Button("Bake all")) Show(ChainCommands.BakeAll(timeline, false, out var error), error);
                if (GUILayout.Button("Regenerate all")) Show(ChainCommands.BakeAll(timeline, true, out var error), error);
            }

            if (GUILayout.Button("Reveal outputs")) ChainCommands.Reveal(timeline, null);
            EditorGUILayout.EndHorizontal();
            if (running != null)
            {
                EditorGUILayout.HelpBox(running.Stage, MessageType.None);
                if (GUILayout.Button("Cancel bake")) GenBake.Cancel(running);
            }

            var last = GenBake.LastRun(timeline);
            if (last == null || !last.Done) return;
            foreach (var seam in last.Seams)
                EditorGUILayout.LabelField(seam.Before + " > " + seam.After, "height " + (seam.HeightStep * 100f).ToString("0.00", CultureInfo.InvariantCulture) + " cm, side " + (seam.SideStep * 100f).ToString("0.00", CultureInfo.InvariantCulture) + " cm, " + seam.Degrees.ToString("0.0", CultureInfo.InvariantCulture) + " deg", EditorStyles.miniLabel);
            foreach (var line in last.Report.Take(8))
                EditorGUILayout.HelpBox(line, last.Error == "" ? MessageType.Info : MessageType.Warning);
        }

        static void Show(GenBakeRun run, string error)
        {
            if (run == null && !string.IsNullOrEmpty(error)) EditorUtility.DisplayDialog(ChainCommands.Title, error, "OK");
        }
    }
}
