using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    [ScriptedImporter(1, "saclip")]
    public sealed class SaClipImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<SaClipAsset>();
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            asset.Bytes = File.ReadAllBytes(ctx.assetPath);
            ctx.AddObjectToAsset("clip", asset);
            ctx.SetMainObject(asset);
        }
    }
}
