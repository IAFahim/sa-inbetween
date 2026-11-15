using System;
using System.Collections.Generic;
using UnityEngine;

namespace SA.Inbetween.Clips
{
    public enum ChainPieceKind
    {
        Block,
        InBetween,
    }

    [Serializable]
    public struct ChainPiece
    {
        public string Name;
        public ChainPieceKind Kind;
        public float Start;
        public float Duration;
        public float Speed;
        public SaClipAsset Clip;
        public string Digest;

        public float End => Start + Duration;
    }

    public sealed class SaChainAsset : ScriptableObject
    {
        public SaClipAsset Whole;
        public List<ChainPiece> Pieces = new List<ChainPiece>();
        public string Digest = "";

        public float Duration => Pieces.Count == 0 ? 0f : Pieces[Pieces.Count - 1].End - Pieces[0].Start;

        public int PieceAt(float seconds)
        {
            if (Pieces.Count == 0) return -1;
            var at = Pieces[0].Start + Mathf.Max(0f, seconds);
            for (var i = 0; i < Pieces.Count; i++)
                if (at < Pieces[i].End)
                    return i;
            return Pieces.Count - 1;
        }

        public int Next(int piece, bool loop)
        {
            if (Pieces.Count == 0) return -1;
            if (piece + 1 < Pieces.Count) return piece + 1;
            return loop ? 0 : -1;
        }
    }
}
