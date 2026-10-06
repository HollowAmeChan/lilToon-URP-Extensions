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
        public int VisualIdentityTextures { get; }
        public int VisualSelectionTextures { get; }
        public string OutlineInheritanceStatus => VisualIdentityTextures > 0 ? "Renderer owner / pixel projection" : "Not produced";
        internal HoAttributeCompositeProductionSnapshot(HoAttributeCompositeDemandSnapshot demand, int selection, int legacy, int statistics, int numeric, int visualIdentity, int visualSelection)
        {
            Demand = demand; SelectionTextures = selection + visualSelection; LegacySemanticTextures = legacy;
            CorrelatedStatisticTextures = statistics; NumericSurfaceTextures = numeric;
            VisualIdentityTextures = visualIdentity; VisualSelectionTextures = visualSelection;
        }
    }
    public static class HoAttributeCompositeProductionDiagnostics
    {
        public static HoAttributeCompositeProductionSnapshot LastSnapshot { get; private set; }
        internal static void Publish(HoAttributeCompositeDemandSnapshot demand, int selection, int legacy, int statistics, int numeric, int visualIdentity = 0, int visualSelection = 0) =>
            LastSnapshot = new HoAttributeCompositeProductionSnapshot(demand, selection, legacy, statistics, numeric, visualIdentity, visualSelection);
    }
}
