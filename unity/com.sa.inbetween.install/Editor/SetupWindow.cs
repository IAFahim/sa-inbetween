using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Install
{
    public sealed class SetupWindow : EditorWindow
    {
        const string Consent = "SA Inbetween generates motion with NVIDIA's MotionBricks model. It is not part of SA Inbetween and is never shipped with it.\n\n" +
                               "This downloads two files from NVIDIA's own GitHub repository (NVlabs/motionbricks) to this project's SaInbetween folder:\n" +
                               "1. " + InstallPlan.ModelFile + " (723 MB), under the NVIDIA Open Model Agreement.\n" +
                               "2. " + InstallPlan.RestSourceFile + " (2 MB), under the NVIDIA Sample Data Evaluation License; only its first frame is kept, as " + InstallPlan.RestFile + ".\n\n" +
                               "By choosing Accept and download you accept those terms. Read them first: Open the agreement.";

        Vector2 scroll;

        [MenuItem("SA/Inbetween/Setup", false, 1)]
        public static SetupWindow Open()
        {
            var window = GetWindow<SetupWindow>(false, "SA Inbetween Setup", true);
            window.minSize = new Vector2(460, 360);
            window.Show();
            return window;
        }

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("SA Inbetween " + InbetweenSetup.Version, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Everything SA Inbetween needs, from one Git URL. Fix all does what is left; each line has its own Fix.", MessageType.None);
            foreach (var step in InbetweenSetup.Check()) Row(step);
            EditorGUILayout.Space(6);
            if (InbetweenSetup.Running)
            {
                EditorGUILayout.HelpBox(InbetweenSetup.Busy == "" ? "Working..." : InbetweenSetup.Busy + "...", MessageType.Info);
                if (GUILayout.Button("Cancel")) InbetweenSetup.Cancel();
            }
            else
            {
                using (new EditorGUI.DisabledScope(InbetweenSetup.Complete))
                    if (GUILayout.Button("Fix all", GUILayout.Height(28)))
                        FixAll();
            }

            if (InbetweenSetup.LastError != "") EditorGUILayout.HelpBox(InbetweenSetup.LastError, MessageType.Error);
            if (InbetweenSetup.Complete) EditorGUILayout.HelpBox("Ready. Try SA > Inbetween > Create Demo Scene, or SA > Inbetween > Create Chain Demo.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        public static void FixAll()
        {
            if (!InbetweenSetup.NvidiaAccepted && !Ask()) return;
            InbetweenSetup.FixAll();
        }

        static bool Ask()
        {
            var choice = EditorUtility.DisplayDialogComplex("NVIDIA files", Consent, "Accept and download", "Not now", "Open the agreement");
            if (choice == 2)
            {
                Application.OpenURL(InstallPlan.ModelAgreement);
                Application.OpenURL(InstallPlan.SampleLicence);
                return false;
            }

            if (choice != 0) return false;
            InbetweenSetup.AcceptNvidia(true);
            return true;
        }

        static void Row(SetupStep step)
        {
            EditorGUILayout.BeginHorizontal();
            var old = GUI.color;
            GUI.color = step.Done ? new Color(0.4f, 0.8f, 0.4f) : step.State == SetupState.Failed ? new Color(0.95f, 0.4f, 0.35f) : new Color(0.95f, 0.75f, 0.35f);
            GUILayout.Label(step.State == SetupState.Optional ? "optional" : step.Done ? "ready" : step.State == SetupState.NeedsConsent ? "needs ok" : step.State.ToString().ToLowerInvariant(), GUILayout.Width(64));
            GUI.color = old;
            EditorGUILayout.LabelField(step.Name, EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(step.State == SetupState.Done || InbetweenSetup.Running || !step.CanFix()))
                if (GUILayout.Button("Fix", GUILayout.Width(48)))
                    Fix(step);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(step.Detail, EditorStyles.wordWrappedMiniLabel);
        }

        static void Fix(SetupStep step)
        {
            switch (step.Name)
            {
                case "Packages":
                    InbetweenSetup.FixPackages();
                    break;
                case "Tools":
                    InbetweenSetup.FixTools();
                    break;
                case "Motion model (NVIDIA)":
                    if (InbetweenSetup.NvidiaAccepted || Ask()) InbetweenSetup.FixModel();
                    break;
                case "Rest pose (NVIDIA sample)":
                    if (InbetweenSetup.NvidiaAccepted || Ask()) InbetweenSetup.FixRest();
                    break;
                case "Render pipeline":
                    InbetweenSetup.FixPipeline();
                    break;
                default:
                    EditorUtility.DisplayDialog(InbetweenSetup.Title, InbetweenSetup.FixLibXml2(), "OK");
                    break;
            }
        }
    }
}
