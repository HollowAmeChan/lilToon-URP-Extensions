using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.NRD;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.RTShadow
{
    // One camera/renderer/light stream. Geometry, material, receiver or camera
    // changes reset stabilization until matching surface motion is available.
    internal sealed class HoRTShadowSigmaContext : IDisposable
    {
        internal readonly RTHandle normal, motion, viewZ, penumbra, encoded, visibility, visibilitySum;
        internal readonly int Estimates;
        internal readonly HoNrdContext nrd;
        private readonly List<RTHandle> textures = new();
        private readonly int width, height;
        private bool historyValid, sawBegin, produced;
        private int frame, lightId, signature;
        private long sceneLifetime;
        private uint sceneRevision;
        private Matrix4x4 previousView, previousProjection;
        private float timeDelta;
        internal bool LastReset { get; private set; }
        internal int LastFrame { get; private set; }
        internal int DispatchCount => nrd.LastDispatchCount;

        internal HoRTShadowSigmaContext(int width, int height, ComputeShader[] shaders, int estimates = 1)
        {
            this.width = width; this.height = height; Estimates = Mathf.Clamp(estimates, 1, 256);
            try
            {
                nrd = new HoNrdContext(shaders, 16, HoRTShadowSigmaManifest.Identifiers, HoRTShadowSigmaManifest.Resources);
                nrd.Prepare(width, height);
                normal = Allocate(GraphicsFormat.R16G16B16A16_UNorm, "Normal");
                motion = Allocate(GraphicsFormat.R16G16B16A16_SFloat, "Motion");
                viewZ = Allocate(GraphicsFormat.R32_SFloat, "ViewZ");
                penumbra = Allocate(GraphicsFormat.R16_SFloat, "Penumbra");
                encoded = Allocate(GraphicsFormat.R16_SFloat, "Encoded shadow");
                visibility = Allocate(GraphicsFormat.R32G32B32A32_SFloat, "Visibility");
                if (Estimates > 1) visibilitySum = Allocate(GraphicsFormat.R32G32_SFloat, "Decoded and raw visibility sums");
            }
            catch { Dispose(); throw; }
        }
        private RTHandle Allocate(GraphicsFormat format, string name)
        {
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore)) throw new NotSupportedException("SIGMA UAV " + format);
            var descriptor = new RenderTextureDescriptor(width, height) { graphicsFormat = format, depthBufferBits = 0, msaaSamples = 1, enableRandomWrite = true };
            var texture = RTHandles.Alloc(descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "HoRTShadow SIGMA " + name);
            textures.Add(texture); return texture;
        }
        internal void CameraBegin()
        { if (sawBegin && !produced) historyValid = false; sawBegin = true; produced = false; }
        internal void Declare(RenderGraph graph, IUnsafeRenderGraphBuilder builder)
        { foreach (var texture in textures) builder.UseTexture(graph.ImportTexture(texture), AccessFlags.ReadWrite); nrd.Declare(graph, builder); }
        internal int Begin(bool preview, long lifetime, uint revision, int light, int inputSignature,
            Matrix4x4 view, Matrix4x4 projection, double time)
        {
            timeDelta = nrd.SetRenderTime(time, out bool timeReset);
            LastReset = !preview || !historyValid || timeReset || lifetime != sceneLifetime || revision != sceneRevision
                || light != lightId || inputSignature != signature || view != previousView || projection != previousProjection;
            if (LastReset) frame = 0;
            LastFrame = preview ? frame : 0;
            sceneLifetime = lifetime; sceneRevision = revision; lightId = light; signature = inputSignature;
            return LastFrame;
        }
        internal void Record(CommandBuffer cmd, ComputeShader shader, RTHandle raw, Vector3 direction, float tanAngularRadius,
            Matrix4x4 view, Matrix4x4 projection, bool preview, int estimateIndex = 0)
        {
            int prepare = shader.FindKernel("Prepare"), decode = shader.FindKernel(Estimates > 1 ? "Accumulate" : "Decode");
            cmd.SetComputeVectorParam(shader, "_HoRTShadowSigmaSize", new Vector4(width, height, 1f / width, 1f / height));
            cmd.SetComputeVectorParam(shader, "_HoRTShadowSigmaLight", new Vector4(direction.x, direction.y, direction.z, tanAngularRadius));
            cmd.SetComputeTextureParam(shader, prepare, "_HoRTShadowRaw", raw.nameID);
            cmd.SetComputeTextureParam(shader, prepare, "_HoRTShadowNormal", normal.nameID);
            cmd.SetComputeTextureParam(shader, prepare, "_HoRTShadowMotion", motion.nameID);
            cmd.SetComputeTextureParam(shader, prepare, "_HoRTShadowViewZ", viewZ.nameID);
            cmd.SetComputeTextureParam(shader, prepare, "_HoRTShadowPenumbra", penumbra.nameID);
            cmd.DispatchCompute(shader, prepare, (width + 7) / 8, (height + 7) / 8, 1);
            var common = HoNrdNative.CommonSettings.Create(width, height, view, projection,
                LastReset ? view : previousView, LastReset ? projection : previousProjection, LastFrame, LastReset);
            common.timeDelta = timeDelta;
            uint stabilizedFrames = preview ? (uint)Mathf.Clamp(Mathf.FloorToInt(.084f / Mathf.Max(.000001f, timeDelta * .001f) + .5f), 0, 7) : 0;
            nrd.Record(cmd, common, new Dictionary<string, RenderTargetIdentifier>
            {
                {"IN_NORMAL_ROUGHNESS", normal.nameID}, {"IN_MV", motion.nameID}, {"IN_VIEWZ", viewZ.nameID},
                {"IN_PENUMBRA", penumbra.nameID}, {"OUT_SHADOW_TRANSLUCENCY", encoded.nameID}
            }, new HoNrdNative.SigmaSettings { lightDirection = direction, planeDistanceSensitivity = .02f, maxStabilizedFrameNum = stabilizedFrames });
            cmd.SetComputeTextureParam(shader, decode, "_HoRTShadowRaw", raw.nameID);
            cmd.SetComputeTextureParam(shader, decode, "_HoRTShadowSigmaEncoded", encoded.nameID);
            cmd.SetComputeTextureParam(shader, decode, "_HoRTShadowSigmaViewZ", viewZ.nameID);
            cmd.SetComputeTextureParam(shader, decode, "_HoRTShadowSigmaVisibility", visibility.nameID);
            if (Estimates > 1)
            {
                cmd.SetComputeTextureParam(shader, decode, "_HoRTShadowSigmaSum", visibilitySum.nameID);
                cmd.SetComputeIntParam(shader, "_HoRTShadowEstimateIndex", estimateIndex);
            }
            cmd.DispatchCompute(shader, decode, (width + 7) / 8, (height + 7) / 8, 1);
            previousView = view; previousProjection = projection; historyValid = true; produced = true; frame++;
        }
        public void Dispose() { nrd?.Dispose(); foreach (var texture in textures) texture.Release(); textures.Clear(); }
    }
}
