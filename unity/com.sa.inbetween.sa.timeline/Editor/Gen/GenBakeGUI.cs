using System.Linq;
using SA.Inbetween.Clips.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public static class GenBakeGUI
    {
        const int MaxFaults = 8;

        public static void Summary(GenPlan plan)
        {
            if (plan == null) return;
            if (plan.Jobs.Count == 0 && plan.Faults.Count == 0)
            {
                EditorGUILayout.HelpBox("No Generate clip yet. Right-click this track in the Timeline window and add a Source clip, a Generate clip and another Source clip, in that order.", MessageType.Info);
                return;
            }

            foreach (var job in plan.Jobs)
                EditorGUILayout.LabelField(job.Name, "from " + job.FromText + " to " + string.Join(", then ", job.GoalTexts) + ", " + GenRules.Text(job.WindowSeconds) + " s" + (job.Character != null ? ", on " + job.Character.name : ""), EditorStyles.wordWrappedMiniLabel);
            Faults(plan, plan.Faults.Select(f => f.Message).ToList(), plan.Notes.Select(n => n.Message).ToList());
        }

        public static void Faults(GenPlan plan, System.Collections.Generic.List<string> faults, System.Collections.Generic.List<string> notes)
        {
            foreach (var fault in faults.Take(MaxFaults)) EditorGUILayout.HelpBox(fault, MessageType.Error);
            if (faults.Count > MaxFaults) EditorGUILayout.HelpBox("and " + (faults.Count - MaxFaults) + " more problems.", MessageType.Error);
            foreach (var note in notes.Take(MaxFaults)) EditorGUILayout.HelpBox(note, MessageType.Info);
        }

        public static void Bake(TimelineAsset timeline, PlayableDirector director, GenPlan plan)
        {
            if (timeline == null || plan == null) return;
            EditorGUILayout.Space(4);
            var running = GenBake.Running(timeline);
            if (running != null)
            {
                EditorGUILayout.HelpBox(running.Stage, MessageType.None);
                if (GUILayout.Button("Cancel bake")) GenBake.Cancel(running);
                return;
            }

            var tools = InbetweenSettings.Tools();
            if (!tools.Complete)
            {
                EditorGUILayout.HelpBox("Inbetween needs its tools before it can bake. " + InbetweenSettings.SetupAdvice(tools), MessageType.Warning);
                if (GUILayout.Button("Open Project Settings")) SettingsService.OpenProjectSettings("Project/SA Inbetween");
            }

            using (new EditorGUI.DisabledScope(!plan.Ready || !tools.Complete))
                if (GUILayout.Button(new GUIContent("Bake Timeline", "Generate every Generate clip on this timeline and write its files, then put the results on the Generated track."), GUILayout.Height(26)))
                {
                    GenBake.Start(timeline, director, out var error);
                    if (error != "") EditorUtility.DisplayDialog("Bake Timeline", error, "OK");
                }

            var last = GenBake.LastRun(timeline);
            if (last != null && last.Done)
                foreach (var line in last.Report.Take(MaxFaults))
                    EditorGUILayout.HelpBox(line, last.Error == "" ? MessageType.Info : MessageType.Warning);
            var planPath = GenRecipes.PlanPath(timeline);
            var planFile = planPath == "" ? null : AssetDatabase.LoadMainAssetAtPath(planPath);
            if (planFile != null && GUILayout.Button("Show plan file")) EditorGUIUtility.PingObject(planFile);
        }
    }
}
