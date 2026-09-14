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
            /// <summary>置位表示 texture 已被我们主动销毁，条目仍在字典中待重建。</summary>
            public bool textureDestroyed;
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
                DestroyEntryTexture(pair.Value);
            }

            tables.Clear();
            staleCameras.Clear();
        }

        public void Dispose()
        {
            Release();
        }

        /// <summary>
        /// 销毁条目纹理并把条目标记为“待重建”，而不是把条目本身从字典里摘掉：
        /// 销毁后条目仍可能被同一帧再次取用（例如切场景时相机对象已被销毁、
        /// 而它的表仍以键的形式留在字典里），留标记才能让 Upload 正确地重建而不是访问已销毁对象。
        /// </summary>
        private static void DestroyEntryTexture(TableEntry entry)
        {
            if (entry.textureDestroyed)
            {
                return;
            }

            if (entry.texture != null)
            {
                CoreUtils.Destroy(entry.texture);
            }

            entry.texture = null;
            entry.textureDestroyed = true;
        }

        /// <summary>
        /// 上传该相机表并立即绑定为全局纹理：AddRenderPasses 时机先于本相机所有 pass（含 RenderGraph composite）
        /// 记录与执行，本相机的 composite 采样到的就是本相机的表。已验证该路径下表内容可被读到，
        /// 不要改用 Unsafe pass / RenderTargetIdentifier(Texture2D) 绑定（实测会把表读成全黑）。
        /// 上传前先自愈：条目里的 texture 若已被销毁（Release/场景卸载触发资源回收等），
        /// 单帧内在原条目上重建，避免 MissingReferenceException 让整个相机渲染中断。
        /// </summary>
        private static void Upload(TableEntry entry)
        {
            bool created = EnsureTexture(entry);
            entry.texture.SetPixelData(entry.data, 0);
            entry.texture.Apply(false, false);
            Shader.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeAngleTextureId, entry.texture);
            if (created)
            {
                // 缓存纹理被销毁说明上一次写入已失效，下一个相机要重写一遍。
                entry.cleared = false;
            }
        }

        private static bool EnsureTexture(TableEntry entry)
        {
            if (entry.texture != null && !entry.textureDestroyed)
            {
                return false;
            }

            entry.texture = CreateTableTexture();
            entry.textureDestroyed = false;
            return true;
        }

        private static Texture2D CreateTableTexture()
        {
            return new Texture2D(
                CharacterCount,
                1,
                TextureFormat.RGBAFloat,
                false,
                false)
            {
                name = HoCharacterSpecializationShaderConstants.EyeAngleTextureName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private TableEntry GetOrCreateEntry(Camera camera)
        {
            if (!tables.TryGetValue(camera, out TableEntry entry))
            {
                entry = new TableEntry
                {
                    data = new float[CharacterCount * FloatCountPerCharacter]
                };
                tables.Add(camera, entry);
            }

            // 纹理延迟到 Upload 内创建：条目可能是新建的，也可能是残留着已销毁纹理的旧条目
            // （例如上一次 Upload 之后纹理被 Release/资源回收销毁），两种情况都在 Upload 里统一校验与重建。
            return entry;
        }

        /// <summary>
        /// 清理已销毁相机的表。判活必须用 ReferenceEquals 而不是裸的 <c>camera == null</c>：
        /// Unity 的 <c>UnityEngine.Object.==</c> 被重载为"两边只要有一边是已销毁对象就返回 true"，
        /// 对"已销毁相机 vs 存活相机"这种比较同样返回 true，会让仍然健在的相机的表被误判为 stale 销毁掉，
        /// 紧接着的 Upload 就落到已销毁纹理上。ReferenceEquals 只认真正的托管空引用，不会误伤。
        /// </summary>
        private void RemoveStaleTables()
        {
            staleCameras.Clear();
            foreach (Camera camera in tables.Keys)
            {
                if (ReferenceEquals(camera, null) || camera == null)
                {
                    staleCameras.Add(camera);
                }
            }

            for (int i = 0; i < staleCameras.Count; i++)
            {
                Camera stale = staleCameras[i];
                if (tables.TryGetValue(stale, out TableEntry entry))
                {
                    DestroyEntryTexture(entry);
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
