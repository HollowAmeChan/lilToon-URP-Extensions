using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
namespace lilToon.URP.Extensions.RTShadow
{
    public enum HoRTShadowProductStatus { Available, Unsupported, NoReceivers }
    public enum HoRTShadowPixelStatus { Unrequested = 0, Valid = 1, InvalidSurface = 2, OutsideLightDomain = 3, RayLimitTooShort = 4 }
    public readonly struct HoRTShadowLightDiagnostics
    {
        public readonly Light Light;
        public readonly long LightId;
        public readonly int Slot, Width, Height, HistoryFrame;
        public readonly HoRTShadowReconstruction Reconstruction;
        public readonly bool HistoryReset;
        public readonly float AngularDiameter;
        public readonly float TerminatorCorrection;
        public readonly int Samples, Receivers, Estimates;
        internal HoRTShadowLightDiagnostics(Light light, long id, int slot, int width, int height, HoRTShadowReconstruction reconstruction, bool reset, int frame,
            float angle, int samples, int receivers, int estimates, float terminatorCorrection)
        { Light = light; LightId = id; Slot = slot; Width = width; Height = height; Reconstruction = reconstruction; HistoryReset = reset; HistoryFrame = frame; AngularDiameter = angle; Samples = samples; Receivers = receivers; Estimates = estimates; TerminatorCorrection = terminatorCorrection; }
    }
    // Texture handles are borrowed only in this current camera RenderGraph.
    public readonly struct HoRTShadowProduct
    {
        public readonly long LightId, RenderSerial, SceneLifetime;
        public readonly Light Light;
        public readonly int Slot, Width, Height, Samples, Estimates;
        public readonly uint SceneRevision;
        public readonly HoRTShadowReconstruction Reconstruction;
        public readonly float TerminatorCorrection;
        public readonly HoRTShadowProductStatus Status;
        public readonly string Reason;
        public readonly TextureHandle Raw, Visibility;
        internal HoRTShadowProduct(Light light, long id, int slot, long serial, int width, int height, int samples, int estimates,
            long sceneLifetime, uint sceneRevision, HoRTShadowReconstruction mode, TextureHandle raw, TextureHandle visibility,
            HoRTShadowProductStatus status = HoRTShadowProductStatus.Available, string reason = null, float terminatorCorrection = 0)
        { Light=light;LightId=id;Slot=slot;RenderSerial=serial;Width=width;Height=height;Samples=samples;Estimates=estimates;SceneLifetime=sceneLifetime;SceneRevision=sceneRevision;Reconstruction=mode;Raw=raw;Visibility=visibility;Status=status;Reason=reason;TerminatorCorrection=terminatorCorrection; }
    }
    public sealed class HoRTShadowRenderGraphResources : ContextItem
    {
        private readonly List<HoRTShadowProduct> products = new();
        public IReadOnlyList<HoRTShadowProduct> Products => products;
        public Camera Camera { get; internal set; }
        public bool TryGet(long lightId, out HoRTShadowProduct product)
        { foreach(var item in products)if(item.LightId==lightId){product=item;return true;}product=default;return false; }
        internal void Add(HoRTShadowProduct product) => products.Add(product);
        internal TextureHandle raw, visibility;
        internal Camera camera;internal Light light;internal int lightId,width,height,samples,estimates;
        internal long sceneLifetime,renderSerial;internal uint sceneRevision;internal HoRTShadowReconstruction reconstruction;
        public override void Reset(){products.Clear();Camera=camera=null;light=null;raw=visibility=TextureHandle.nullHandle;lightId=width=height=samples=estimates=0;sceneLifetime=renderSerial=0;sceneRevision=0;reconstruction=HoRTShadowReconstruction.Raw;}
    }
    // Borrowed current-request handles for a compatibility pass enqueued after
    // the producer. This does not synchronize the CPU or promise GPU completion.
    public readonly struct HoRTShadowCompatibilityProduct
    {
        public readonly long LightId, RenderSerial; public readonly Light Light; public readonly RTHandle Raw, Visibility;public readonly int Slot;
        public readonly HoRTShadowReconstruction Reconstruction;
        public readonly float TerminatorCorrection;
        internal HoRTShadowCompatibilityProduct(Light light,long id,long serial,RTHandle raw,RTHandle visibility,HoRTShadowReconstruction reconstruction,int slot,float terminatorCorrection)
        {Light=light;LightId=id;RenderSerial=serial;Raw=raw;Visibility=visibility;Reconstruction=reconstruction;Slot=slot;TerminatorCorrection=terminatorCorrection;}
    }
}
