using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace lilToon.URP.Extensions.AttributeComposite
{
    [Flags]
    public enum HoACDemandResources
    {
        None = 0, Identity = 1, Geometry = 2, Outline = 4, Selection = 8,
        SurfaceSemantic = 16, LegacySemantic = 32, CorrelatedSemantic = 64,
        SurfaceAttributes = 128, SurfaceColor = 256, SurfaceNormal = 512
    }

    /// <summary>Immutable declaration. Display names are not ownership keys.</summary>
    public sealed class HoAttributeCompositeConsumerDeclaration
    {
        private readonly string[] names;
        private readonly string[] unresolved;
        public int OwnerId { get; }
        public string Consumer { get; }
        public string[] Names => (string[])names.Clone();
        public string[] UnresolvedNames => (string[])unresolved.Clone();
        public IReadOnlyList<HoACQueryDescriptor> Queries { get; }
        public HoACDemandResources Resources { get; }
        public uint LaneMask { get; }
        public uint AttributeMask { get; }
        public int InvalidQueryCount { get; }

        public HoAttributeCompositeConsumerDeclaration(string consumer, string[] semanticNames)
            : this(0, consumer, ResolveNames(semanticNames), semanticNames, HoACDemandResources.None, 0, 0) { }

        internal static HoACQueryDescriptor[] ResolveNames(string[] names)
        {
            var result = new HoACQueryDescriptor[names?.Length ?? 0];
            for (int i = 0; i < result.Length; i++)
                result[i] = HoACQueryDescriptor.Resolve(HoACQueryKind.Semantic, names[i], 0, HoACMaskDomain.Screen);
            return result;
        }

        internal HoAttributeCompositeConsumerDeclaration(int ownerId, string consumer, IEnumerable<HoACQueryDescriptor> queries,
            IEnumerable<string> semanticNames, HoACDemandResources resources, uint attributeMask, uint laneMask)
        {
            OwnerId = ownerId; Consumer = consumer;
            names = semanticNames != null ? new List<string>(semanticNames).ToArray() : Array.Empty<string>();
            var missing = new List<string>();
            foreach (string name in names)
                if (!HoSemanticSchema.TryGetByName(name, out _)) missing.Add(name);
            unresolved = missing.ToArray();
            var queryList = queries != null ? new List<HoACQueryDescriptor>(queries) : new List<HoACQueryDescriptor>();
            Queries = new ReadOnlyCollection<HoACQueryDescriptor>(queryList);
            foreach (HoACQueryDescriptor query in queryList)
            {
                if (!query.IsValid) { InvalidQueryCount++; continue; }
                if (query.NeedsIdentity) resources |= HoACDemandResources.Identity;
                if (query.NeedsGeometry) resources |= HoACDemandResources.Geometry;
                if (query.NeedsOutline) resources |= HoACDemandResources.Outline;
                if (query.NeedsSelection)
                {
                    resources |= HoACDemandResources.Selection | HoACDemandResources.Identity;
                    laneMask |= 1u << query.Value;
                    if (HoSemanticSchema.TryGetBySemanticId(query.SemanticId, out HoSemanticEntry entry) &&
                        entry.sourceMode != HoSemanticSourceMode.ObjectOnly) resources |= HoACDemandResources.SurfaceSemantic;
                }
            }
            if (attributeMask != 0) resources |= HoACDemandResources.SurfaceAttributes | HoACDemandResources.Identity;
            Resources = resources; LaneMask = laneMask; AttributeMask = attributeMask;
        }
    }

    /// <summary>One camera render invocation, including repeated RenderRequests within the same frame.</summary>
    public sealed class HoAttributeCompositeDemandSnapshot
    {
        public int CameraId { get; }
        public string CameraName { get; }
        public int Frame { get; }
        public long RenderSequence { get; }
        public IReadOnlyList<HoAttributeCompositeConsumerDeclaration> Consumers { get; }
        public HoACDemandResources Resources { get; }
        public uint LaneMask { get; }
        public uint AttributeMask { get; }
        public int InvalidQueryCount { get; }
        public bool HasUnscopedConsumers { get; }
        public bool NeedsSelection => (Resources & HoACDemandResources.Selection) != 0;
        public bool NeedsSurfaceSemantic => (Resources & (HoACDemandResources.SurfaceSemantic |
            HoACDemandResources.LegacySemantic | HoACDemandResources.CorrelatedSemantic)) != 0;

        internal HoAttributeCompositeDemandSnapshot(Camera camera, long sequence, int frame,
            IEnumerable<HoAttributeCompositeConsumerDeclaration> declarations)
        {
            CameraId = camera.GetInstanceID(); CameraName = camera.name; RenderSequence = sequence; Frame = frame;
            var list = new List<HoAttributeCompositeConsumerDeclaration>(declarations);
            Consumers = new ReadOnlyCollection<HoAttributeCompositeConsumerDeclaration>(list);
            foreach (var declaration in list)
            {
                Resources |= declaration.Resources; LaneMask |= declaration.LaneMask; AttributeMask |= declaration.AttributeMask;
                InvalidQueryCount += declaration.InvalidQueryCount;
                HasUnscopedConsumers |= declaration.OwnerId == 0;
            }
        }
    }
}
