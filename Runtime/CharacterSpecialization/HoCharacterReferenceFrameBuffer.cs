using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.GeometryData;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    /// <summary>Consumer adapter: associates OB screen identities with Renderer-scoped GD frame outputs.</summary>
    public sealed class HoCharacterReferenceFrameBuffer : IDisposable
    {
        public const int IdentityCapacity = 65536;
        public const int Stride = 64;
        public static readonly int BufferId = Shader.PropertyToID("_HoGDObjectFrames");
        public static readonly int IdentityMapId = Shader.PropertyToID("_HoGDIdentityFrameSlots");
        public static readonly int FrameCountId = Shader.PropertyToID("_HoGDObjectFrameCount");
        public static readonly int ViewPositionId = Shader.PropertyToID("_HoGDObserverPosition");
        private static readonly uint[] IdentityRows = new uint[IdentityCapacity];
        private static readonly uint[] PendingIdentities = new uint[IdentityCapacity];
        private static readonly List<HoGeometryFrameData> PendingFrames = new List<HoGeometryFrameData>();
        private static readonly Dictionary<Renderer, uint> RendererFrames = new Dictionary<Renderer, uint>();
        private static readonly HashSet<uint> ConflictingIdentities = new HashSet<uint>();
        private static HoGeometryFrameData[] frames = Array.Empty<HoGeometryFrameData>();
        private static GraphicsBuffer buffer;
        private static GraphicsBuffer identityMap;
        private static int leases;
        private bool disposed;
        public static int UploadRevision { get; private set; }
        public static int FrameCount => frames.Length;
        public static GraphicsBuffer IdentityMapBuffer => identityMap;

        public HoCharacterReferenceFrameBuffer() { leases++; }

        public GraphicsBuffer Capture()
        {
            if (disposed) throw new ObjectDisposedException(nameof(HoCharacterReferenceFrameBuffer));
            HoObjectBufferRegistry.EnsureBuilt();
            // Palette row 0 is invalid. Camera changes do not alter this shared world data.
            PendingFrames.Clear();
            PendingFrames.Add(default);
            RendererFrames.Clear();
            ConflictingIdentities.Clear();
            Array.Clear(PendingIdentities, 0, IdentityCapacity);
            foreach (var source in HoGeometryReferenceFrameRegistry.GetActiveSources())
            {
                if (source == null || !source.TryGetFrame(out var frame)) continue;
                uint index = (uint)PendingFrames.Count;
                PendingFrames.Add(frame);
                foreach (var renderer in source.GetTargetRenderers())
                {
                    if (RendererFrames.TryGetValue(renderer, out uint existing) && existing != index) RendererFrames[renderer] = 0;
                    else RendererFrames[renderer] = index;
                }
            }

            // OB identities can represent several Renderers. Ambiguity stays invalid rather than choosing a producer.
            foreach (var binding in RendererFrames)
            {
                uint identity = HoGeometryRendererBinding.GetManagedValue(binding.Key) & 0xffffu;
                if (identity == 0 || ConflictingIdentities.Contains(identity)) continue;
                uint index = binding.Value;
                if (index == 0 || (PendingIdentities[identity] != 0 && PendingIdentities[identity] != index))
                {
                    PendingIdentities[identity] = 0;
                    ConflictingIdentities.Add(identity);
                }
                else PendingIdentities[identity] = index;
            }

            bool frameChanged = frames.Length != PendingFrames.Count || buffer == null || !buffer.IsValid();
            for (int i = 0; !frameChanged && i < frames.Length; i++) frameChanged = !frames[i].Equals(PendingFrames[i]);
            int capacity = Mathf.NextPowerOfTwo(PendingFrames.Count);
            if (buffer == null || !buffer.IsValid() || buffer.count < capacity)
            {
                buffer?.Dispose();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Stride) { name = "Ho-Character GD Frames" };
                frameChanged = true;
            }
            if (frameChanged)
            {
                frames = PendingFrames.ToArray();
                buffer.SetData(frames);
            }

            // Bone motion updates the compact frame palette; the 256 KiB identity map only changes with associations.
            bool mapChanged = identityMap == null || !identityMap.IsValid();
            for (int i = 0; !mapChanged && i < IdentityCapacity; i++) mapChanged = IdentityRows[i] != PendingIdentities[i];
            if (identityMap == null || !identityMap.IsValid()) identityMap = new GraphicsBuffer(GraphicsBuffer.Target.Structured, IdentityCapacity, 4) { name = "Ho-Character Identity to GD Frame" };
            if (mapChanged)
            {
                Array.Copy(PendingIdentities, IdentityRows, IdentityCapacity);
                identityMap.SetData(IdentityRows);
            }
            if (frameChanged || mapChanged) UploadRevision++;
            return buffer;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (--leases == 0) ReleaseResources();
        }

        public static void ReleaseResources()
        {
            buffer?.Dispose();
            identityMap?.Dispose();
            buffer = null;
            identityMap = null;
            frames = Array.Empty<HoGeometryFrameData>();
            Array.Clear(IdentityRows, 0, IdentityCapacity);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources() => ReleaseResources();
    }
}
