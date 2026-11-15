using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SA.Animation;
using SA.Animation.Content;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Gen;
using SA.Motion.Numerics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using Pose = SA.Motion.Numerics.Pose;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class ChainSegment
    {
        public string Name = "";
        public ChainPieceKind Kind;
        public double Start;
        public double Duration;
        public double Speed = 1.0;
        public SaClipAsset Asset;
        public SharedClip Clip;

        public double End => Start + Duration;
    }

    public static class GenChain
    {
        public const string Suffix = "_Chain";
        public const float Rate = 30f;
        const double Gap = 2e-3;

        public static string Stem(TimelineAsset timeline, ChainTrack track)
        {
            var chains = ChainSync.Tracks(timeline).Count();
            var name = chains > 1 ? timeline.name + "_" + TimelineBridge.SafeName(track.name) : timeline.name;
            return GenRecipes.Folder(timeline) + "/" + TimelineBridge.SafeName(name) + Suffix;
        }

        public static IEnumerable<string> Write(GenBakeRun run)
        {
            var lines = new List<string>();
            run.Chains.Clear();
            foreach (var track in ChainSync.Tracks(run.Timeline))
            {
                if (track.mutedInHierarchy || !track.Chain.WholeChain) continue;
                if (!TrySegments(run, track, out var segments, out var problem))
                {
                    lines.Add("Chain '" + track.name + "': the whole chain was not written: " + problem);
                    continue;
                }

                var character = CharacterOf(run, track);
                if (!TryWrite(run, track, segments, character, out var chain, out var written, out problem))
                {
                    lines.Add("Chain '" + track.name + "': the whole chain was not written: " + problem);
                    continue;
                }

                run.Chains.Add(chain);
                lines.Add("Chain '" + track.name + "': " + segments.Count(s => s.Kind == ChainPieceKind.Block) + " blocks and " + segments.Count(s => s.Kind == ChainPieceKind.InBetween) +
                          " in-betweens as one clip, " + GenRules.Text(chain.Duration) + " s (" + string.Join(", ", written.Select(Path.GetFileName)) + ").");
            }

            return lines;
        }

        static GameObject CharacterOf(GenBakeRun run, ChainTrack track)
        {
            if (track.Chain.Character != null) return TimelineBridge.CharacterAsset(track.Chain.Character);
            var job = run.Jobs.FirstOrDefault(j => j.Job.Chain == track && j.Job.Character != null);
            return job != null ? job.Job.Character : run.Plan.BoundCharacter(track);
        }

        public static bool TrySegments(GenBakeRun run, ChainTrack track, out List<ChainSegment> segments, out string problem)
        {
            segments = new List<ChainSegment>();
            problem = "";
            foreach (var job in run.Jobs.Where(j => j.Job.Chain == track))
            {
                var output = job.Kept != null ? job.Kept.Output : null;
                if (job.Error != "" || job.Skipped || output == null)
                {
                    problem = "the in-between '" + job.Job.Name + "' is not baked yet.";
                    return false;
                }

                if (!CanonicalClip.TryLoad(output.Bytes, out var clip, out problem)) return false;
                var placed = GenOutputs.Track(run.Timeline, false)?.GetClips().FirstOrDefault(c => c.displayName == job.Job.Name);
                var duration = placed != null ? placed.duration : clip.Duration;
                var speed = placed != null && placed.timeScale > 0 ? placed.timeScale : 1.0;
                segments.Add(new ChainSegment { Name = job.Job.Name, Kind = ChainPieceKind.InBetween, Start = job.Job.StartSeconds, Duration = duration, Speed = speed, Asset = output, Clip = clip });
            }

            foreach (var piece in run.Pieces.Where(p => p.Chain == track))
            {
                if (!CanonicalClip.TryLoad(piece.Saclip.Bytes, out var clip, out problem)) return false;
                segments.Add(new ChainSegment { Name = piece.Source.Frame.Clip.displayName, Kind = ChainPieceKind.Block, Start = piece.Start, Duration = piece.Seconds, Asset = piece.Saclip, Clip = clip });
            }

            segments.Sort((a, b) => a.Start.CompareTo(b.Start));
            if (segments.Count == 0)
            {
                problem = "the track has no blocks to play (put Source clips on it).";
                return false;
            }

            for (var i = 1; i < segments.Count; i++)
                if (Math.Abs(segments[i].Start - segments[i - 1].End) > Gap)
                {
                    problem = "'" + segments[i - 1].Name + "' ends at " + GenRules.Text(segments[i - 1].End) + " s but '" + segments[i].Name + "' starts at " + GenRules.Text(segments[i].Start) + " s; bake the chain again so every gap is filled.";
                    return false;
                }

            return true;
        }

        public static RigidPose HipsWorld(SharedClip clip, double seconds)
        {
            var pose = CanonicalClip.Sample(clip, seconds);
            RigidMath.TryCompose(SaClipExport.RootAt(clip, Math.Max(0.0, Math.Min(clip.Duration, seconds))), new RigidPose(pose[SeamFix.Hips].Position, Quaternion.Normalize(pose[SeamFix.Hips].Rotation)), out var hips);
            return hips;
        }

        public static RigidPose GroundDelta(RigidPose to, RigidPose from)
        {
            var twist = Quaternion.Multiply(Quaternion.Normalize(to.Rotation), Quaternion.InverseUnit(Quaternion.Normalize(from.Rotation)));
            var forward = Quaternion.Rotate(twist, new Float3(0f, 0f, 1f));
            var yaw = forward.X * forward.X + forward.Z * forward.Z > 1e-8f ? (float)Math.Atan2(forward.X, forward.Z) : 0f;
            var turn = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), yaw);
            var moved = to.Position - Quaternion.Rotate(turn, from.Position);
            return new RigidPose(new Float3(moved.X, 0f, moved.Z), turn);
        }

        public static CanonicalFrames Concatenate(IReadOnlyList<ChainSegment> segments, float rate)
        {
            var start = segments[0].Start;
            var total = segments[segments.Count - 1].End - start;
            var count = Math.Max(2, (int)Math.Round(total * rate) + 1);
            var frames = new CanonicalFrames { Count = count, Duration = total, Loop = false, RootPolicy = RootPolicy.Planar, Poses = new Pose[count * SeamFix.Bones], Root = new RigidPose[count] };
            var place = new RigidPose[segments.Count];
            place[0] = RigidPose.Identity;
            for (var i = 1; i < segments.Count; i++)
            {
                RigidMath.TryCompose(place[i - 1], HipsWorld(segments[i - 1].Clip, segments[i - 1].Duration * segments[i - 1].Speed), out var leaving);
                place[i] = GroundDelta(leaving, HipsWorld(segments[i].Clip, 0.0));
            }

            var s = 0;
            for (var f = 0; f < count; f++)
            {
                var t = start + total * f / (count - 1);
                while (s + 1 < segments.Count && t >= segments[s + 1].Start - 1e-9) s++;
                var local = Math.Max(0.0, Math.Min(segments[s].Clip.Duration, (t - segments[s].Start) * segments[s].Speed));
                Array.Copy(CanonicalClip.Sample(segments[s].Clip, local), 0, frames.Poses, f * SeamFix.Bones, SeamFix.Bones);
                RigidMath.TryCompose(place[s], SaClipExport.RootAt(segments[s].Clip, local), out frames.Root[f]);
            }

            return frames;
        }

        static bool TryWrite(GenBakeRun run, ChainTrack track, List<ChainSegment> segments, GameObject character, out SaChainAsset chain, out List<string> written, out string problem)
        {
            chain = null;
            written = new List<string>();
            var stem = Stem(run.Timeline, track);
            var frames = Concatenate(segments, Rate);
            if (!CanonicalClip.TryEncode(frames, out var bytes, out problem)) return false;
            var clipPath = stem + ".saclip";
            var full = Path.GetFullPath(clipPath);
            if (!File.Exists(full) || !File.ReadAllBytes(full).SequenceEqual(bytes))
            {
                File.WriteAllBytes(full, bytes);
                AssetDatabase.ImportAsset(clipPath, ImportAssetOptions.ForceUpdate);
            }

            var whole = AssetDatabase.LoadAssetAtPath<SaClipAsset>(clipPath);
            if (whole == null)
            {
                problem = "Unity did not import " + clipPath;
                return false;
            }

            written.Add(clipPath);
            if (character != null)
            {
                if (track.Chain.WholeAnim)
                {
                    if (!SaClipExport.TryExport(whole, character, stem + ".anim", out _, out problem)) return false;
                    written.Add(stem + ".anim");
                }

                if (track.Chain.WholeHumanoidAnim && TimelineBridge.IsHumanoid(character))
                {
                    if (!SaClipHumanoidExport.TryBuild(whole, character, out var humanoid, out problem)) return false;
                    SaClipExport.SaveAs(humanoid, stem + "_Humanoid.anim");
                    written.Add(stem + "_Humanoid.anim");
                }

                if (track.Chain.WholeFbx)
                {
                    if (SaClipFbxExport.TryExport(whole, character, stem + ".fbx", out var fbxError)) written.Add(stem + ".fbx");
                    else run.Report.Add("Chain '" + track.name + "': the FBX was not written: " + fbxError);
                }
            }

            var path = stem + ".asset";
            chain = AssetDatabase.LoadAssetAtPath<SaChainAsset>(path);
            var fresh = chain == null;
            if (fresh) chain = ScriptableObject.CreateInstance<SaChainAsset>();
            chain.Whole = whole;
            chain.Pieces = segments.Select(s => new ChainPiece
            {
                Name = s.Name,
                Kind = s.Kind,
                Start = (float)(s.Start - segments[0].Start),
                Duration = (float)s.Duration,
                Speed = (float)s.Speed,
                Clip = s.Asset,
                Digest = Digest(s.Asset.Bytes),
            }).ToList();
            chain.Digest = Digest(System.Text.Encoding.UTF8.GetBytes(string.Join("|", chain.Pieces.Select(p => p.Kind + ":" + p.Name + ":" + p.Digest + ":" + p.Start.ToString("R", CultureInfo.InvariantCulture))) + "|" + Digest(bytes)));
            if (fresh) AssetDatabase.CreateAsset(chain, path);
            else EditorUtility.SetDirty(chain);
            AssetDatabase.SaveAssetIfDirty(chain);
            written.Add(path);
            return true;
        }

        public static string Digest(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return "sha256:" + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
