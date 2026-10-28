# Ho-CharacterShadow：当前栅格化角色投影

> 状态：**现行实现与使用入口**，2026-10-29。CS 使用角色局部 shadow map、PCF / PCSS；修复基线 `541d06f2` 不代表所有角色近景画质已验收。独立 [Ho-RT / RTShadow / SIGMA](Ho-RT.md) 已实现并有有界 GPU / Player 验收，需单独配置，不自动替换本模块。

## 功能与接入

CS 为角色接收盒生成高精度的主方向光实时投影。局部覆盖有效时，lilToon 用 CS 可见度替换原主光实时阴影采样；盒外、容量不足或组件失效时回退普通主光投影。它沿用材质现有的阴影接收、Mask、各层强度与 toon 着色，不增加一套阴影色模型。

这里的主光是 URP 主方向光，不包括天空 SH / APV / 环境漫反射。角色之外的场景几何、其他角色和离屏遮挡物，只要符合该灯的普通投影规则，都可以成为 caster。

1. 在 Renderer Data 添加 `HoCharacterShadowRendererFeature`；主方向光开启 Shadows，URP 主光阴影可用。
2. 在预制件根部添加 `Rendering/Ho-CharacterShadow`，指定已有 `HoObjectBufferGroup`。
3. **接收范围**默认“整组”，包括后来新增的 OB 部件；切为“指定部件”后直接勾选 OB 已有部件，可全选或清空。指定模式为空时不接收 CS。Renderer 在 OB 的部件中绑定，“配置 OB 部件…”可跳转到该配置。
4. 用“从接收对象计算包围盒”按当前姿态计算一次，再调整 Anchor / Center / Size，给动作预留空间。该工具不会逐帧随蒙皮收缩。
5. 在 `Ho-CharacterShadow Volume` 覆盖逐相机参数；未覆盖时沿用 Feature 的兜底值。组件显示实际状态、Tile、投影深度与世界单位 / texel。

一个 OB 组使用一个 CS 组件；不需要为 CS 单独开启 OB 屏幕纹理绘制。接收身份来自 OB 登记，空间覆盖来自角色盒。

## 参数与单位

以下是 `HoCharacterShadowSettings` 的默认值；实际值以本相机解析后的 Volume 覆盖与运行状态为准。

| 项目 | 默认值 | 含义 |
| --- | --- | --- |
| 单角色分辨率 | 2048 | 每个接收域的方形深度 tile，可选 512 / 1024 / 2048 / 4096 |
| 同时接收域上限 | 16 | 与实际图集容量取小；不足时回退，不偷偷降低分辨率 |
| 图集边长上限 | 8192 | 可调至 16384，仍受设备上限约束 |
| 阴影软边 | 0.005 m | 额外固定滤波半径；设为 0 仍保留基础抗锯齿 |
| 深度 / 法线偏移 | 各 1 texel | caster 偏移；过大可能丢失贴近的细投影 |
| PCSS | 开，Ultra | 质量档只控制采样数，不改变世界半径 |
| 光源角直径 | 2° | `pcssSoftness` 的现行语义，范围 0–8°；0 只保留固定软边及基础 AA |
| Blocker 搜索半径 | 0.02 m | 寻找遮挡物的范围，不能当作最终软边宽度 |
| 半影半径上限 | 0.04 m | 距离相关半影的上限，不随分辨率或采样档缩窄 |
| Blocker 深度偏移 | 0.0005 | 阴影空间归一化深度偏移，与 texel 单位的 caster bias 不同 |

方向光的 PCSS 估计为：

```text
半影半径 = min(半影上限, 平均 blocker 深度间隔 × slice 深度范围 × tan(角直径 / 2))
```

现行 C# / HLSL 上限为 blocker 32、filter 64，由 `HoCharacterShadowShaderContract` 与验证器核对。不要把另一个 `Ho-ShadowCast` 模块的 softness 或 texel 参数套到 CS。

质量档的 blocker / filter 预算为 Low 8/16、Medium 16/32、High 24/48、Ultra 32/64。

## 采样与资源契约

- 光空间 X/Y 拟合角色接收盒；上游 caster 主要扩展 Z。增加 caster 数量不会自动分摊 tile 的 X/Y 分辨率，扩大接收盒会降低单位面积的 texel 密度。
- Feature 持有持久 atlas，通过 RenderGraph 导入。已检查的角色样本实际分配为 4096² D32、单 mip、未启用动态缩放。graph 结束后全局纹理返回 4×4 默认黑图，不能作为实际 atlas 降采样的证据。
- 各深度 texel 先比较，再插值可见度；Gather 与后备读取遵循同一顺序。tile 边界单独夹取。
- 小核对 texel 单元积分 tent，以相邻单元权重合并为四点读取；半径 ≤2 texel 时最多 16 个四点读取，2–2.5 texel 向方差匹配的固定对称盘过渡。
- PCSS blocker 搜索使用连续权重的 gap / coverage，首个部分遮挡渐入半影。世界半径不再按有限采样预算截断；宽核低采样仍有稀疏误差。
- 接收面梯度从世界坐标导数变换得到。局部平面外推受深度残差和 raw / corrected 可见度保护，减轻掠射面的大三角漏光；它仍是有局限的局部近似。
- `_HoCSDepthParameters[slice].x` 为 `far - near`；全局数组按固定上限发布，避免 Unity 会话内数组容量漂移。

