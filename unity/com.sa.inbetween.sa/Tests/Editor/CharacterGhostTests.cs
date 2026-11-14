using NUnit.Framework;
using SA.Inbetween.Clips.Editor;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Tests
{
    public sealed class CharacterGhostTests
    {
        const string Character = "Assets/Synty/AnimationBaseLocomotion/Meshes/PolygonSyntyCharacter.fbx";
        const string Jump = "Assets/InbetweenDemo/MidAirJump.saclip";

        static int GreenPixels(Texture2D picture)
        {
            var count = 0;
            foreach (var pixel in picture.GetPixels32())
                if (pixel.g > pixel.r + 25 && pixel.g > pixel.b + 25) count++;
            return count;
        }

        static int Differing(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            var count = 0;
            for (var i = 0; i < pa.Length; i++)
                if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) > 24) count++;
            return count;
        }

        [Test, Category("RealAssets")]
        public void The_character_preview_shows_the_start_and_goal_poses_as_faded_ghosts_on_the_mesh()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(Character);
            var bridge = AssetDatabase.LoadAssetAtPath<SaClipAsset>(Jump);
            if (character == null || bridge == null) Assert.Ignore("Synty character or the MidAirJump result missing");
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            recipe.Start = new ClipFrame { Kind = SourceKind.SaClip, Clip = bridge, Where = FrameWhere.Start };
            recipe.Goals.Clear();
            recipe.Goals.Add(new ClipFrame { Kind = SourceKind.SaClip, Clip = bridge, Where = FrameWhere.End });
            recipe.Output = bridge;
            recipe.Character = character;
            var actor = new CharacterPreview();
            Texture2D plain = null, withGhosts = null;
            try
            {
                Assert.IsTrue(actor.Bind(character, bridge.Bytes), actor.Error);
                actor.ShowGhosts = false;
                actor.Snapshot(64, 64, 0f);
                plain = actor.Snapshot(480, 360, actor.Duration * 0.5f);
                actor.ShowGhosts = true;
                actor.GhostsFor(recipe);
                withGhosts = actor.Snapshot(480, 360, actor.Duration * 0.5f);
                var skins = character.GetComponentsInChildren<SkinnedMeshRenderer>(false).Length;
                Assert.AreEqual(2 * skins, actor.GhostMeshes, "one ghost mesh per skinned mesh for the start and for the goal");
                var changed = Differing(plain, withGhosts);
                var green = GreenPixels(withGhosts) - GreenPixels(plain);
                Assert.Greater(changed, 500, "the ghosts did not change the picture");
                Assert.Greater(green, 100, "the start ghost is not drawn in green");
                actor.ShowGhosts = false;
                var off = actor.Snapshot(480, 360, actor.Duration * 0.5f);
                Assert.Less(Differing(plain, off), 50, "turning the ghosts off must give the plain picture");
                Object.DestroyImmediate(off);
            }
            finally
            {
                actor.Dispose();
                if (plain != null) Object.DestroyImmediate(plain);
                if (withGhosts != null) Object.DestroyImmediate(withGhosts);
                Object.DestroyImmediate(recipe);
            }
        }
    }
}
