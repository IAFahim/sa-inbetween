using System;
using UnityEngine;

namespace SA.Inbetween.Clips
{
    public enum SeamCurve
    {
        CriticallyDamped,
        Quintic,
    }

    [Serializable]
    public struct SeamSettings
    {
        public const float DefaultSeconds = 0.1f;

        [Tooltip("Critically Damped: the offset at a join dies away like a critically damped spring (Seconds is its half-life). Quintic: it reaches zero exactly after Seconds (Bollo's polynomial).")]
        public SeamCurve Curve;

        [Min(0f), Tooltip("How long a join takes to blend away. 0 turns inertialization off.")]
        public float Seconds;

        [Tooltip("Keep planted feet still (foot lock), keep soles above the ground (ground clamp) and blend in and out of contacts.")]
        public bool Feet;

        public static SeamSettings Default => new SeamSettings { Curve = SeamCurve.CriticallyDamped, Seconds = DefaultSeconds, Feet = true };

        public static SeamSettings Off => new SeamSettings { Curve = SeamCurve.CriticallyDamped, Seconds = 0f, Feet = false };

        public bool Blends => Seconds > 0f;

        public bool Active => Blends || Feet;

        public string Key => Curve + ":" + Seconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + (Feet ? "feet" : "nofeet");
    }
}
