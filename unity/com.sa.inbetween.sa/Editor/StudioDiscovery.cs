using System.Collections.Generic;
using System.IO;

namespace SA.Inbetween.Clips.Editor
{
    public struct StudioTools
    {
        public string Cli;
        public string Bake;
        public string Model;
        public string Family;
        public string Library;

        public bool Complete => Cli != "" && Bake != "" && Model != "" && Family != "" && Library != "";

        public List<string> Missing()
        {
            var m = new List<string>();
            if (Cli == "") m.Add("sa-inbetween");
            if (Bake == "") m.Add("sa-soma-bake");
            if (Model == "") m.Add("mmm_v6_soma_fp32.onnx");
            if (Family == "") m.Add("a .safamily file");
            if (Library == "") m.Add("library.json");
            return m;
        }
    }

    public static class StudioDiscovery
    {
        const int MaxDepth = 4;

        public static StudioTools Find(string root)
        {
            var tools = new StudioTools { Cli = "", Bake = "", Model = "", Family = "", Library = "" };
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return tools;
            Walk(root, 0, ref tools);
            return tools;
        }

        static void Walk(string dir, int depth, ref StudioTools t)
        {
            string[] files;
            string[] subs;
            try
            {
                files = Directory.GetFiles(dir);
                subs = Directory.GetDirectories(dir);
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException)
            {
                return;
            }

            foreach (var f in files)
            {
                var n = Path.GetFileName(f);
                if (t.Cli == "" && (n == "sa-inbetween" || n == "sa-inbetween.exe")) t.Cli = f;
                else if (t.Cli == "" && n == "sa-inbetween.dll") t.Cli = f;
                if (t.Bake == "" && (n == "sa-soma-bake" || n == "sa-soma-bake.exe" || n == "SA.Soma.Bake.dll")) t.Bake = f;
                if (t.Model == "" && n == "mmm_v6_soma_fp32.onnx") t.Model = f;
                if (n.EndsWith(".safamily", System.StringComparison.Ordinal) && (t.Family == "" || n.StartsWith("humanoid-v1", System.StringComparison.Ordinal))) t.Family = f;
                if (t.Library == "" && n == "library.json") t.Library = f;
            }

            if (depth >= MaxDepth)
                return;
            foreach (var s in subs)
                Walk(s, depth + 1, ref t);
        }
    }
}
