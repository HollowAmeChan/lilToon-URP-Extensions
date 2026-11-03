# 已制作场景的烘焙 / HoShadowCast 退役对照

任务状态：执行中。不要将用户预期的“更好看、更快”预先当作结论。

输入是 BREAK_URP 的花吐电気通用动画、圣诞万花镜、孔雀月令。先导出只读依赖清单，再在 `.codex-research/LightingBakeAudit-20261102` 中物理复制资源与渲染包快照。原 Scene、材质和共享 PC Renderer 保持原状。

对照至少区分：

1. 原 HoShadowCast 开启的作者配置。
2. 只关 HoShadowCast，其他照明保持一致。
3. 关闭 HoShadowCast、烘焙 Lightmap + 传统 Probe。
4. 关闭 HoShadowCast、烘焙 Lightmap + APV。

烘焙替代 RTGI 的收益须与单独关闭 ShadowCast 分开报告。静态舞台、动态角色受光、角色投影、体积光、反射分别检查。没有有效 GPU 时间不能用 Editor 墙钟冒充 GPU 时间；所有比较记录分辨率、预热、采样数、质量和烘焙耗时。

本机阶段性输入与输出在 `Temp~/LightingBakeAudit-20261102/`。最终结论、图和性能数据在完成后补充。

## 当前证据（2026-11-02）

测试只使用隔离副本 `D:\HoBakeAudit26`，固定相机与 VMD 第 240 帧姿态；没有评审角色运动或修改 BREAK_URP 原始场景。用户的验收约束是：角色只接收 Main ShadowCast；场景接收端可以选 Main 或 Full；阴影路由不得关闭或削弱多光源颜色 / 强度。

花吐电気的 A/B 使用相同场景、灯光和 RTGI，只切换 HoShadowCast。B 的额外光颜色仍由 lilToon 的 `_MultiLightIntensity` 路径提供，差别来自 HoShadowCast 阴影衰减消失；固定相机图像已输出在 `Temp~/LightingBakeAudit-20261102/花吐电気/{A,B}/main.png`。这验证了全局关闭 feature 的效果，但当前开关没有按角色 / 场景区分接收范围。

lilToon 的材质面板将 `_MultiLightIntensity` 与 `_MultiLightCastShadowStrength` 分开；后者控制 URP 附加光阴影衰减。HoShadowCast 目前只有一份全局衰减：采样结果乘在合成后的附加光 HDR 上，不能选择 Main 或 Full。`_ShadowReceive`、`_Shadow2ndReceive`、`_Shadow3rdReceive` 是 lilToon 的 toon 阴影层控制，不是 Ho ShadowCast 来源选择。灯光采集路径也没有检查 `Light.shadows`：`IsLightCollectable` 检查启用状态、类型及 Layer / Rendering Layer，因此 `shadows=None` 的灯仍可能进入 HoShadowCast 候选。

Lightmap + 探针的烘焙均成功。每组均使用 16 texels/unit、128 direct / 512 indirect / 256 environment samples、最多 4 次反弹；表中时间是编辑器烘焙总耗时，APV 还包含 APV 数据烘焙。

| 场景 | Lightmap | 传统 Probe | 传统 Probe 耗时 | APV 耗时 | 烘焙资产目录大小 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 花吐电気 | 2 | 36 | 40.6 s | 73.7 s | 12.13 MiB / 15.44 MiB |
| 圣诞万花镜 | 1 | 1,624 | 47.7 s | 264.6 s | 30.99 MiB / 76.21 MiB |
| 孔雀月令 | 3 | 315 | 16.3 s | 38.9 s | 未统计 |

目录大小依次为传统 Probe / APV，包含测试场景、Lightmap 与对应探针数据，不是纯运行时内存。RTGI 关闭时，传统 Probe 和 APV 两种烘焙都完成；对应固定机位渲染分别为 E / F。

花吐电気在 RTX 4060 / D3D12 的编辑器 GPU marker 结果：

| 模式 | HoShadowCast 平均 / P95 | RTGI 平均 / P95 |
| --- | ---: | ---: |
| A：原配置 | 1.75 / 4.04 ms（107 帧） | 13.52 / 19.87 ms（107 帧） |
| B：只关闭 HoShadowCast | 无 | 13.43 / 19.53 ms（107 帧） |
| C：传统 Probe + Lightmap，RTGI 开 | 无 | 12.91 / 19.22 ms（107 帧） |
| D：APV + Lightmap，RTGI 开 | 无 | 14.02 / 19.33 ms（107 帧） |
| E：传统 Probe + Lightmap，RTGI 关 | 无 | 无 |
| F：APV + Lightmap，RTGI 关 | 无 | 无 |

这只量到 feature GPU marker，不能当作整帧 GPU 时间。编辑器 `FrameTimingManager` 样本为 0；隔离 Player 已能按构建索引加载场景，但当前 Player 帧计时也为 0 个有效样本，自动截图为黑帧，因此不纳入画质或总帧性能结论。现有有效证据只支持：关闭 HoShadowCast 移除约 1.75 ms 平均 GPU pass；RTGI 是明显更大的约 13 ms pass，Lightmap / Probe 本身不会关闭它。

## 暂定结论

Lightmap + 传统 Probe / APV 能烘焙出静态场景光照，且关闭 RTGI 后可移除 RTGI pass；但单凭这些数据还不能安全退役 HoShadowCast。当前 HoShadowCast 是全局合成阴影入口，既没有角色 Main-only / 场景 Main-or-Full 的接收选项，也不尊重 `Light.shadows=None`。需要先把 Main 与 Full 的灯光池、材质接收路由和独立的颜色照明路径定清并验证；在此之前，退役会失去可控的动态场景补充阴影。性能结论限于 pass 级，整帧对比仍待可用的 GPU 帧计时方案。
