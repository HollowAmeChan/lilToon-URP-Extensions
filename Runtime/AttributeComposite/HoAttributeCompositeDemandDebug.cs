using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.AttributeComposite
{
    internal static class HoAttributeCompositeDemandDebug
    {
        internal static void Declare(Camera camera, UnityEngine.Object owner, HoAttributeCompositeSettings settings)
        {
            var queries = new List<HoACQueryDescriptor>();
            var names = new List<string>();
            HoACDemandResources resources = HoACDemandResources.None;
            uint lanes = 0;
            var mode = settings.debugMode;
            if (mode == HoAttributeCompositeDebugMode.GeometryCoverage)
                queries.Add(HoACQueryDescriptor.Resolve(HoACQueryKind.Geometry, null, 0, HoACMaskDomain.Screen));
            else if (mode == HoAttributeCompositeDebugMode.OutlineCoverage)
                queries.Add(HoACQueryDescriptor.Resolve(HoACQueryKind.Outline, null, 0, HoACMaskDomain.Screen));
            else if (mode == HoAttributeCompositeDebugMode.InputAvailability)
                resources = HoACDemandResources.Identity | HoACDemandResources.Geometry | HoACDemandResources.Outline;
            else if (mode == HoAttributeCompositeDebugMode.LaneCoverage || mode == HoAttributeCompositeDebugMode.LaneSemanticId || mode == HoAttributeCompositeDebugMode.LaneObjectMask)
            {
                foreach (HoSemanticEntry entry in HoSemanticSchema.Declarations)
                {
                    names.Add(entry.name);
                    queries.Add(HoACQueryDescriptor.Resolve(HoACQueryKind.Semantic, entry.name, 0, HoACMaskDomain.Screen));
                }
            }
            else if (HoAttributeCompositeSettings.IsSemanticDebug(mode))
            {
                names.Add(settings.debugSemanticName);
                resources |= HoACDemandResources.Identity;
                if (HoSemanticSchema.TryGetByName(settings.debugSemanticName, out HoSemanticEntry entry)) lanes = 1u << entry.laneIndex;
                if (mode == HoAttributeCompositeDebugMode.FinalCoverage || mode == HoAttributeCompositeDebugMode.SemanticCompare)
                    queries.Add(HoACQueryDescriptor.Resolve(HoACQueryKind.Semantic, settings.debugSemanticName, 0, HoACMaskDomain.Screen));
                if (mode == HoAttributeCompositeDebugMode.SurfaceWritten || mode == HoAttributeCompositeDebugMode.SurfaceValue ||
                    mode == HoAttributeCompositeDebugMode.SemanticOwnerMatch || mode == HoAttributeCompositeDebugMode.SemanticCompare)
                    resources |= HoACDemandResources.LegacySemantic;
                if (mode == HoAttributeCompositeDebugMode.WrittenCoverage || mode == HoAttributeCompositeDebugMode.WeightedCoverage ||
                    mode == HoAttributeCompositeDebugMode.SemanticPrecision || mode == HoAttributeCompositeDebugMode.SemanticCompare)
                    resources |= HoACDemandResources.CorrelatedSemantic;
            }
            HoAttributeCompositeConsumerRegistry.DeclareForCamera(camera, owner, "AttributeComposite Debug", queries, names, resources, 0, lanes);
        }
    }
}