| Shader 输入 | 编码 |
| --- | --- |
| `_HoCSPcssParams` | enabled、tan(角直径 / 2)、blocker 搜索半径 m、半影上限 m |
| `_HoCSPcssParams2` | blocker 深度偏移、blocker 次数、filter 次数、固定软边 m |
| `_HoCSParameters[slice].w` | 世界单位 / texel |
| `_HoCSDepthParameters[slice].x` | slice 的 far − near |

## 剔除与普通阴影隔离

CS 固定在 `BeforeRenderingShadows` 工作，用自己的隐藏方向光及光空间剔除相机创建原生 shadow renderer list。隐藏光只服务局部 caster 列表，不贡献有效光照。该设计避免借用场景主光时与 URP 的每灯阴影列表状态冲突。

修改 pass 时机、剔除或 atlas 生命周期，必须保留远距 / 级联扫描和普通场景阴影回归。临时对象销毁也要遵守编辑器 / 渲染回调的时机约束：编辑模式延后到允许销毁的回调，播放模式延迟销毁，不能在渲染回调中直接 DestroyImmediate。

不要通过借用场景主光或更换 split 索引规避 Unity 的每灯列表状态；只改 pass 的先后顺序会在“普通场景阴影被局部列表覆盖”和“CS atlas 为空”之间切换。具体历史排查已由 Git `541d06f2` 保存。

## 调试与验证

调试入口在 Volume：“Atlas”看整图，“Character”按 Tile 放大，“All”使用公共平铺展示；Scene / Game View 各有开关。Feature 仅保留兜底值与状态，不在 Inspector 复制一套日常调试入口。当前没有最终 toon 阴影 AOV。

验证类为 `lilToon.URP.Extensions.Editor.CharacterShadow.HoCharacterShadowValidation`：

| 方法 | 覆盖 |
| --- | --- |
| `Validate` | 固定 XY、上游覆盖、接收盒、Shader 参数上限 |
| `ValidateReceiverSelectionRendering` | 整组、指定、空指定与接收身份 |
| `ValidateRendering` | 场景 caster、材质接收、停用 / 恢复、Volume 与图集调试 |
| `ValidatePcss` | 固定与距离相关软边、核心亮度、细 tile |
| `ValidateFiltering` | texel 比较、Gather / 后备、坡度保护、tile 隔离、小核积分、连续 blocker、软度方向扫描 |
| `ValidateDistanceRendering` | 1 / 4 级联与远近相机扫描 |
| `ValidateSceneShadows` | CS 开关不影响其他物体的普通阴影、SRV 与临时对象生命周期 |

2026-10-28 的独立 D3D11 / D3D12 RenderGraph 运行通过完整 suite 与扩展 filtering；本次提交前再次确认相关源 SHA 与记录一致，Runtime / Editor 分程序集编译为 0 errors。摘要、实际 metrics 和复核范围见 [Filtering 验证快照](../Tests~/CharacterShadow/Filtering-20261028/README.md)。这些证据不等同于完整透明 / XR / 大规模动画角色矩阵或所有近景画质已通过。

## 已知限制与后续入口

- 单张深度图、PCF 和 PCSS 不能恢复生成阶段漏掉的亚 texel 几何；4K 也不等于屏幕空间已充分采样。
- 实际角色对照仍有条纹和局部面状结构。宽稀疏核、normal bias 的掠射面耦合、投影保护区与宽核边界仍需分别诊断。
- Cast 在 toon ramp 前接收。某层 Blur 为 0 时可以重新形成硬边；原始 V 平滑不保证最终 ramp 已抗锯齿。诊断应使用材质克隆，不能擅自改用户的美术层。
- 局部平面保护不是通用几何可见性；极近、近共面遮挡仍有歧义。
- 本模块仅处理主方向光的局部栅格投影。公共 Ho-RT host、多灯 RTShadow 与 SIGMA 属于独立模块，其使用和支持域见 [Ho-RT](Ho-RT.md)。

下一轮设计统一从 [Ho-RT](Ho-RT.md) 开始。历史 Plan 的“下一项”、降低分辨率增大软边、旧半径截断和旧“半影放大”参数，不作为现行建议。
