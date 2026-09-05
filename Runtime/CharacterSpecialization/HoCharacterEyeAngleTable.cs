using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    /// <summary>
    /// 每个 characterId 一行 (平转角, 俯仰角) 的相机角度查询表，每个渲染相机各持一份。
    /// 眼睛透过在 Composite 中按眼睛捕获的角色 ID（预乘取回）采样本表，
    /// 得到相机相对该角色面部朝向的平转/俯仰角后做视锥内/外判定。
    /// 表内容由 CPU 在每条相机渲染前（AddRenderPasses，URP17 fork 主路径的唯一时机）
    /// 按当前渲染相机与 HoMetadataBufferGroup 提供的世界朝向计算，
    /// 并把“该相机的表”设为全局纹理 _lilHoCharacterEyeAngleTable。
    /// 因为“写表→本相机 composite 读取”在每条相机的渲染循环内顺序成对，
    /// 多相机（Scene 视图 + 游戏相机、多屏、录制相机）各自使用自己的表，互不干扰，无需任何“活动相机”判定。
    /// 行号 = group.characterId (0-255)；同一 characterId 有多个组且都提供了朝向时，后写者覆盖。
    /// 未提供朝向或未启用时行数据为 (0,0)，曲线因子恒为 1（等价于不修正）。
    /// </summary>
    internal sealed class HoCharacterEyeAngleTable : IDisposable
    {
        public const int CharacterCount = 256;
        private const int FloatCountPerCharacter = 4; // RGBAFloat：每字符 4 个 float，前两个存 yaw/pitch

        private sealed class TableEntry
        {
            public Texture2D texture;
            public float[] data;
            public bool cleared;
        }

        private readonly Dictionary<Camera, TableEntry> tables = new Dictionary<Camera, TableEntry>();
        private readonly List<Camera> staleCameras = new List<Camera>();

        public void UpdateForCamera(Camera camera, HoCharacterSpecializationSettings settings)
        {
            if (camera == null)
            {
                return;
            }

            RemoveStaleTables();

            TableEntry entry = GetOrCreateEntry(camera);
            bool enabled = settings != null && settings.eyeRevealAngleEnabled;
            if (!enabled)
            {
                if (entry.cleared)
                {
                    return;
                }

                Array.Clear(entry.data, 0, entry.data.Length);
                entry.cleared = true;
                Upload(entry);
                return;
            }

            Array.Clear(entry.data, 0, entry.data.Length);
            Vector3 cameraPosition = camera.transform.position;
            IReadOnlyList<HoMetadataBufferGroup> groups = HoMetadataBufferGroup.GetActiveGroups();
            for (int i = 0; i < groups.Count; i++)
            {
                HoMetadataBufferGroup group = groups[i];
                if (group == null || !group.isActiveAndEnabled)
                {
                    continue;
                }

                // 朝向数据由组件提供（后续 SDF 等系统复用同一入口），这里只做相机相关角度分解。
                if (!group.TryGetWorldFacing(out Vector3 origin, out Vector3 forward, out Vector3 right, out Vector3 up))
                {
                    continue;
                }

                ComputeAngles(cameraPosition, origin, forward, right, up, out float yaw, out float pitch);
                int charId = Mathf.Clamp(group.characterId, 0, CharacterCount - 1);
                int index = charId * FloatCountPerCharacter;
                entry.data[index] = yaw;
                entry.data[index + 1] = pitch;
            }

            Upload(entry);
        }

        public void Release()
        {
            foreach (KeyValuePair<Camera, TableEntry> pair in tables)
            {
                if (pair.Value?.texture != null)
                {
                    CoreUtils.Destroy(pair.Value.texture);
                }
            }

            tables.Clear();
            staleCameras.Clear();
        }

        public void Dispose()
        {
            Release();
        }

        /// <summary>
        /// 上传该相机表并立即绑定为全局纹理：AddRenderPasses 时机先于本相机所有 pass（含 RenderGraph composite）
        /// 记录与执行，本相机的 composite 采样到的就是本相机的表。已验证该路径下表内容可被读到，
        /// 不要改用 Unsafe pass / RenderTargetIdentifier(Texture2D) 绑定（实测会把表读成全黑）。
        /// </summary>
        private static void Upload(TableEntry entry)
        {
            entry.texture.SetPixelData(entry.data, 0);
            entry.texture.Apply(false, false);
            Shader.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeAngleTextureId, entry.texture);
        }

        private TableEntry GetOrCreateEntry(Camera camera)
        {
            if (!tables.TryGetValue(camera, out TableEntry entry))
            {
                entry = new TableEntry
                {
                    texture = new Texture2D(
                        CharacterCount,
                        1,
                        TextureFormat.RGBAFloat,
                        false,
                        false)
                    {
                        name = HoCharacterSpecializationShaderConstants.EyeAngleTextureName + "_" + camera.name,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    },
                    data = new float[CharacterCount * FloatCountPerCharacter]
                };
                tables.Add(camera, entry);
            }

            return entry;
        }

        private void RemoveStaleTables()
        {
            staleCameras.Clear();
            foreach (Camera camera in tables.Keys)
            {
                if (camera == null)
                {
                    staleCameras.Add(camera);
                }
            }

            for (int i = 0; i < staleCameras.Count; i++)
            {
                Camera stale = staleCameras[i];
                if (tables.TryGetValue(stale, out TableEntry entry) && entry?.texture != null)
                {
                    CoreUtils.Destroy(entry.texture);
                }

                tables.Remove(stale);
            }
        }

        private static void ComputeAngles(
            Vector3 cameraPosition,
            Vector3 facingOrigin,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            out float yawDegrees,
            out float pitchDegrees)
        {
            Vector3 vdir = cameraPosition - facingOrigin;
            if (vdir.sqrMagnitude < 1e-8f)
            {
                yawDegrees = 0.0f;
                pitchDegrees = 0.0f;
                return;
            }

            vdir.Normalize();
            // 平转角（yaw）：相机方向在“脸前-右”平面内绕竖直轴的转动，atan2 全角域 ±180°。
            float yaw = Mathf.Atan2(Vector3.Dot(vdir, right), Vector3.Dot(vdir, forward));
            // 俯仰角（pitch）：相机方向在“脸前-上”平面内的转动，同样 atan2，±180° 全角域（无 asin 截断）。
            float pitch = Mathf.Atan2(Vector3.Dot(vdir, up), Vector3.Dot(vdir, forward));
            yawDegrees = yaw * Mathf.Rad2Deg;
            pitchDegrees = pitch * Mathf.Rad2Deg;
        }
    }
}
