using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace SA.Inbetween.Clips
{
    public enum ChainPlayback
    {
        Whole,
        PieceByPiece,
    }

    [DisallowMultipleComponent]
    public sealed class SaChainPlayer : MonoBehaviour
    {
        [Tooltip("The chain a Chain Track bake wrote (<Timeline>_Chain.asset).")]
        public SaChainAsset Chain;

        [Tooltip("Whole: plays the chain's single .saclip. Piece By Piece: plays each block and in-between clip in turn; when one ends the next takes over (Next).")]
        public ChainPlayback Playback = ChainPlayback.Whole;

        public bool Loop = true;
        public bool PlayOnStart = true;
        public string Status = "Waiting for the character...";

        World world;
        Entity entity;
        int clipIndex = -1;
        int piece = -1;
        float elapsed;
        bool wanted;

        public int Piece => piece;

        public bool IsPlaying => clipIndex >= 0;

        void OnEnable() => wanted = PlayOnStart;

        void OnDisable() => Stop();

        public void Play() => wanted = true;

        public void Stop()
        {
            Detach();
            world = null;
            piece = -1;
            wanted = false;
            Status = "Stopped";
        }

        void Detach()
        {
            if (clipIndex >= 0 && world != null && world.IsCreated)
                SaClipRuntime.Detach(world.EntityManager, entity, clipIndex);
            clipIndex = -1;
        }

        void Update()
        {
            if (clipIndex >= 0)
            {
                if (Playback != ChainPlayback.PieceByPiece) return;
                elapsed += Time.deltaTime;
                if (elapsed < Chain.Pieces[piece].Duration) return;
                var next = Chain.Next(piece, Loop);
                Detach();
                if (next < 0)
                {
                    Status = "Finished " + Chain.name;
                    piece = -1;
                    return;
                }

                Attach(next, elapsed - Chain.Pieces[piece].Duration);
                return;
            }

            if (!wanted) return;
            if (Chain == null || (Playback == ChainPlayback.Whole ? Chain.Whole == null : Chain.Pieces.Count == 0))
            {
                Status = "No chain assigned (bake a Chain Track, then drop its _Chain asset here)";
                return;
            }

            var active = World.DefaultGameObjectInjectionWorld;
            if (active == null || !active.IsCreated)
            {
                Status = "Waiting for the animation world...";
                return;
            }

            var manager = active.EntityManager;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<SharedClipAsset>(), ComponentType.ReadOnly<SharedContribution>());
            var count = query.CalculateEntityCount();
            if (count != 1)
            {
                Status = count == 0 ? "Waiting for the character..." : "Found " + count + " shared-family characters; this player needs exactly one";
                return;
            }

            using var entities = query.ToEntityArray(Allocator.Temp);
            world = active;
            entity = entities[0];
            wanted = false;
            Attach(0, 0f);
        }

        void Attach(int index, float carry)
        {
            var whole = Playback == ChainPlayback.Whole;
            var clip = whole ? Chain.Whole : Chain.Pieces[index].Clip;
            if (clip == null || clip.Bytes.Length == 0)
            {
                Status = "Piece " + index + " has no clip; bake the chain again";
                return;
            }

            if (!SaClipRuntime.TryAttach(world.EntityManager, entity, clip.Bytes, whole && Loop, index == 0, out clipIndex, out var error))
            {
                Status = error;
                return;
            }

            if (!whole && Chain.Pieces[index].Speed > 0f) SaClipRuntime.SetSpeed(world.EntityManager, entity, clipIndex, Chain.Pieces[index].Speed);
            piece = index;
            elapsed = whole ? 0f : Mathf.Max(0f, carry);
            Status = "Playing " + (whole ? Chain.name : Chain.Pieces[index].Name);
        }
    }
}
