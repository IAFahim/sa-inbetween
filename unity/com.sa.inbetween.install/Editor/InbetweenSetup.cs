using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Rendering;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SA.Inbetween.Install
{
    public enum SetupState
    {
        Done,
        Missing,
        Working,
        Failed,
        NeedsConsent,
        Optional,
    }

    public sealed class SetupStep
    {
        public string Name = "";
        public SetupState State;
        public string Detail = "";
        public Func<bool> CanFix = () => true;

        public bool Done => State == SetupState.Done || State == SetupState.Optional;
    }

    [Serializable]
    sealed class SetupConsent
    {
        public bool NvidiaAccepted;
        public string Agreement = "";
    }

    [InitializeOnLoad]
    public static class InbetweenSetup
    {
        public const string Title = "SA Inbetween setup";
        public const string BatchKey = "SA.Inbetween.setup.batch";
        public const string AutoKey = "SA.Inbetween.setup.auto";
        public const string PackagesKey = "SA.Inbetween.setup.packages";
        public const string AcceptVariable = "SA_INBETWEEN_ACCEPT_NVIDIA";
        public const string ReleaseVariable = "SA_INBETWEEN_RELEASE_URL";
        public const string ModelVariable = "SA_INBETWEEN_MODEL_URL";
        public const string RestVariable = "SA_INBETWEEN_REST_SOURCE_URL";

        static AddAndRemoveRequest adding;
        static readonly Queue<Action> Work = new Queue<Action>();
        static Download downloading;
        static Action<Download> afterDownload;
        static string busy = "";
        static string lastError = "";
        static int progress = -1;

        public static string Busy => busy;

        public static string LastError => lastError;

        public static bool Running => busy != "" || adding != null || downloading != null || Work.Count > 0;

        static InbetweenSetup()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.delayCall += FirstLook;
            if (Application.isBatchMode && SessionState.GetBool(BatchKey, false)) EditorApplication.update += WaitThenFinishBatch;
        }

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public static string ToolsFolder => Path.Combine(ProjectRoot, "SaInbetween");

        public static string DownloadFolder => Path.Combine(ProjectRoot, "Library", "SaInbetweenDownloads");

        static string ConsentFile => Path.Combine(ProjectRoot, "UserSettings", "SaInbetweenSetup.json");

        public static PackageInfo Own => PackageInfo.FindForAssembly(typeof(InbetweenSetup).Assembly);

        public static string Version => Own != null ? Own.version : "0.0.0";

        public static string OwnSource
        {
            get
            {
                var own = Own;
                if (own == null) return "";
                var at = own.packageId.IndexOf('@');
                return at < 0 ? "" : own.packageId.Substring(at + 1);
            }
        }

        public static bool NvidiaAccepted
        {
            get
            {
                if (Environment.GetEnvironmentVariable(AcceptVariable) == "1") return true;
                try
                {
                    return File.Exists(ConsentFile) && JsonUtility.FromJson<SetupConsent>(File.ReadAllText(ConsentFile)).NvidiaAccepted;
                }
                catch (Exception e) when (e is IOException || e is ArgumentException)
                {
                    return false;
                }
            }
        }

        public static void AcceptNvidia(bool accepted)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConsentFile) ?? ProjectRoot);
            File.WriteAllText(ConsentFile, JsonUtility.ToJson(new SetupConsent { NvidiaAccepted = accepted, Agreement = InstallPlan.ModelAgreement }, true));
        }

        public static string Manifest => Path.Combine(ProjectRoot, "Packages", "manifest.json");

        public static List<PackageNeed> MissingPackages()
        {
            var text = File.Exists(Manifest) ? File.ReadAllText(Manifest) : "";
            return InstallPlan.Needs(Own != null ? Own.name : "com.sa.inbetween.install", OwnSource).Where(n => !text.Contains("\"" + n.Name + "\"")).ToList();
        }

        public static string Platform => InstallPlan.Platform(Application.platform == RuntimePlatform.WindowsEditor, Application.platform == RuntimePlatform.LinuxEditor);

        public static bool HasTools()
        {
            var cli = Path.Combine(ToolsFolder, "cli");
            var bake = Path.Combine(ToolsFolder, "bake");
            var exe = Application.platform == RuntimePlatform.WindowsEditor ? ".exe" : "";
            return File.Exists(Path.Combine(cli, "sa-inbetween" + exe)) && File.Exists(Path.Combine(bake, "sa-soma-bake" + exe)) && File.Exists(Path.Combine(ToolsFolder, "humanoid-v1.safamily"));
        }

        public static string ModelPath => Path.Combine(ToolsFolder, "model", InstallPlan.ModelFile);

        public static string RestPath => Path.Combine(ToolsFolder, InstallPlan.RestFile);

        public static bool HasModel() => File.Exists(ModelPath) && new FileInfo(ModelPath).Length == InstallPlan.Model.Bytes;

        public static bool HasRest() => File.Exists(RestPath) && new FileInfo(RestPath).Length == SomaRestPose.Bytes;

        public static bool HasPipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline != null || GraphicsSettings.currentRenderPipeline != null) return true;
            for (var level = 0; level < QualitySettings.names.Length; level++)
                if (QualitySettings.GetRenderPipelineAssetAt(level) != null)
                    return true;
            return false;
        }

        public static List<SetupStep> Check()
        {
            var steps = new List<SetupStep>();
            var missing = MissingPackages();
            steps.Add(new SetupStep
            {
                Name = "Packages",
                State = adding != null ? SetupState.Working : missing.Count == 0 ? SetupState.Done : SetupState.Missing,
                Detail = missing.Count == 0 ? "SA Inbetween, SA Animation, SA Motion and the FBX Exporter are in Packages/manifest.json." : "Adds " + string.Join(", ", missing.Select(m => m.Name)) + " to Packages/manifest.json (Package Manager cannot follow Git dependencies by itself).",
            });
            var platform = Platform;
            steps.Add(new SetupStep
            {
                Name = "Tools",
                State = HasTools() ? SetupState.Done : platform == "" ? SetupState.Failed : SetupState.Missing,
                Detail = HasTools() ? "The generator, the baker and humanoid-v1.safamily are in SaInbetween/." : platform == "" ? "There are no tools for this system (Windows and Linux only)." : "Downloads " + InstallPlan.ToolsArchive(Version, platform) + " from the v" + Version + " release, checks its SHA-256 against SHA256SUMS and unpacks it into SaInbetween/ next to Assets.",
                CanFix = () => platform != "",
            });
            var consent = NvidiaAccepted;
            steps.Add(new SetupStep
            {
                Name = "Motion model (NVIDIA)",
                State = HasModel() ? SetupState.Done : consent ? SetupState.Missing : SetupState.NeedsConsent,
                Detail = HasModel() ? InstallPlan.ModelFile + " is in SaInbetween/model." : "Downloads NVIDIA's MotionBricks model (" + InstallPlan.Model.Bytes / (1024 * 1024) + " MB) from NVIDIA's own repository and checks its SHA-256. It is under the NVIDIA Open Model Agreement; it is never shipped with SA Inbetween.",
            });
            steps.Add(new SetupStep
            {
                Name = "Rest pose (NVIDIA sample)",
                State = HasRest() ? SetupState.Done : consent ? SetupState.Missing : SetupState.NeedsConsent,
                Detail = HasRest() ? InstallPlan.RestFile + " is in SaInbetween." : "Makes " + InstallPlan.RestFile + " on this computer from frame 0 of NVIDIA's SOMA sample " + InstallPlan.RestSourceFile + " (downloaded from NVIDIA's repository, NVIDIA Sample Data Evaluation License). Nothing of it is shipped.",
            });
            steps.Add(new SetupStep
            {
                Name = "Render pipeline",
                State = HasPipeline() ? SetupState.Done : SetupState.Missing,
                Detail = HasPipeline() ? "A Scriptable Render Pipeline is set, so SA characters in SubScenes are drawn." : "SA characters in SubScenes are drawn only by URP or HDRP. Fix makes a URP asset and sets it in Graphics and Quality settings.",
            });
            if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                var fbx = LibXml2Found();
                steps.Add(new SetupStep
                {
                    Name = "FBX export on Linux (optional)",
                    State = fbx ? SetupState.Done : SetupState.Optional,
                    Detail = fbx ? "libxml2.so.2 is found, so Export > FBX works." : "Export > FBX needs libxml2.so.2. Fix makes the link ~/.local/lib/unity-libxml2/libxml2.so.2; then start Unity with LD_LIBRARY_PATH=~/.local/lib/unity-libxml2 (docs/install.md). Everything else works without it.",
                });
            }

            return steps;
        }

        static bool LibXml2Found()
        {
            var places = new List<string> { "/usr/lib", "/usr/lib64", "/usr/lib/x86_64-linux-gnu", "/lib/x86_64-linux-gnu" };
            places.AddRange((Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "").Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries));
            return places.Any(p => File.Exists(Path.Combine(p, "libxml2.so.2")));
        }

        public static bool Complete => Check().All(s => s.Done);

        static void FirstLook()
        {
            if (Application.isBatchMode && Environment.GetEnvironmentVariable("SA_INBETWEEN_AUTOSETUP") != "1") return;
            if (SessionState.GetBool(AutoKey, false)) return;
            SessionState.SetBool(AutoKey, true);
            if (MissingPackages().Count > 0)
            {
                if (SessionState.GetBool(PackagesKey, false)) return;
                SessionState.SetBool(PackagesKey, true);
                FixPackages();
                return;
            }

            if (!HasTools() && Platform != "") Work.Enqueue(FixTools);
            if ((!HasModel() || !HasRest()) && NvidiaAccepted)
            {
                Work.Enqueue(FixModel);
                Work.Enqueue(FixRest);
            }

            if (!Application.isBatchMode && !Complete) EditorApplication.delayCall += () => SetupWindow.Open();
        }

        public static void FixAll()
        {
            if (MissingPackages().Count > 0)
            {
                SessionState.SetBool(AutoKey, false);
                SessionState.SetBool(PackagesKey, true);
                FixPackages();
                return;
            }

            if (!HasTools()) Work.Enqueue(FixTools);
            if (NvidiaAccepted)
            {
                if (!HasModel()) Work.Enqueue(FixModel);
                if (!HasRest()) Work.Enqueue(FixRest);
            }

            if (!HasPipeline()) Work.Enqueue(FixPipeline);
        }

        public static void FixPackages()
        {
            var missing = MissingPackages();
            if (missing.Count == 0 || adding != null) return;
            busy = "Adding " + string.Join(", ", missing.Select(m => m.Name));
            Debug.Log("SA Inbetween setup: adding to Packages/manifest.json: " + string.Join(", ", missing.Select(m => m.Request)));
            adding = Client.AddAndRemove(missing.Select(m => m.Request).ToArray(), null);
        }

        public static void FixTools()
        {
            var platform = Platform;
            if (platform == "" || HasTools()) return;
            var version = Version;
            var baseUrl = InstallPlan.ReleaseBase(version, Environment.GetEnvironmentVariable(ReleaseVariable));
            var archive = InstallPlan.ToolsArchive(version, platform);
            var sumsFile = Path.Combine(DownloadFolder, "SHA256SUMS");
            Fetch("Checking the v" + version + " release", new Download(baseUrl + "SHA256SUMS", sumsFile), sums =>
            {
                var hash = InstallPlan.HashFor(File.ReadAllText(sumsFile), archive);
                if (hash == "")
                {
                    Fail("SHA256SUMS of v" + version + " does not list " + archive + ".");
                    return;
                }

                var file = Path.Combine(DownloadFolder, archive);
                Fetch("Downloading the tools", new Download(baseUrl + archive, file, hash), done =>
                {
                    var unpack = Path.Combine(DownloadFolder, "unpack");
                    if (Directory.Exists(unpack)) Directory.Delete(unpack, true);
                    ToolArchive.Extract(file, unpack);
                    var top = Path.Combine(unpack, "SaInbetween");
                    if (!Directory.Exists(top))
                    {
                        Fail(archive + " has no SaInbetween folder.");
                        return;
                    }

                    var copied = Copy(top, ToolsFolder);
                    ToolArchive.MakeExecutable(copied.Where(f => Path.GetFileName(f) == "sa-inbetween" || Path.GetFileName(f) == "sa-soma-bake" || Path.GetFileName(f) == "createdump").ToList());
                    Directory.Delete(unpack, true);
                    Debug.Log("SA Inbetween setup: tools v" + version + " unpacked into " + ToolsFolder + " (" + copied.Count + " files, SHA-256 " + hash + ").");
                });
            });
        }

        static List<string> Copy(string from, string to)
        {
            var copied = new List<string>();
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, file.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar, '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? to);
                File.Copy(file, target, true);
                copied.Add(target);
            }

            return copied;
        }

        public static void FixModel()
        {
            if (HasModel()) return;
            if (!NvidiaAccepted)
            {
                Fail("Accept NVIDIA's terms in SA > Inbetween > Setup first.");
                return;
            }

            var url = Environment.GetEnvironmentVariable(ModelVariable);
            var pin = InstallPlan.Model;
            var from = string.IsNullOrEmpty(url) ? pin.Url : url;
            Fetch("Downloading NVIDIA's motion model", new Download(from, ModelPath, pin.Sha256, pin.Bytes), done =>
                Debug.Log("SA Inbetween setup: " + InstallPlan.ModelFile + " downloaded from " + from + " and checked (SHA-256 " + pin.Sha256 + ")."));
        }

        public static void FixRest()
        {
            if (HasRest()) return;
            if (!NvidiaAccepted)
            {
                Fail("Accept NVIDIA's terms in SA > Inbetween > Setup first.");
                return;
            }

            var url = Environment.GetEnvironmentVariable(RestVariable);
            var pin = InstallPlan.RestSource;
            var file = Path.Combine(DownloadFolder, InstallPlan.RestSourceFile);
            Fetch("Making the rest pose", new Download(string.IsNullOrEmpty(url) ? pin.Url : url, file, pin.Sha256, pin.Bytes), done =>
            {
                if (!SomaRestPose.TryConvert(File.ReadAllText(file), 0, out var pose, out var error))
                {
                    Fail(error);
                    return;
                }

                Directory.CreateDirectory(ToolsFolder);
                File.WriteAllBytes(RestPath, pose);
                File.Delete(file);
                Debug.Log("SA Inbetween setup: " + InstallPlan.RestFile + " made from frame 0 of " + InstallPlan.RestSourceFile + " (" + pose.Length + " bytes), downloaded from " + done.Url + ".");
            });
        }

        public static void FixPipeline()
        {
            if (HasPipeline()) return;
            var assetType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            var dataType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");
            if (assetType == null || dataType == null)
            {
                Fail("The Universal Render Pipeline package is not installed. Make the project from the Universal 3D template, or add com.unity.render-pipelines.universal.");
                return;
            }

            const string folder = "Assets/SA Inbetween Settings";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "SA Inbetween Settings");
            var data = ScriptableObject.CreateInstance(dataType);
            AssetDatabase.CreateAsset(data, folder + "/SA URP Renderer.asset");
            var create = assetType.GetMethods().FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 1);
            var pipeline = create?.Invoke(null, new object[] { data }) as RenderPipelineAsset;
            if (pipeline == null)
            {
                Fail("Could not make a URP asset.");
                return;
            }

            AssetDatabase.CreateAsset(pipeline, folder + "/SA URP.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (var level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = pipeline;
            }

            AssetDatabase.SaveAssets();
        }

        public static string FixLibXml2()
        {
            var source = new[] { "/usr/lib/libxml2.so.16", "/usr/lib64/libxml2.so.16", "/usr/lib/x86_64-linux-gnu/libxml2.so.16" }.FirstOrDefault(File.Exists);
            if (source == null) return "No libxml2 found in /usr/lib to link to. Install libxml2 with your package manager.";
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "lib", "unity-libxml2");
            Directory.CreateDirectory(folder);
            var link = Path.Combine(folder, "libxml2.so.2");
            if (!File.Exists(link))
            {
                var info = new System.Diagnostics.ProcessStartInfo("ln") { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("-s");
                info.ArgumentList.Add(source);
                info.ArgumentList.Add(link);
                using var process = System.Diagnostics.Process.Start(info);
                process?.WaitForExit(10000);
            }

            return "Made " + link + ". Quit Unity and start it with LD_LIBRARY_PATH=" + folder + " (docs/install.md step 4).";
        }

        static void Fetch(string label, Download download, Action<Download> then)
        {
            busy = label;
            downloading = download;
            afterDownload = then;
            if (progress < 0) progress = Progress.Start("SA Inbetween setup", label, Progress.Options.None);
            Progress.RegisterCancelCallback(progress, () =>
            {
                Cancel();
                return true;
            });
            download.Start();
        }

        public static void Cancel()
        {
            downloading?.Cancel();
            downloading = null;
            Work.Clear();
            busy = "";
            lastError = "Cancelled.";
            EndProgress(Progress.Status.Canceled);
        }

        static void Fail(string error)
        {
            lastError = error;
            Debug.LogError("SA Inbetween setup: " + error);
            Work.Clear();
            busy = "";
            EndProgress(Progress.Status.Failed);
        }

        static void EndProgress(Progress.Status status)
        {
            if (progress >= 0 && Progress.Exists(progress)) Progress.Finish(progress, status);
            progress = -1;
        }

        static void Tick()
        {
            if (adding != null)
            {
                if (!adding.IsCompleted) return;
                if (adding.Status == StatusCode.Failure) Fail("Package Manager could not add the packages: " + adding.Error?.message);
                adding = null;
                busy = "";
                return;
            }

            if (downloading != null)
            {
                if (progress >= 0 && Progress.Exists(progress)) Progress.Report(progress, downloading.Progress, busy);
                if (!downloading.Poll()) return;
                var finished = downloading;
                var then = afterDownload;
                downloading = null;
                afterDownload = null;
                if (!finished.Ok)
                {
                    Fail(finished.Error);
                    return;
                }

                try
                {
                    then?.Invoke(finished);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException || e is FormatException)
                {
                    Fail(e.Message);
                    return;
                }

                if (downloading == null && Work.Count == 0)
                {
                    busy = "";
                    EndProgress(Progress.Status.Succeeded);
                }

                return;
            }

            if (Work.Count > 0)
            {
                lastError = "";
                var next = Work.Dequeue();
                try
                {
                    next();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Fail(e.Message);
                }

                if (downloading == null && Work.Count == 0)
                {
                    busy = "";
                    EndProgress(lastError == "" ? Progress.Status.Succeeded : Progress.Status.Failed);
                }
            }
        }

        public static void BatchFixAll()
        {
            SessionState.SetBool(BatchKey, true);
            if (Environment.GetEnvironmentVariable(AcceptVariable) == "1") AcceptNvidia(true);
            FixAll();
            EditorApplication.update -= WaitThenFinishBatch;
            EditorApplication.update += WaitThenFinishBatch;
        }

        static double settledAt;

        static void WaitThenFinishBatch()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Running)
            {
                settledAt = EditorApplication.timeSinceStartup;
                return;
            }

            if (EditorApplication.timeSinceStartup - settledAt < 3.0) return;
            if (lastError == "" && MissingPackages().Count == 0 && (!HasTools() || (NvidiaAccepted && (!HasModel() || !HasRest())) || !HasPipeline()))
            {
                FixAll();
                if (Running) return;
            }

            EditorApplication.update -= WaitThenFinishBatch;
            SessionState.SetBool(BatchKey, false);
            var steps = Check();
            foreach (var step in steps) Debug.Log("SA Inbetween setup: " + step.Name + ": " + step.State + " - " + step.Detail);
            var ok = steps.All(s => s.Done) && lastError == "";
            Debug.Log("SA Inbetween setup: " + (ok ? "complete" : "not complete: " + lastError));
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
