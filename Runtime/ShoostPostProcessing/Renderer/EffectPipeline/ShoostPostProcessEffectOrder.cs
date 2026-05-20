namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ShoostPostProcessEffectOrder
    {
        public static bool IsRemovedEffectSlot(ShoostPostProcessEffect effect)
        {
            return ShoostPostProcessEffectDescriptor.Get(effect).IsRemoved;
        }

        public static int CompareRuntimeLayerOrder(ShoostPostProcessRuntimeLayer a, ShoostPostProcessRuntimeLayer b)
        {
            int orderA = GetRuntimeEffectOrder(a.settings.effect);
            int orderB = GetRuntimeEffectOrder(b.settings.effect);
            return orderA.CompareTo(orderB);
        }

        private static int GetRuntimeEffectOrder(ShoostPostProcessEffect effect)
        {
            return ShoostPostProcessEffectDescriptor.Get(effect).RuntimeOrder;
        }
    }
}
