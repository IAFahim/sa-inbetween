using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    [ScriptedImporter(1, "pose")]
    public sealed class SaPoseImporter : ScriptedImporter
    {
        public const float UnitTolerance = 1e-3f;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<SaPoseAsset>();
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            var bytes = File.ReadAllBytes(ctx.assetPath);
            asset.Problem = Parse(bytes, out var values);
            asset.Values = values;
            if (asset.Problem != "")
                ctx.LogImportError(ctx.assetPath + ": " + asset.Problem);
            ctx.AddObjectToAsset("pose", asset);
            ctx.SetMainObject(asset);
        }

        public static string Parse(byte[] bytes, out float[] values)
        {
            values = Array.Empty<float>();
            if (bytes == null || bytes.Length != SaPoseAsset.FloatCount * sizeof(float))
                return "a pose holds " + SaPoseAsset.FloatCount + " float32 values (" + SaPoseAsset.FloatCount * sizeof(float) + " bytes), this file has " + (bytes == null ? 0 : bytes.Length) + " bytes.";
            var parsed = new float[SaPoseAsset.FloatCount];
            Buffer.BlockCopy(bytes, 0, parsed, 0, bytes.Length);
            for (var i = 0; i < parsed.Length; i++)
                if (float.IsNaN(parsed[i]) || float.IsInfinity(parsed[i]))
                    return "value " + i + " is not finite.";
            for (var j = 0; j < SaPoseAsset.Joints; j++)
            {
                var at = j * SaPoseAsset.FloatsPerJoint + 3;
                var length = parsed[at] * parsed[at] + parsed[at + 1] * parsed[at + 1] + parsed[at + 2] * parsed[at + 2] + parsed[at + 3] * parsed[at + 3];
                if (Mathf.Abs(length - 1f) > UnitTolerance)
                    return "joint " + j + " rotation is not a unit quaternion.";
            }

            values = parsed;
            return "";
        }
    }
}
