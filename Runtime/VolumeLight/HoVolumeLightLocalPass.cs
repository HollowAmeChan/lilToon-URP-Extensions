using System.Collections.Generic;
using Unity.Collections;
using lilToon.URP.Extensions.Atmosphere;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.VolumeLight
{
    // Transient per-camera atlases: neither the SDF nor screen depth crops source shadows.
    internal sealed class HoVolumeLightLocalPass : ScriptableRenderPass
    {
        private Material material;
        private RTHandle noise;
        private float intensity, anisotropy, distance;
        private ScriptableRenderContext renderContext;
        internal static RTHandle DebugShadowTarget;
        internal void SetContext(ScriptableRenderContext context) => renderContext = context;
        private readonly List<HoVolumeLightSource> sources = new();
        internal int Count { get; private set; }
        internal HoVolumeLightLocalPass() { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; ConfigureInput(ScriptableRenderPassInput.Depth); }
        internal void Setup(float intensity, float anisotropy, float distance) { this.intensity = intensity; this.anisotropy = anisotropy; this.distance = distance; }
        internal void Dispose() { CoreUtils.Destroy(material); material = null; noise?.Release(); noise = null; Count = 0; }
        private sealed class ShadowData
        {
            internal RendererListHandle list;
            internal Matrix4x4 view, projection, cameraView, cameraProjection;
            internal Vector4 bias, lightPosition;
            internal Rect viewport;
        }
        private sealed class DrawData
        {
            internal Material material;
            internal HoAtmosphereMediumResources medium;
            internal TextureHandle depth, shadow, noise;
            internal Vector4 positionRange, directionType, color, attenuation, parameters, shape, shadowParameters;
            internal Matrix4x4 worldToLight;
            internal Matrix4x4[] shadowMatrices;
        }
        private sealed class DebugData { internal Material material; internal TextureHandle shadow; }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            Count = 0;
            if (HoVolumeLightSource.Active.Count == 0) return;
            var medium = frame.GetOrCreate<HoAtmosphereMediumResources>(); var resources = frame.Get<UniversalResourceData>();
            if (!medium.IsValid || !resources.cameraDepthTexture.IsValid() || intensity <= 0) return;
            var cameraData = frame.Get<UniversalCameraData>(); var lights = frame.Get<UniversalLightData>();
            var rendering = frame.Get<UniversalRenderingData>(); var shadowData = frame.Get<UniversalShadowData>();
            if (material == null) { var shader = Resources.Load<Shader>("HoVolumeLightLocal"); if (shader == null) return; material = CoreUtils.CreateEngineMaterial(shader); }
            if (noise == null || noise.externalTexture == null) { noise?.Release(); noise = RTHandles.Alloc(Resources.Load<Texture2D>("HoVolumeLightBlueNoise")); }
            sources.Clear(); sources.AddRange(HoVolumeLightSource.Active);
            foreach (var source in sources)
            {
                if (source == null || !source.isActiveAndEnabled) continue;
                var light = source.Source;
                if (light == null || !light.isActiveAndEnabled || light.intensity <= 0 || source.intensity <= 0) { source.Status = "灯光或体积亮度已关闭"; continue; }
                if (light.type != LightType.Spot && light.type != LightType.Point) { source.Status = "仅支持 Point / Spot"; continue; }
                int visibleIndex = -1;
                for (int i = 0; i < lights.visibleLights.Length; ++i) if (lights.visibleLights[i].light == light) { visibleIndex = i; break; }
                if (visibleIndex < 0) { source.Status = "本相机无可见照亮范围"; continue; }
                var bounds = new Bounds(light.transform.position, Vector3.one * light.range * 2);
                if (!bounds.Intersects(medium.snapshot.bounds)) { source.Status = "与大气区域不相交"; continue; }
                bool point = light.type == LightType.Point;
                int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(source.shadowResolution), 128, 2048), faces = point ? 6 : 1;
                int columns = point ? 3 : 1, rows = point ? 2 : 1;
                TextureHandle shadow = graph.defaultResources.defaultShadowTexture;
                var matrices = new Matrix4x4[6]; bool captured = false;
                var shadowCull = rendering.cullResults; int shadowIndex = visibleIndex;
                bool wantsShadow = source.quality == HoVolumeLightQuality.HD && light.shadows != LightShadows.None;
                if (wantsShadow && cameraData.camera.TryGetCullingParameters(out var culling))
                {
                    float range = Mathf.Max(.1f, light.range);
                    var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(light.transform.position - Vector3.forward * range, Quaternion.identity, Vector3.one).inverse;
                    culling.cullingMatrix = Matrix4x4.Ortho(-range, range, -range, range, .001f, range * 2 + .01f) * view;
                    var planes = GeometryUtility.CalculateFrustumPlanes(culling.cullingMatrix); culling.cullingPlaneCount = planes.Length;
                    for (int i = 0; i < planes.Length; ++i) culling.SetCullingPlane(i, planes[i]);
                    culling.origin = light.transform.position; culling.isOrthographic = true;
                    culling.cullingMask = uint.MaxValue; culling.shadowDistance = range * 2;
                    culling.cullingOptions = CullingOptions.ShadowCasters | CullingOptions.NeedsLighting;
                    shadowCull = renderContext.Cull(ref culling); shadowIndex = -1;
                    for (int i = 0; i < shadowCull.visibleLights.Length; ++i) if (shadowCull.visibleLights[i].light == light) { shadowIndex = i; break; }
                }
                if (wantsShadow && shadowIndex >= 0 && shadowCull.GetShadowCasterBounds(shadowIndex, out _))
                {
                    var views = new Matrix4x4[faces]; var projections = new Matrix4x4[faces];
                    var splits = new NativeArray<ShadowSplitData>(faces, Allocator.Temp);
                    var perLight = new NativeArray<LightShadowCasterCullingInfo>(shadowCull.visibleLights.Length, Allocator.Temp);
                    for (int face = 0; face < faces; ++face)
                    {
                        ShadowSplitData split;
                        if (point) ShadowUtils.ExtractPointLightMatrix(ref shadowCull, shadowData, shadowIndex, (CubemapFace)face, 2, out matrices[face], out views[face], out projections[face], out split);
                        else ShadowUtils.ExtractSpotLightMatrix(ref shadowCull, shadowData, shadowIndex, out matrices[face], out views[face], out projections[face], out split);
                        splits[face] = split;
                    }
                    perLight[shadowIndex] = new LightShadowCasterCullingInfo { splitRange = new RangeInt(0, faces), projectionType = BatchCullingProjectionType.Perspective };
                    renderContext.CullShadowCasters(shadowCull, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = perLight });
                    splits.Dispose(); perLight.Dispose();
                    shadow = graph.CreateTexture(new TextureDesc(resolution * columns, resolution * rows)
                    { name = "Ho Local LightDepth " + light.name, depthBufferBits = DepthBits.Depth16, isShadowMap = true, clearBuffer = true, filterMode = FilterMode.Bilinear });
                    for (int face = 0; face < faces; ++face)
                    {
                        var view = views[face]; var projection = projections[face];
                        var atlas = Matrix4x4.identity; atlas.m00 = 1f / columns; atlas.m11 = 1f / rows;
                        atlas.m03 = (float)(face % columns) / columns; atlas.m13 = (float)(face / columns) / rows;
                        matrices[face] = atlas * matrices[face];
                        using var builder = graph.AddRasterRenderPass<ShadowData>("Ho Local LightDepth Face " + face, out var data);
                        var settings = new ShadowDrawingSettings(shadowCull, shadowIndex) { splitIndex = face, useRenderingLayerMaskTest = UniversalRenderPipeline.asset.useRenderingLayers };
                        data.list = graph.CreateShadowRendererList(ref settings); builder.UseRendererList(data.list);
                        data.view = view; data.projection = projection; data.cameraView = cameraData.GetViewMatrix(); data.cameraProjection = cameraData.GetProjectionMatrix();
                        float texelSize = (point ? Mathf.Tan(46 * Mathf.Deg2Rad) : Mathf.Tan(light.spotAngle * .5f * Mathf.Deg2Rad)) * light.range / resolution;
                        data.bias = new Vector4(-light.shadowBias * texelSize, point ? 0 : -light.shadowNormalBias * texelSize, 1, 0);
                        data.lightPosition = light.transform.position; data.viewport = new Rect(face % columns * resolution, face / columns * resolution, resolution, resolution);
                        builder.SetRenderAttachmentDepth(shadow, AccessFlags.ReadWrite); builder.AllowGlobalStateModification(true);
                        builder.SetRenderFunc(static (ShadowData d, RasterGraphContext ctx) =>
                        {
                            ctx.cmd.SetGlobalVector("_ShadowBias", d.bias); ctx.cmd.SetGlobalVector("_LightPosition", d.lightPosition);
                            ctx.cmd.EnableShaderKeyword("_CASTING_PUNCTUAL_LIGHT_SHADOW"); ctx.cmd.SetGlobalDepthBias(1, 2.5f);
                            ctx.cmd.SetViewProjectionMatrices(d.view, d.projection); ctx.cmd.SetViewport(d.viewport); ctx.cmd.DrawRendererList(d.list);
                            ctx.cmd.SetGlobalDepthBias(0, 0); ctx.cmd.DisableShaderKeyword("_CASTING_PUNCTUAL_LIGHT_SHADOW");
                            ctx.cmd.SetViewProjectionMatrices(d.cameraView, d.cameraProjection);
                        });
                        captured = true;
                    }
                }
                UniversalRenderPipeline.GetLightAttenuationAndSpotDirection(light.type, light.range, light.transform.localToWorldMatrix,
                    light.spotAngle, light.innerSpotAngle, out var attenuation, out var spotDirection);
                if (DebugShadowTarget != null && captured)
                    using (var builder = graph.AddRasterRenderPass<DebugData>("Local depth readback", out var data))
                    {
                        data.material = material; data.shadow = shadow; builder.UseTexture(shadow, AccessFlags.Read);
                        builder.SetRenderAttachment(graph.ImportTexture(DebugShadowTarget), 0, AccessFlags.WriteAll); builder.AllowGlobalStateModification(true);
                        builder.SetRenderFunc(static (DebugData d, RasterGraphContext ctx) => { ctx.cmd.SetGlobalTexture("_HoDebugShadow", d.shadow); Blitter.BlitTexture(ctx.cmd, new Vector4(1,1,0,0), d.material, 1); });
                    }
                using (var builder = graph.AddRasterRenderPass<DrawData>("Ho Local Volume " + light.name, out var data))
                {
                    data.material = material; data.medium = medium; data.depth = resources.cameraDepthTexture; data.shadow = shadow; data.noise = graph.ImportTexture(noise);
                    data.shadowMatrices = matrices; data.worldToLight = Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one).inverse;
                    var p = light.transform.position; data.positionRange = new Vector4(p.x, p.y, p.z, light.range);
                    data.directionType = new Vector4(spotDirection.x, spotDirection.y, spotDirection.z, point ? 1 : 0);
                    Color color = lights.visibleLights[visibleIndex].finalColor * source.tint;
                    data.color = new Vector4(color.r, color.g, color.b, Mathf.Max(0, source.intensity)); data.attenuation = attenuation;
                    data.parameters = new Vector4(intensity, distance, Mathf.Clamp(source.stepCount, 16, 256), anisotropy);
                    data.shape = new Vector4(Mathf.Max(.01f, source.sourceRadius), Mathf.Clamp(source.edgeSoftness, .001f, 1), Mathf.Tan(light.spotAngle * Mathf.Deg2Rad * .5f), source.jitter ? 1 : 0);
                    data.shadowParameters = new Vector4(captured ? light.shadowStrength * source.shadowStrength : 0, 1f / (resolution * columns), 1f / (resolution * rows), light.shadows == LightShadows.Soft ? 1 : 0);
                    builder.UseTexture(data.depth, AccessFlags.Read); builder.UseTexture(medium.densityTexture, AccessFlags.Read); builder.UseTexture(shadow, AccessFlags.Read); builder.UseTexture(data.noise, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite); builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext ctx) =>
                    {
                        HoAtmosphereMediumBindings.Bind(ctx.cmd, d.medium, d.medium.snapshot.sampleTime);
                        ctx.cmd.SetGlobalTexture("_CameraDepthTexture", d.depth); ctx.cmd.SetGlobalTexture("_HoLocalShadow", d.shadow); ctx.cmd.SetGlobalTexture("_HoVolumeLightBlueNoise", d.noise);
                        ctx.cmd.SetGlobalVector("_HoLocalPositionRange", d.positionRange); ctx.cmd.SetGlobalVector("_HoLocalDirectionType", d.directionType);
                        ctx.cmd.SetGlobalVector("_HoLocalColor", d.color); ctx.cmd.SetGlobalVector("_HoLocalAttenuation", d.attenuation);
                        ctx.cmd.SetGlobalVector("_HoVolumeLightParams", d.parameters); ctx.cmd.SetGlobalVector("_HoLocalShape", d.shape); ctx.cmd.SetGlobalVector("_HoLocalShadowParams", d.shadowParameters);
                        ctx.cmd.SetGlobalMatrix("_HoLocalWorldToLight", d.worldToLight); ctx.cmd.SetGlobalMatrixArray("_HoLocalShadowMatrices", d.shadowMatrices);
                        Blitter.BlitTexture(ctx.cmd, new Vector4(1, 1, 0, 0), d.material, 0);
                    });
                }
                source.Status = source.quality == HoVolumeLightQuality.SD ? "SD：范围 / 场景深度" : captured ? "HD：独立光源遮挡" : "HD：当前无有效 ShadowCaster";
                Count++;
            }
        }
    }
}
