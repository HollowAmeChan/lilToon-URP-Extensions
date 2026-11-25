using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    // Values are the default composition order and are mirrored in the composition shader.
    public enum HoCharacterCompositionEffect
    {
        DropShadow = 0,
        FaceHairDiffuse = 1,
        EnhancedOutline = 2,
        SubjectOutline = 3,
        EyeReveal = 4,
        CapsuleAO = 5
    }

    public static class HoCharacterCompositionOrder
    {
        public const int Count = 6;
        private const int ColorCount = 5;
        public static Vector4 DefaultFirst => new Vector4(0, 1, 2, 3);
        public static Vector4 DefaultLast => new Vector4(4, 1, 0, 0);

        public static HoCharacterCompositionEffect[] CreateDefault()
        {
            return new[] { HoCharacterCompositionEffect.DropShadow, HoCharacterCompositionEffect.FaceHairDiffuse,
                HoCharacterCompositionEffect.EnhancedOutline, HoCharacterCompositionEffect.SubjectOutline,
                HoCharacterCompositionEffect.EyeReveal, HoCharacterCompositionEffect.CapsuleAO };
        }

        // Editor order includes the pre-opaque AO entry. The color ABI remains five effects.
        public static HoCharacterCompositionEffect[] Normalize(IReadOnlyList<HoCharacterCompositionEffect> order)
        {
            var result = new List<HoCharacterCompositionEffect>(Count);
            int used = 0;
            if (order != null)
                foreach (var effect in order)
                {
                    int id = (int)effect;
                    if (id < 0 || id >= Count || (used & (1 << id)) != 0) continue;
                    used |= 1 << id; result.Add(effect);
                }
            foreach (var effect in CreateDefault())
                if ((used & (1 << (int)effect)) == 0) { used |= 1 << (int)effect; result.Add(effect); }
            return result.ToArray();
        }

        // Pure, allocation-free normalization: keep valid unique entries, then append missing
        // effects in default order. Old/partial profiles and unknown enum values remain usable.
        public static void Pack(IReadOnlyList<HoCharacterCompositionEffect> order, out Vector4 first, out Vector4 last)
        {
            first = Vector4.zero;
            last = DefaultLast;
            int used = 0, count = 0;
            if (order != null)
                for (int i = 0; i < order.Count && count < ColorCount; i++)
                    Append((int)order[i], ref used, ref count, ref first, ref last);
            for (int i = 0; i < ColorCount; i++) Append(i, ref used, ref count, ref first, ref last);
        }

        private static void Append(int effect, ref int used, ref int count, ref Vector4 first, ref Vector4 last)
        {
            if (effect < 0 || effect >= ColorCount || (used & (1 << effect)) != 0) return;
            used |= 1 << effect;
            if (count < 4) first[count] = effect;
            else last.x = effect;
            count++;
        }
    }
}
