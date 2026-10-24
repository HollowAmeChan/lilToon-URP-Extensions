# 2026-10-24 血月古堡体积光失去遮挡：过程、复核与交接

## 修改前 review

用户观察到物体不再把天光切成明暗光束。实际启用的是 Ho-VolumeLight Solar HD，旧 ScreenProcess/SkyTyndall 关闭，不应仅凭“丁达尔”的名字修改旧 Shader。

当前 SceneCamera 为 (12.09, 9.63, 58.67)，PC_RPAsset 的方向光阴影距离 50 m；VolumeLight 积分距离 100 m、256 步、时域稳定。主方向光 Soft、strength=1，运行时 `_HoVolumeLightShadow=(1,1,0,0)`，级联关键词有效。不是主光阴影开关关闭或 RTDI/RTGI 公共 NRD 改造导致输入缺失。

先验证：隔离工程旧天空遮罩在 lilToon、lilToonCutout、URP Unlit 三种材质下均正常；现有 VolumeLight GPU 用例验证阴影、镂空、屏外投影、SDF 区间、屏幕空间阴影、深度及重建通过。隔离工程初次完整用例因灯光强度空间配置失败，后续 wrapper 明确设 `GraphicsSettings.lightsUseLinearIntensity=true`，未改产品代码或放宽门槛。

方案：仅把当前项目 `Assets/Settings/PC_RPAsset.asset` 的阴影距离由 50 m 调为 100 m，与体积光工作距离一致；不改包内积分/阴影语义。100 m 与 150 m 的对照已表明遮挡恢复，先用开销较小的 100 m。阴影 atlas 分辨率不变，较远覆盖会分摊其精度并可能增加投影物数量，必须检查实际画面。

## GPU 取证

同一实际 SceneView，通过临时 Editor 脚本读取 VolumeLight 自有时域历史，逐组等待 16 次 SceneView 渲染，记录原始红通道浮点数据与 EXR。依次 50/100/150 m 和原距离关闭主光阴影；完成后自动还原全部临时设置。

| 配置 | 体积光 R 均值 | 峰值 |
| --- | ---: | ---: |
| 原始 50 m | 0.10958347 | 0.1523438 |
| 100 m | 0.01921669 | 0.0352478 |
| 150 m | 0.00147197 | 0.01145172 |
| 50 m、关闭阴影 | 0.10957939 | 0.1523438 |

50 m 与无阴影均值几乎相同；100 m 均值为原来的 17.54%，150 m 为 1.34%。这说明当前视角确实主要采到了阴影覆盖以外的空气。不是“光束算法完全没有阴影”。这些是当前场景/视角的体积贡献，不是整幅合成图亮度，也不是性能收益。

证据在 `Tests~/VolumeLight/SkyOcclusion-20261024`。`comparison.txt` 的 mean/max 有效；旧临时采集脚本的 baseline 数组被 PNG 色调映射原地修改，故 `deltaMean/reductionVsBase` 列无效，不可用来宣称像素差异。原始 `.float`/EXR 没有经过该修改。

## 修改后 review

已保存项目 `PC_RPAsset.asset` 的 100 m 设置；与备份执行逐行 diff，只有 `m_ShadowDistance: 50 → 100` 一处变化。Unity 实际 SceneView 复核：原来的均匀泛灰明显消退，遮挡重新生效。当前视角不保证窗孔光束造型符合艺术预期；那仍取决于太阳方向、模型透光孔与雾区域。没有修改公共 Shader。完整 Solar GPU 用例通过。临时 Editor 诊断脚本与 meta 已移除；未保存场景、Volume 或其他资产。

## 踩坑禁言与后续交接

- 不再混淆 ScreenProcess/SkyTyndall 与 Ho-VolumeLight Solar HD；先确认实际启用的模块。
- 不可在 RenderGraph 执行完后用 `Shader.GetGlobalTexture` 得到 UnityBlack 就认定 pass 输入是黑图；此处必须在图内探测或读取明确拥有的持久 RT。
- 不可用被 PNG 预览转换原地修改过的 baseline 数组计算差异；保留 raw 数组或 clone。
- 阴影范围外按受光处理是当前明确定义的行为。不能为掩盖范围不足把阴影外空气强制变黑，也不能自动修改全项目阴影距离。
- 原始资产应先备份；本次项目配置与包仓库提交分开记录，保持原有 RTGI 047 草稿和 PlanarReference 未提交状态。
