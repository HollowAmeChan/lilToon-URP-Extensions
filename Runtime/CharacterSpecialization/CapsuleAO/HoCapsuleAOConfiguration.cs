using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    public enum HoCapsuleAOMode { Capsule, Cross }
    public enum HoCapsuleAOBlendMode { Min, Multiply }

    // A value snapshot: render functions never retain a mutable Volume/profile.
    internal struct HoCapsuleAOConfiguration
    {
        internal HoCapsuleAOMode mode;
        internal HoCapsuleAOBlendMode blend;
        internal float strength, distance, crossLength, crossWidth, crossAngle, crossRotation, crossSoftness;
        internal int budget;

        internal static HoCapsuleAOConfiguration From(HoCharacterSpecializationEffects e) => new HoCapsuleAOConfiguration
        {
            mode = e.capsuleAOMode, blend = e.capsuleAOBlend,
            strength = Mathf.Clamp01(Safe(e.capsuleAOStrength, .75f)),
            distance = Mathf.Max(.001f, Safe(e.capsuleAOInfluenceDistance, .5f)),
            crossLength = Mathf.Max(.001f, Safe(e.capsuleAOCrossLength, .25f)),
            crossWidth = Mathf.Max(.001f, Safe(e.capsuleAOCrossWidth, .06f)),
            crossAngle = Mathf.Clamp(Safe(e.capsuleAOCrossAngle, 90), 0, 180),
            crossRotation = Safe(e.capsuleAOCrossRotation, 0),
            crossSoftness = Mathf.Max(.0001f, Safe(e.capsuleAOCrossSoftness, .02f)),
            budget = Mathf.Clamp(e.capsuleAOMaxCapsules, 1, HoCapsuleAOPass.MaxCapsules)
        };

        private static float Safe(float x, float fallback) => float.IsFinite(x) ? x : fallback;
    }
}
