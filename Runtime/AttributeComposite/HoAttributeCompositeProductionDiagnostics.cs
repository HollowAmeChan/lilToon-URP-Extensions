namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>Logical allocations declared by the last AC camera render, not a GPU timing estimate.</summary>
    public sealed class HoAttributeCompositeProductionSnapshot
    {
        public HoAttributeCompositeDemandSnapshot Demand { get; }
        public int SelectionTextures { get; }
        public int LegacySemanticTextures { get; }
        public int CorrelatedStatisticTextures { get; }
        public int CorrelatedCaptureTextures => CorrelatedStatisticTextures > 0 ? 2 : 0; // packet + private depth
        public int NumericSurfaceTextures { get; }
        internal HoAttributeCompositeProductionSnapshot(HoAttributeCompositeDemandSnapshot demand, int selection, int legacy, int statistics, int numeric)
        { Demand = demand; SelectionTextures = selection; LegacySemanticTextures = legacy; CorrelatedStatisticTextures = statistics; NumericSurfaceTextures = numeric; }
    }
    public static class HoAttributeCompositeProductionDiagnostics
    {
        public static HoAttributeCompositeProductionSnapshot LastSnapshot { get; private set; }
        internal static void Publish(HoAttributeCompositeDemandSnapshot demand, int selection, int legacy, int statistics, int numeric) =>
            LastSnapshot = new HoAttributeCompositeProductionSnapshot(demand, selection, legacy, statistics, numeric);
    }
}
