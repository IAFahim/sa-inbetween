using System;
using System.Collections.Generic;

namespace SA.Inbetween.Install
{
    public readonly struct PackageNeed
    {
        public readonly string Name;
        public readonly string Source;

        public PackageNeed(string name, string source)
        {
            Name = name;
            Source = source;
        }

        public string Request => Source.StartsWith("https://", StringComparison.Ordinal) || Source.StartsWith("git+", StringComparison.Ordinal) || Source.StartsWith("file:", StringComparison.Ordinal) ? Source : Name + "@" + Source;
    }

    public readonly struct DownloadPin
    {
        public readonly string Url;
        public readonly string Sha256;
        public readonly long Bytes;

        public DownloadPin(string url, string sha256, long bytes)
        {
            Url = url;
            Sha256 = sha256;
            Bytes = bytes;
        }
    }

    public static class InstallPlan
    {
        public const string Repository = "https://github.com/IAFahim/sa-inbetween";
        public const string MotionPackage = "https://github.com/IAFahim/sa-motion.git?path=packages/com.sa.motion.unity#e56b12fe81fb7166c2c8a0984025220e3c07bb48";
        public const string SaUnityPackage = "https://github.com/IAFahim/com.sa.unity.git#v0.3.7";
        public const string FbxVersion = "5.1.6";
        public const string NvidiaRevision = "54fc563aeaaf819a753d72cc9fdc6713989cdd43";
        public const string NvidiaMedia = "https://media.githubusercontent.com/media/NVlabs/motionbricks/" + NvidiaRevision + "/";
        public const string ModelAgreement = "https://www.nvidia.com/en-us/agreements/enterprise-software/nvidia-open-model-agreement/";
        public const string SampleLicence = "https://github.com/NVlabs/motionbricks/blob/" + NvidiaRevision + "/assets/motions/LICENSE.txt";
        public const string ModelFile = "mmm_v6_soma_fp32.onnx";
        public const string RestFile = "soma_rest.pose";
        public const string RestSourceFile = "combat_quickstep_ff_idle_180_R_002__A365.bvh";

        public static readonly DownloadPin Model = new DownloadPin(NvidiaMedia + "motionbricks/assets/onnx/soma/" + ModelFile,
            "856419dc72aeb14e591eebf4dd0ea05734a2506ddb00bcd39dd6dac2ad60f4fb", 757864273);

        public static readonly DownloadPin RestSource = new DownloadPin(NvidiaMedia + "assets/motions/soma/variations/" + RestSourceFile,
            "5e175607b1aad62a779b03fbfff7e37320a763529147d6e5b5d3aae3ff7138fa", 2241123);

        public static readonly string[] Siblings = { "com.sa.inbetween.sa", "com.sa.inbetween.sa.timeline" };

        public static string SiblingSource(string ownName, string ownSource, string sibling)
        {
            if (string.IsNullOrEmpty(ownSource)) return "";
            if (ownSource.StartsWith("file:", StringComparison.Ordinal))
            {
                var path = ownSource.Substring("file:".Length).TrimEnd('/', '\\');
                var cut = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
                return cut < 0 ? "" : "file:" + path.Substring(0, cut + 1) + sibling;
            }

            var hash = ownSource.IndexOf('#');
            var reference = hash >= 0 ? ownSource.Substring(hash) : "";
            var url = hash >= 0 ? ownSource.Substring(0, hash) : ownSource;
            var query = url.IndexOf("?path=", StringComparison.Ordinal);
            if (query < 0) return "";
            var path2 = url.Substring(query + "?path=".Length).TrimEnd('/');
            var slash = path2.LastIndexOf('/');
            var folder = slash < 0 ? "" : path2.Substring(0, slash + 1);
            return url.Substring(0, query) + "?path=" + folder + sibling + reference;
        }

        public static List<PackageNeed> Needs(string ownName, string ownSource)
        {
            var needs = new List<PackageNeed>
            {
                new PackageNeed("com.sa.motion.unity", MotionPackage),
                new PackageNeed("com.sa.unity", SaUnityPackage),
            };
            foreach (var sibling in Siblings)
            {
                var source = SiblingSource(ownName, ownSource, sibling);
                if (source != "") needs.Add(new PackageNeed(sibling, source));
            }

            needs.Add(new PackageNeed("com.unity.formats.fbx", FbxVersion));
            return needs;
        }

        public static string Platform(bool windows, bool linux) => windows ? "win-x64" : linux ? "linux-x64" : "";

        public static string ToolsArchive(string version, string platform) =>
            platform == "" ? "" : "SaInbetween-tools-v" + version + "-" + platform + (platform.StartsWith("win", StringComparison.Ordinal) ? ".zip" : ".tar.gz");

        public static string ReleaseBase(string version, string overrideBase) =>
            string.IsNullOrEmpty(overrideBase) ? Repository + "/releases/download/v" + version + "/" : overrideBase.EndsWith("/", StringComparison.Ordinal) ? overrideBase : overrideBase + "/";

        public static string HashFor(string sums, string file)
        {
            foreach (var raw in (sums ?? "").Split('\n'))
            {
                var line = raw.Trim();
                var space = line.IndexOf(' ');
                if (space <= 0) continue;
                var name = line.Substring(space).Trim().TrimStart('*');
                if (string.Equals(name, file, StringComparison.Ordinal)) return line.Substring(0, space).ToLowerInvariant();
            }

            return "";
        }
    }
}
