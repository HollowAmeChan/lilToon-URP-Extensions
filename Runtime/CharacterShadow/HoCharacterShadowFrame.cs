using System.Collections.Generic;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.CharacterShadow
{
    // Fixed shader array lengths: never upload shorter arrays on the first camera.
    internal sealed class HoCharacterShadowFrame
    {
        internal const int MaxSlices = 16;
        internal readonly List<HoCharacterShadowSlice> slices = new List<HoCharacterShadowSlice>();
        internal readonly Vector4[] groupSlices = new Vector4[64];
        internal readonly Vector4[] partMasks = new Vector4[MaxSlices * 64];
        internal readonly Matrix4x4[] worldToShadow = new Matrix4x4[MaxSlices];
        internal readonly Matrix4x4[] worldToBounds = new Matrix4x4[MaxSlices];
        internal readonly Vector4[] tileRects = new Vector4[MaxSlices];
        internal readonly Vector4[] parameters = new Vector4[MaxSlices];
        internal int atlasSize, resolution;
        internal Light light;
        internal Vector3 cameraPosition;
        internal Matrix4x4 cameraView, cameraProjection;
        internal float filterRadius;
        internal Vector4 pcssParams, pcssParams2;

        internal static HoCharacterShadowFrame Build(Camera camera, Light light,
            Matrix4x4 cameraView, Matrix4x4 cameraProjection, HoCharacterShadowRenderConfig config)
        {
            var frame = new HoCharacterShadowFrame
            {
                light = light, resolution = config.resolution,
                cameraPosition = camera.transform.position, cameraView = cameraView,
                cameraProjection = cameraProjection, filterRadius = Mathf.Clamp(config.filterRadius, 0, 2)
            };
            // PCSS 的四个形状参数 + (深度偏移, blocker 采样数, filter 采样数, 0)。
            // 关闭时 params2 归零，shader 只看 params.x 就回退 PCF（与 ShadowCast 同一套约定）。
            frame.pcssParams = new Vector4(
                config.pcssEnabled ? 1.0f : 0.0f,
                Mathf.Max(0.0f, config.pcssSoftness),
                Mathf.Max(0.0f, config.pcssBlockerRadius),
                Mathf.Max(0.0f, config.pcssMaxPenumbraRadius));
            frame.pcssParams2 = config.pcssEnabled
                ? new Vector4(Mathf.Max(0.0f, config.pcssDepthBias), config.pcssBlockerSamples, config.pcssFilterSamples, 0.0f)
                : Vector4.zero;
            HoObjectBufferRegistry.EnsureBuilt();
            var subjects = new List<HoCharacterShadow>(HoCharacterShadow.Active);
            subjects.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            int maxSize = Mathf.Min(config.maxAtlasSize, SystemInfo.maxTextureSize);
            int columns = Mathf.FloorToInt((float)maxSize / frame.resolution);
            int capacity = Mathf.Min(Mathf.Clamp(config.maxCharacters, 1, MaxSlices), columns * columns);
            var groups = new HashSet<int>();
            var corners = new Vector3[8];
            var cameraPlanes = GeometryUtility.CalculateFrustumPlanes(camera);

            // This broad scan supplies bounds only, once per camera, never manual draw commands.
            // The native shadow renderer list below owns LOD, ShadowsOnly, TwoSided, terrain, etc.
            // Bounds are deliberately conservative; filtering by layers happens in native culling.
            var casterBounds = new List<Bounds>();
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy
                    && renderer.shadowCastingMode != ShadowCastingMode.Off
                    && (light.cullingMask & (1 << renderer.gameObject.layer)) != 0)
                    casterBounds.Add(renderer.bounds);
            foreach (Terrain terrain in Terrain.activeTerrains)
                if (terrain.terrainData != null && terrain.shadowCastingMode != ShadowCastingMode.Off
                    && (light.cullingMask & (1 << terrain.gameObject.layer)) != 0)
                {
                    Bounds bounds = terrain.terrainData.bounds;
                    bounds.center += terrain.transform.position;
                    casterBounds.Add(bounds);
                }

            foreach (HoCharacterShadow subject in subjects)
            {
                subject.atlasSlice = -1;
                var group = subject.objectGroup;
                if (group == null || !group.isActiveAndEnabled || !HoObjectBufferRegistry.IsGroupValid(group)
                    || group.groupId <= 0 || group.groupId > 255)
                { subject.status = "需要有效的 Ho-ObjectBuffer Group"; continue; }
                subject.GetWorldCorners(corners);
                Bounds visibleBounds = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < 8; i++) visibleBounds.Encapsulate(corners[i]);
                if (!GeometryUtility.TestPlanesAABB(cameraPlanes, visibleBounds))
                { subject.status = "接收盒在当前相机外"; continue; }
                if (frame.slices.Count >= capacity)
                { subject.status = "图集容量不足，使用普通天光投影"; continue; }
                if (groups.Contains(group.groupId))
                { subject.status = "同一个 OB 组只能有一个有效 CS 组件"; continue; }
                int index = frame.slices.Count;
                System.Array.Clear(frame.partMasks, index * 64, 64);
                bool hasReceiver = false;
                foreach (string name in group.GetPartNames())
                {
                    if (subject.receiverParts.Count != 0 && !subject.receiverParts.Contains(name)) continue;
                    uint identity = HoObjectBufferRegistry.GetPartId(group.groupId, name);
                    if (identity == 0) continue;
                    int slot = (int)(identity & 255);
                    frame.partMasks[index * 64 + slot / 4][slot % 4] = 1;
                    hasReceiver = true;
                }
                if (!hasReceiver) { subject.status = "没有匹配的 OB 接收部件"; continue; }
                HoCharacterShadowSlice slice = HoCharacterShadowProjection.Build(subject, light, corners,
                    casterBounds, frame.resolution, frame.filterRadius, config.depthBias, config.normalBias);
                if (slice == null) { subject.status = "包围盒变换无效"; continue; }
                groups.Add(group.groupId);
                frame.slices.Add(slice);
                frame.groupSlices[group.groupId / 4][group.groupId % 4] = index + 1;
                frame.worldToShadow[index] = slice.worldToShadow;
                frame.worldToBounds[index] = Matrix4x4.Scale(new Vector3(1 / subject.size.x, 1 / subject.size.y, 1 / subject.size.z))
                    * Matrix4x4.Translate(-subject.center) * subject.Anchor.worldToLocalMatrix;
                frame.parameters[index] = new Vector4(subject.edgeBlend, light.shadowStrength, 0, 0);
                subject.atlasSlice = index;
                subject.depthRange = slice.farPlane - slice.nearPlane;
                subject.worldUnitsPerTexel = slice.texelSize;
                subject.status = "CS 天光投影";
            }
            int side = Mathf.CeilToInt(Mathf.Sqrt(frame.slices.Count));
            frame.atlasSize = Mathf.Max(frame.resolution, side * frame.resolution);
            for (int i = 0; i < frame.slices.Count; i++)
            {
                var slice = frame.slices[i];
                slice.viewport = new Rect((i % side) * frame.resolution, (i / side) * frame.resolution, frame.resolution, frame.resolution);
                frame.tileRects[i] = new Vector4(slice.viewport.x / frame.atlasSize, slice.viewport.y / frame.atlasSize,
                    (float)frame.resolution / frame.atlasSize, (float)frame.resolution / frame.atlasSize);
            }
            return frame;
        }
    }

    internal sealed class HoCharacterShadowSlice
    {
        internal Matrix4x4 view, projection, worldToShadow;
        internal Vector3 origin;
        internal Plane[] planes;
        internal Vector4 bias;
        internal Rect viewport;
        internal float nearPlane, farPlane, texelSize;
        internal bool valid;
    }

    internal static class HoCharacterShadowProjection
    {
        internal static HoCharacterShadowSlice Build(HoCharacterShadow subject, Light light, Vector3[] corners,
            List<Bounds> casters, int resolution, float filterRadius, float depthBias, float normalBias)
        {
            if (Mathf.Abs(subject.Anchor.localToWorldMatrix.determinant) < 1e-8f) return null;
            Quaternion rotation = light.transform.rotation;
            Matrix4x4 basis = Matrix4x4.Rotate(Quaternion.Inverse(rotation));
            Vector3 min = basis.MultiplyPoint3x4(corners[0]), max = min;
            for (int i = 1; i < 8; i++)
            { Vector3 p = basis.MultiplyPoint3x4(corners[i]); min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            // Reserve texels for filtering and snapping. Never fit XY to environment bounds.
            float extent = Mathf.Max(max.x - min.x, max.y - min.y, 0.01f) * 0.5f;
            extent *= resolution / (resolution - 2 * (filterRadius + 3));
            float texel = 2 * extent / resolution;
            Vector3 center = (min + max) * 0.5f;
            center.x = Mathf.Round(center.x / texel) * texel;
            center.y = Mathf.Round(center.y / texel) * texel;
            float upstream = min.z;
            foreach (Bounds bounds in casters)
            {
                Vector3 c = basis.MultiplyPoint3x4(bounds.center), e = bounds.extents;
                Vector3 r = new Vector3(
                    Mathf.Abs(basis.m00) * e.x + Mathf.Abs(basis.m01) * e.y + Mathf.Abs(basis.m02) * e.z,
                    Mathf.Abs(basis.m10) * e.x + Mathf.Abs(basis.m11) * e.y + Mathf.Abs(basis.m12) * e.z,
                    Mathf.Abs(basis.m20) * e.x + Mathf.Abs(basis.m21) * e.y + Mathf.Abs(basis.m22) * e.z);
                if (c.x + r.x < center.x - extent || c.x - r.x > center.x + extent
                    || c.y + r.y < center.y - extent || c.y - r.y > center.y + extent || c.z - r.z > max.z) continue;
                upstream = Mathf.Min(upstream, c.z - r.z);
            }
            float padding = Mathf.Max(0.1f, texel * (depthBias + normalBias + 4));
            const float near = 0.01f;
            Vector3 origin = rotation * new Vector3(center.x, center.y, upstream - padding - near);
            var slice = new HoCharacterShadowSlice
            {
                origin = origin, nearPlane = near, farPlane = max.z - upstream + padding * 2 + near,
                texelSize = texel,
                view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(origin, rotation, Vector3.one).inverse,
                bias = new Vector4(-Mathf.Max(0, depthBias) * texel, -Mathf.Max(0, normalBias) * texel, (float)LightType.Directional, 0)
            };
            slice.projection = Matrix4x4.Ortho(-extent, extent, -extent, extent, near, slice.farPlane);
            slice.planes = GeometryUtility.CalculateFrustumPlanes(slice.projection * slice.view);
            Matrix4x4 projection = slice.projection;
            if (SystemInfo.usesReversedZBuffer)
                for (int column = 0; column < 4; column++) projection[2, column] = -projection[2, column];
            Matrix4x4 scaleBias = Matrix4x4.identity;
            scaleBias.m00 = scaleBias.m11 = scaleBias.m22 = 0.5f;
            scaleBias.m03 = scaleBias.m13 = scaleBias.m23 = 0.5f;
            slice.worldToShadow = scaleBias * projection * slice.view;
            return slice;
        }
    }
}
