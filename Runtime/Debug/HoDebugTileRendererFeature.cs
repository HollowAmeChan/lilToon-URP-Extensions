#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.MetadataBuffer;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.SubsurfaceScattering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Debugging
{
    [DisallowMultipleRendererFeature("Ho-DebugTile")]
    public sealed class HoDebugTileRendererFeature : ScriptableRendererFeature
    {
        public const string NoneViewId = "";
        public const string AllRegisteredViewId = "__AllRegistered";

        [SerializeField]
        private bool enabledForGameView = true;

        [SerializeField]
        private bool enabledForSceneView = true;

        [SerializeField]
        private RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [SerializeField]
        [InspectorName("Geometry Depth Near")]
        private float geometryDepthNear = 0.0f;

        [SerializeField]
        [InspectorName("Geometry Depth Far")]
        private float geometryDepthFar = 100.0f;

        [SerializeField]
        private string selectedDebugViewId = NoneViewId;

        private HoDebugTilePass pass;
        private Shader shader;
        private Material material;
        private bool warnedMissingShader;

        public override void Create()
        {
            pass = new HoDebugTilePass();
            EnsureMaterial();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            EnsureMaterial();
            if (pass == null || material == null)
            {
                return;
            }

            pass.Setup(material, selectedDebugViewId, passEvent, GetGeometryDepthParams());
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
            shader = null;
            pass = null;
        }

        private bool ShouldRender(in RenderingData renderingData)
        {
            if (string.IsNullOrEmpty(selectedDebugViewId))
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return (enabledForGameView && cameraType == CameraType.Game)
                || (enabledForSceneView && cameraType == CameraType.SceneView);
        }

        private void EnsureMaterial()
        {
            Shader currentShader = Shader.Find(HoDebugTileShaderConstants.ShaderName);
            if (material != null && shader == currentShader)
            {
                return;
            }

            CoreUtils.Destroy(material);
            material = null;
            shader = currentShader;
            if (shader == null)
            {
                if (!warnedMissingShader)
                {
                    warnedMissingShader = true;
                    Debug.LogWarning($"Debug tile view is unavailable because shader '{HoDebugTileShaderConstants.ShaderName}' could not be found.");
                }

                return;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
        }

        private Vector4 GetGeometryDepthParams()
        {
            float near = Mathf.Max(0.0f, geometryDepthNear);
            float far = Mathf.Max(near + 0.0001f, geometryDepthFar);
            return new Vector4(near, far, 1.0f / (far - near), 0.0f);
        }

        private sealed class HoDebugTilePass : ScriptableRenderPass
        {
            private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-DebugTile");
            private static readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();

            private Material material;
            private string selectedDebugViewId;
            private Vector4 geometryDepthParams;

            public HoDebugTilePass()
            {
                ConfigureInput(ScriptableRenderPassInput.None);
                requiresIntermediateTexture = true;
            }

            public void Setup(Material material, string selectedDebugViewId, RenderPassEvent passEvent, Vector4 geometryDepthParams)
            {
                this.material = material;
                this.selectedDebugViewId = selectedDebugViewId;
                this.geometryDepthParams = geometryDepthParams;
                renderPassEvent = passEvent;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (material == null)
                {
                    return;
                }

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
                HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
                HoShadowCastRenderGraphResources shadowCastResources = frameData.GetOrCreate<HoShadowCastRenderGraphResources>();
                HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();

                bool hasMetadata = metadataResources.maskIdTexture.IsValid()
                    && metadataResources.surfaceDataTexture.IsValid()
                    && metadataResources.custom0Texture.IsValid()
                    && metadataResources.objectCustom0Texture.IsValid()
                    && metadataResources.objectCustom1Texture.IsValid()
                    && metadataResources.surfaceColorTexture.IsValid();
                bool hasGeometry = geometryResources.normalDepthTexture.IsValid();
                bool hasShadowCastAtlas = shadowCastResources.atlasTexture.IsValid();
                bool hasShadowCastSecondDirectionalAtlas = shadowCastResources.secondDirectionalAtlasTexture.IsValid();
                bool hasSubsurfaceScattering = sssResources.sourceTexture.IsValid()
                    && sssResources.transmissionTexture.IsValid()
                    && metadataResources.HasRequiredTextures
                    && geometryResources.HasRequiredTextures;

                List<DebugTile> tiles = BuildTiles(
                    selectedDebugViewId,
                    hasMetadata,
                    hasGeometry,
                    hasShadowCastAtlas,
                    hasShadowCastSecondDirectionalAtlas,
                    hasSubsurfaceScattering);
                if (tiles.Count == 0)
                {
                    return;
                }

                TextureHandle destination = resourceData.activeColorTexture;
                if (!destination.IsValid())
                {
                    return;
                }

                int targetWidth = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
                int targetHeight = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
                float targetAspect = targetWidth / (float)targetHeight;
                CalculateTileGrid(tiles.Count, targetAspect, out int columns, out int rows);

                for (int i = 0; i < tiles.Count; i++)
                {
                    DebugTile tile = tiles[i];
                    int column = i % columns;
                    int row = i / columns;
                    tiles[i] = new DebugTile(tile.renderKind, tile.modeValue, new LabelData(tile.label0, tile.label1, tile.label2, tile.label3), CalculateTileRect(column, row, columns, rows));
                }

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-DebugTile", out PassData passData, ProfilingSampler))
                {
                    passData.material = material;
                    passData.tiles = tiles;
                    passData.tileGrid = new Vector4(columns, rows, tiles.Count, 0.0f);
                    passData.hasMetadata = hasMetadata;
                    passData.hasGeometry = hasGeometry;
                    passData.hasShadowCastAtlas = hasShadowCastAtlas;
                    passData.hasShadowCastSecondDirectionalAtlas = hasShadowCastSecondDirectionalAtlas;
                    passData.hasSubsurfaceScattering = hasSubsurfaceScattering;
                    passData.maskIdTexture = metadataResources.maskIdTexture;
                    passData.surfaceDataTexture = metadataResources.surfaceDataTexture;
                    passData.custom0Texture = metadataResources.custom0Texture;
                    passData.objectCustom0Texture = metadataResources.objectCustom0Texture;
                    passData.objectCustom1Texture = metadataResources.objectCustom1Texture;
                    passData.surfaceColorTexture = metadataResources.surfaceColorTexture;
                    passData.normalDepthTexture = geometryResources.normalDepthTexture;
                    passData.shadowCastAtlasTexture = shadowCastResources.atlasTexture;
                    passData.shadowCastSecondDirectionalAtlasTexture = shadowCastResources.secondDirectionalAtlasTexture;
                    passData.sssSourceTexture = sssResources.sourceTexture;
                    passData.sssTransmissionTexture = sssResources.transmissionTexture;
                    passData.geometryDepthParams = geometryDepthParams;

                    if (hasMetadata)
                    {
                        builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                        builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                        builder.UseTexture(passData.custom0Texture, AccessFlags.Read);
                        builder.UseTexture(passData.objectCustom0Texture, AccessFlags.Read);
                        builder.UseTexture(passData.objectCustom1Texture, AccessFlags.Read);
                        builder.UseTexture(passData.surfaceColorTexture, AccessFlags.Read);
                    }

                    if (hasGeometry)
                    {
                        builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                    }

                    if (hasShadowCastAtlas)
                    {
                        builder.UseTexture(passData.shadowCastAtlasTexture, AccessFlags.Read);
                    }

                    if (hasShadowCastSecondDirectionalAtlas)
                    {
                        builder.UseTexture(passData.shadowCastSecondDirectionalAtlasTexture, AccessFlags.Read);
                    }

                    if (hasSubsurfaceScattering)
                    {
                        builder.UseTexture(passData.sssSourceTexture, AccessFlags.Read);
                        builder.UseTexture(passData.sssTransmissionTexture, AccessFlags.Read);
                        if (!hasMetadata)
                        {
                            builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                            builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                        }

                        if (!hasGeometry)
                        {
                            builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                        }
                    }

                    builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.black, 1.0f, 0);
                        if (data.hasMetadata)
                        {
                            context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.maskIdTexture);
                            context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                            context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.Custom0TextureId, data.custom0Texture);
                            context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.objectCustom0Texture);
                            context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.objectCustom1Texture);
                            context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceColorTextureId, data.surfaceColorTexture);
                        }

                        if (data.hasGeometry)
                        {
                            context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                        }

                        if (data.hasShadowCastAtlas)
                        {
                            context.cmd.SetGlobalTexture(HoShadowCastShaderConstants.AtlasTextureId, data.shadowCastAtlasTexture);
                        }

                        if (data.hasShadowCastSecondDirectionalAtlas)
                        {
                            context.cmd.SetGlobalTexture(HoShadowCastShaderConstants.SecondDirectionalAtlasTextureId, data.shadowCastSecondDirectionalAtlasTexture);
                        }

                        if (data.hasSubsurfaceScattering)
                        {
                            context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.maskIdTexture);
                            context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                            context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                            context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, data.sssSourceTexture);
                            context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.TransmissionTextureId, data.sssTransmissionTexture);
                        }

                        for (int i = 0; i < data.tiles.Count; i++)
                        {
                            DebugTile tile = data.tiles[i];
                            PropertyBlock.Clear();
                            PropertyBlock.SetInt(HoDebugTileShaderConstants.RenderKindId, (int)tile.renderKind);
                            PropertyBlock.SetInt(HoDebugTileShaderConstants.ModeId, tile.modeValue);
                            PropertyBlock.SetVector(HoDebugTileShaderConstants.RectId, tile.tileRect);
                            PropertyBlock.SetVector(HoDebugTileShaderConstants.GridId, data.tileGrid);
                            PropertyBlock.SetVector(HoDebugTileShaderConstants.Label0Id, tile.label0);
                            PropertyBlock.SetVector(HoDebugTileShaderConstants.Label1Id, tile.label1);
                            PropertyBlock.SetVector(HoDebugTileShaderConstants.Label2Id, tile.label2);
                            PropertyBlock.SetVector(HoDebugTileShaderConstants.Label3Id, tile.label3);
                            PropertyBlock.SetVector(HoDebugTileShaderConstants.GeometryDepthParamsId, data.geometryDepthParams);
                            context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 6, 1, PropertyBlock);
                        }
                    });
                }
            }

            private static List<DebugTile> BuildTiles(
                string selectedId,
                bool hasMetadata,
                bool hasGeometry,
                bool hasShadowCastAtlas,
                bool hasShadowCastSecondDirectionalAtlas,
                bool hasSubsurfaceScattering)
            {
                List<DebugTile> tiles = new List<DebugTile>();
                IReadOnlyList<HoDebugViewInfo> views = HoDebugViewRegistry.AllViews;
                bool all = selectedId == AllRegisteredViewId;
                for (int i = 0; i < views.Count; i++)
                {
                    HoDebugViewInfo view = views[i];
                    if (!view.SupportsAutomaticTile || (!all && view.ViewId != selectedId))
                    {
                        continue;
                    }

                    if (view.RenderKind == HoDebugViewRenderKind.MetadataBuffer && !hasMetadata)
                    {
                        continue;
                    }

                    if (view.RenderKind == HoDebugViewRenderKind.GeometryBuffer && !hasGeometry)
                    {
                        continue;
                    }

                    if (view.RenderKind == HoDebugViewRenderKind.ShadowCast
                        && ((view.ModeValue == (int)HoShadowCastDebugMode.Atlas && !hasShadowCastAtlas)
                            || (view.ModeValue == (int)HoShadowCastDebugMode.SecondDirectionalAtlas && !hasShadowCastSecondDirectionalAtlas)))
                    {
                        continue;
                    }

                    if (view.RenderKind == HoDebugViewRenderKind.SubsurfaceScattering && !hasSubsurfaceScattering)
                    {
                        continue;
                    }

                    tiles.Add(new DebugTile(view.RenderKind, view.ModeValue, EncodeLabel(view.ShortName)));
                }

                return tiles;
            }

            private static void CalculateTileGrid(int tileCount, float targetAspect, out int columns, out int rows)
            {
                int preferredColumns = targetAspect >= 1.45f ? 5 : 4;
                columns = Mathf.Clamp(preferredColumns, 1, Mathf.Max(1, tileCount));
                rows = Mathf.CeilToInt(tileCount / (float)columns);
            }

            private static Vector4 CalculateTileRect(int column, int row, int columns, int rows)
            {
                float cellWidth = 1.0f / columns;
                float cellHeight = 1.0f / rows;
                float gutter = Mathf.Min(cellWidth, cellHeight) * 0.035f;
                return new Vector4(
                    column * cellWidth + gutter,
                    row * cellHeight + gutter,
                    Mathf.Max(0.0001f, cellWidth - gutter * 2.0f),
                    Mathf.Max(0.0001f, cellHeight - gutter * 2.0f));
            }

            private static LabelData EncodeLabel(string label)
            {
                return new LabelData(
                    PackLabelChunk(label, 0),
                    PackLabelChunk(label, 4),
                    PackLabelChunk(label, 8),
                    PackLabelChunk(label, 12));
            }

            private static Vector4 PackLabelChunk(string label, int start)
            {
                return new Vector4(
                    LabelCharCode(label, start),
                    LabelCharCode(label, start + 1),
                    LabelCharCode(label, start + 2),
                    LabelCharCode(label, start + 3));
            }

            private static float LabelCharCode(string label, int index)
            {
                if (string.IsNullOrEmpty(label) || index < 0 || index >= label.Length)
                {
                    return ' ';
                }

                char c = label[index];
                if (c >= 'a' && c <= 'z')
                {
                    return c - 32;
                }

                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    return c;
                }

                return ' ';
            }

            private readonly struct DebugTile
            {
                public DebugTile(HoDebugViewRenderKind renderKind, int modeValue, LabelData label)
                    : this(renderKind, modeValue, label, Vector4.zero)
                {
                }

                public DebugTile(HoDebugViewRenderKind renderKind, int modeValue, LabelData label, Vector4 tileRect)
                {
                    this.renderKind = renderKind;
                    this.modeValue = modeValue;
                    label0 = label.label0;
                    label1 = label.label1;
                    label2 = label.label2;
                    label3 = label.label3;
                    this.tileRect = tileRect;
                }

                public readonly HoDebugViewRenderKind renderKind;
                public readonly int modeValue;
                public readonly Vector4 label0;
                public readonly Vector4 label1;
                public readonly Vector4 label2;
                public readonly Vector4 label3;
                public readonly Vector4 tileRect;
            }

            private readonly struct LabelData
            {
                public LabelData(Vector4 label0, Vector4 label1, Vector4 label2, Vector4 label3)
                {
                    this.label0 = label0;
                    this.label1 = label1;
                    this.label2 = label2;
                    this.label3 = label3;
                }

                public readonly Vector4 label0;
                public readonly Vector4 label1;
                public readonly Vector4 label2;
                public readonly Vector4 label3;
            }

            private sealed class PassData
            {
                public Material material;
                public List<DebugTile> tiles;
                public Vector4 tileGrid;
                public bool hasMetadata;
                public bool hasGeometry;
                public bool hasShadowCastAtlas;
                public bool hasShadowCastSecondDirectionalAtlas;
                public bool hasSubsurfaceScattering;
                public TextureHandle maskIdTexture;
                public TextureHandle surfaceDataTexture;
                public TextureHandle custom0Texture;
                public TextureHandle objectCustom0Texture;
                public TextureHandle objectCustom1Texture;
                public TextureHandle surfaceColorTexture;
                public TextureHandle normalDepthTexture;
                public TextureHandle shadowCastAtlasTexture;
                public TextureHandle shadowCastSecondDirectionalAtlasTexture;
                public TextureHandle sssSourceTexture;
                public TextureHandle sssTransmissionTexture;
                public Vector4 geometryDepthParams;
            }
        }
    }
}
