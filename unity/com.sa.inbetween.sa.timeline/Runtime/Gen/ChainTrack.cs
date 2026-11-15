using System;
using System.ComponentModel;
using SA.Inbetween.Clips;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Gen
{
    [Serializable]
    public struct ChainPayload
    {
        [Tooltip("Generate and bake the in-betweens on their own the first time they are missing and whenever a block moves, changes or is added or deleted. Only the gaps that changed are generated again.")]
        public bool AutoBake;

        [Tooltip("The model or prefab the motion is baked onto. Empty: the character bound to this track.")]
        public GameObject Character;

        [Tooltip("Which clips go on the Generated track. Auto: Humanoid clips for a Humanoid avatar, else Generic ones.")]
        public GenRig Rig;

        [Tooltip("New in-betweens keep their end pose exactly on the next block.")]
        public bool LandExactly;

        [Tooltip("How every join blends: the offset between a block and its in-between dies away (inertialization), and planted feet stay still.")]
        public SeamSettings Seams;

        [Tooltip("What each in-between writes next to its .saclip.")]
        public ExportPayload Export;

        [Tooltip("Also write the whole chain, blocks and in-betweens, as one .saclip (<Timeline>_Chain.saclip) and a chain asset that lists every piece.")]
        public bool WholeChain;

        [Tooltip("Also write the whole chain as a Generic .anim.")]
        public bool WholeAnim;

        [Tooltip("Also write the whole chain as a Humanoid .anim (Humanoid characters only).")]
        public bool WholeHumanoidAnim;

        [Tooltip("Also write the whole chain as an FBX take (needs the FBX Exporter package).")]
        public bool WholeFbx;

        public static ChainPayload Default => new ChainPayload
        {
            AutoBake = true,
            Rig = GenRig.Auto,
            LandExactly = true,
            Seams = SeamSettings.Default,
            Export = ExportPayload.Default,
            WholeChain = true,
            WholeAnim = false,
            WholeHumanoidAnim = true,
            WholeFbx = false,
        };
    }

    [DisplayName("SA Inbetween/Chain Track")]
    [TrackColor(0.85f, 0.55f, 0.2f)]
    [TrackClipType(typeof(GenClip))]
    [TrackBindingType(typeof(Animator))]
    public sealed class ChainTrack : GenTrack
    {
        public const string GapName = "In-between";

        public ChainPayload Chain = ChainPayload.Default;
    }
}
