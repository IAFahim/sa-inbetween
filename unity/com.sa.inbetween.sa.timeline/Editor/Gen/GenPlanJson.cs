using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Timeline.Editor
{
    public static class GenPlanJson
    {
        public const int Schema = 1;

        public static string Write(GenPlan plan, bool withRecipeHashes, out string digest)
        {
            var json = new StringBuilder();
            json.Append("{\n");
            Key(json, 1, "schema").Append(Schema).Append(",\n");
            Key(json, 1, "name").Append(Str(plan.Timeline != null ? plan.Timeline.name : "")).Append(",\n");
            Key(json, 1, "tickRate").Append(GenRules.TicksPerSecond).Append(",\n");
            Key(json, 1, "duration").Append(plan.Duration).Append(",\n");
            if (plan.Loop) Key(json, 1, "loop").Append("true,\n");
            if (plan.Next != null) Key(json, 1, "next").Append(Str(plan.Next.name)).Append(",\n");
            Key(json, 1, "tracks").Append('[');
            var tracks = plan.Clips.GroupBy(c => c.TrackIndex).OrderBy(g => g.Key).ToList();
            for (var t = 0; t < tracks.Count; t++)
            {
                var track = tracks[t].First().Track;
                json.Append(t == 0 ? "\n" : ",\n");
                Indent(json, 2).Append("{ \"name\": ").Append(Str(track.name)).Append(", \"namespace\": ").Append(Str(typeof(GenTrack).Namespace)).Append(", \"type\": ").Append(Str(nameof(GenTrack))).Append(", \"clips\": [");
                var clips = tracks[t].OrderBy(c => c.Order).ToList();
                for (var c = 0; c < clips.Count; c++)
                {
                    var frame = clips[c];
                    var type = frame.Asset != null ? frame.Asset.GetType() : null;
                    json.Append(c == 0 ? "\n" : ",\n");
                    Indent(json, 3).Append("{ \"name\": ").Append(Str(frame.Clip.displayName))
                        .Append(", \"namespace\": ").Append(Str(type != null ? type.Namespace ?? "" : ""))
                        .Append(", \"type\": ").Append(Str(type != null ? type.Name : "Missing"))
                        .Append(", \"start\": ").Append(frame.Span.Start)
                        .Append(", \"end\": ").Append(frame.Span.End);
                    if (frame.Clip.clipIn > 0) json.Append(", \"clipIn\": ").Append(Num(frame.Clip.clipIn));
                    if (Math.Abs(frame.Clip.timeScale - 1.0) > 1e-9) json.Append(", \"timeScale\": ").Append(Num(frame.Clip.timeScale));
                    json.Append(", \"data\": ").Append(Payload(frame.Asset)).Append(" }");
                }

                json.Append(clips.Count == 0 ? "] }" : "\n" + new string(' ', 4) + "] }");
            }

            json.Append(tracks.Count == 0 ? "],\n" : "\n  ],\n");
            Key(json, 1, "generations").Append('[');
            var jobs = plan.Jobs.OrderBy(j => j.Name, StringComparer.Ordinal).ToList();
            for (var j = 0; j < jobs.Count; j++)
            {
                json.Append(j == 0 ? "\n" : ",\n");
                Job(json, plan, jobs[j], withRecipeHashes);
            }

            json.Append(jobs.Count == 0 ? "],\n" : "\n  ],\n");
            Key(json, 1, "faults").Append('[');
            for (var f = 0; f < plan.Faults.Count; f++)
                json.Append(f == 0 ? "\n" : ",\n").Append(new string(' ', 4)).Append(Str(plan.Faults[f].Message));
            json.Append(plan.Faults.Count == 0 ? "],\n" : "\n  ],\n");
            var body = json.ToString();
            digest = Digest(body);
            return body + "  \"digest\": " + Str(digest) + "\n}\n";
        }

        public static string Digest(string text)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return "sha256:" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        static void Job(StringBuilder json, GenPlan plan, GenJob job, bool withRecipeHashes)
        {
            Indent(json, 2).Append("{\n");
            Key(json, 3, "name").Append(Str(job.Name)).Append(",\n");
            Key(json, 3, "clip").Append(Str(job.Frame.Clip.displayName)).Append(",\n");
            Key(json, 3, "start").Append(job.Span.Start).Append(",\n");
            Key(json, 3, "end").Append(job.Span.End).Append(",\n");
            Key(json, 3, "seconds").Append(Num(job.WindowSeconds)).Append(",\n");
            Key(json, 3, "moves").Append(job.Moves).Append(",\n");
            Key(json, 3, "moveSeconds").Append(Num(job.MoveSeconds)).Append(",\n");
            Key(json, 3, "from").Append(Frame(job.From)).Append(",\n");
            Key(json, 3, "goals").Append('[').Append(string.Join(", ", job.Goals.Select(Frame))).Append("],\n");
            Key(json, 3, "landExactly").Append(job.Payload.LandExactly ? "true" : "false").Append(",\n");
            Key(json, 3, "keep").Append(Str(job.Payload.Keep.ToString())).Append(",\n");
            Key(json, 3, "variants").Append('[').Append(string.Join(", ", job.Variants.Select(v => Str(v.ToString())))).Append("],\n");
            Key(json, 3, "character").Append(Asset(job.Character)).Append(",\n");
            Key(json, 3, "rig").Append(Str(job.Rig.ToString())).Append(",\n");
            Key(json, 3, "footIK").Append(job.FootIK ? "true" : "false").Append(",\n");
            Key(json, 3, "export").Append("{ \"anim\": ").Append(Bool(job.Export.Anim)).Append(", \"humanoidAnim\": ").Append(Bool(job.Export.HumanoidAnim))
                .Append(", \"fbx\": ").Append(Bool(job.Export.Fbx)).Append(", \"placeOnTimeline\": ").Append(Bool(job.Export.PlaceOnTimeline)).Append(" },\n");
            Key(json, 3, "labels").Append('[').Append(string.Join(", ", job.Labels.Select(Str))).Append("],\n");
            Key(json, 3, "recipes").Append('[');
            for (var i = 0; i < job.Variants.Count; i++)
            {
                var path = GenRecipes.RecipePath(plan.Timeline, job, job.Variants[i]);
                json.Append(i == 0 ? "" : ", ").Append("{ \"variant\": ").Append(Str(job.Variants[i].ToString())).Append(", \"asset\": ").Append(Str(path));
                if (withRecipeHashes) json.Append(", \"hash\": ").Append(Str(Hash(job, job.Variants[i])));
                json.Append(" }");
            }

            json.Append("]\n");
            Indent(json, 2).Append('}');
        }

        public static string Hash(GenJob job, GenVariant variant)
        {
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                GenRecipes.Configure(recipe, job, variant);
                return RecipeRunner.Hash(recipe);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(recipe);
            }
        }

        static string Frame(ClipFrame frame)
        {
            var sb = new StringBuilder("{ \"kind\": ").Append(Str(frame.Kind.ToString()));
            if (frame.Kind == SourceKind.Pose) return sb.Append(", \"pose\": ").Append(Asset(frame.Pose)).Append(" }").ToString();
            sb.Append(", \"where\": ").Append(Str(frame.Where.ToString()));
            if (frame.Where == FrameWhere.Time) sb.Append(", \"seconds\": ").Append(Num(frame.Seconds));
            if (frame.Where == FrameWhere.Frame || frame.Where == FrameWhere.Time) sb.Append(", \"frame\": ").Append(frame.Frame);
            if (frame.Kind == SourceKind.UnityClip) sb.Append(", \"motion\": ").Append(Asset(frame.Motion)).Append(", \"character\": ").Append(Asset(frame.Character));
            else sb.Append(", \"clip\": ").Append(Asset(frame.Clip));
            return sb.Append(" }").ToString();
        }

        static string Payload(GenClip clip)
        {
            if (clip == null) return "{}";
            var field = clip.GetType().GetField("Payload", BindingFlags.Instance | BindingFlags.Public);
            if (field == null) return "{}";
            return Struct(field.GetValue(clip));
        }

        static string Struct(object value)
        {
            var fields = value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => f.MetadataToken).ToList();
            var parts = new List<string>();
            foreach (var f in fields) parts.Add(Str(f.Name) + ": " + Value(f.GetValue(value)));
            return "{ " + string.Join(", ", parts) + (parts.Count == 0 ? "}" : " }");
        }

        static string Value(object value)
        {
            switch (value)
            {
                case null: return "null";
                case bool b: return Bool(b);
                case string s: return Str(s);
                case float f: return Num(f);
                case double d: return Num(d);
                case int i: return i.ToString(CultureInfo.InvariantCulture);
                case Enum e: return Str(e.ToString());
                case UnityEngine.Object o: return Asset(o);
                default:
                    return value.GetType().IsValueType && !value.GetType().IsPrimitive ? Struct(value) : Str(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        public static string AssetName(UnityEngine.Object asset)
        {
            if (asset == null) return "";
            var path = AssetDatabase.GetAssetPath(asset);
            if (path == "") return asset.name;
            return AssetDatabase.IsMainAsset(asset) ? path : path + ":" + asset.name;
        }

        static string Asset(UnityEngine.Object asset) => asset == null ? "null" : Str(AssetName(asset));

        static string Bool(bool value) => value ? "true" : "false";

        static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        static string Num(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        static StringBuilder Key(StringBuilder json, int depth, string key) => Indent(json, depth).Append(Str(key)).Append(": ");

        static StringBuilder Indent(StringBuilder json, int depth) => json.Append(' ', depth * 2);

        public static string Str(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
