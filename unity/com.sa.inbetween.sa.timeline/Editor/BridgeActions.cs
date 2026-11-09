using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.Timeline.Actions;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    /// <summary>Right-click one clip (bridges to the next clip) or two neighbouring clips on an Animation Track.</summary>
    [MenuEntry("Add Inbetween bridge", MenuPriority.CustomClipActionSection.customClipAction)]
    sealed class AddBridgeAction : ClipAction
    {
        public override ActionValidity Validate(IEnumerable<TimelineClip> clips) =>
            BridgeActions.Pair(clips, out var a, out var b) && TimelineBridge.NeighbourProblem(a, b) == "" && !TimelineBridge.IsBridge(a) && !TimelineBridge.IsBridge(b)
                ? ActionValidity.Valid
                : ActionValidity.NotApplicable;

        public override bool Execute(IEnumerable<TimelineClip> clips)
        {
            BridgeActions.Pair(clips, out var a, out var b);
            return BridgeActions.Report("Add Inbetween bridge", TimelineBridge.Add(TimelineEditor.inspectedDirector, a, b, out var error), error);
        }
    }

    [MenuEntry("Regenerate Inbetween bridge", MenuPriority.CustomClipActionSection.customClipAction + 1)]
    sealed class RegenerateBridgeAction : ClipAction
    {
        public override ActionValidity Validate(IEnumerable<TimelineClip> clips)
        {
            var list = clips.ToList();
            return list.Count > 0 && list.All(TimelineBridge.IsBridge) ? ActionValidity.Valid : ActionValidity.NotApplicable;
        }

        public override bool Execute(IEnumerable<TimelineClip> clips)
        {
            var ok = true;
            foreach (var bridge in clips.ToList())
                ok &= BridgeActions.Report("Regenerate Inbetween bridge", TimelineBridge.Regenerate(TimelineEditor.inspectedDirector, bridge, out var error), error);
            return ok;
        }
    }

    static class BridgeActions
    {
        public static bool Pair(IEnumerable<TimelineClip> clips, out TimelineClip a, out TimelineClip b)
        {
            var list = clips.OrderBy(c => c.start).ToList();
            a = b = null;
            if (list.Count == 1) { a = list[0]; b = TimelineBridge.Next(a); }
            else if (list.Count == 2) { a = list[0]; b = list[1]; }
            return a != null && b != null;
        }

        public static bool Report(string title, BridgeJob job, string error)
        {
            if (job == null)
            {
                EditorUtility.DisplayDialog(title, error, "OK");
                return false;
            }

            return true;
        }

        [MenuItem("Tools/SA/Inbetween/Add Bridge Between Selected Timeline Clips", false, 40)]
        static void AddFromMenu()
        {
            Pair(TimelineEditor.selectedClips, out var a, out var b);
            Report("Add Inbetween bridge", TimelineBridge.Add(TimelineEditor.inspectedDirector, a, b, out var error), error);
        }

        [MenuItem("Tools/SA/Inbetween/Regenerate Selected Timeline Bridges", false, 41)]
        static void RegenerateFromMenu()
        {
            var bridges = TimelineEditor.selectedClips.Where(TimelineBridge.IsBridge).ToList();
            if (bridges.Count == 0)
            {
                EditorUtility.DisplayDialog("Regenerate Inbetween bridge", "Select a bridge clip in the Timeline window.", "OK");
                return;
            }

            foreach (var bridge in bridges)
                Report("Regenerate Inbetween bridge", TimelineBridge.Regenerate(TimelineEditor.inspectedDirector, bridge, out var error), error);
        }
    }
}
