# HoRP 反射管线：现状、目标契约与路线图

> 状态：**反射权威设计文档**（2026-09-14 初稿；2026 文档审核核对：**P0 输入迁移已完成**——SB 已落地、`MetadataBuffer` 反射桥已随 R6/R7 删除，反射消费的是 SB 具名通道 + AC 遮罩 + GB 几何）
>
> 结构基线：[`Ho-管线总览.md`](架构优化/Ho-管线总览.md) 的 GB / ObjectBuffer / SurfaceBuffer / AttributeComposite 四层归属。
>
> 实现约束：实验性桌面管线；不维护旧资产和移动端降级；质量优先。

## 1. 已冻结的原则

- `PLR` = Planar Reflection；`SSR` = Screen-Space Reflection。
- 反射 source 只提供 radiance；材质响应统一使用 roughness、metallic、reflectance/F0、Fresnel 和接收强度。
- `_UseReflection` 是总开关。关闭时 PLR、SSR、Probe/Sky 全部停止消费；子开关不能绕过它。
- lilToon `_Smoothness`、`_Metallic` 及贴图按常见 PBR 语义解释，最终 NPR 外观仍由 lilToon 决定。
- 不再维护卡通反射模式，不再把 camera color 与 source 做无条件 `lerp`。
- Surface color 的 RGB 为线性 HDR 表面色，producer 不钳制；需要 `[0,1]` 的消费者自行钳制。
- 屏幕空间反射必须使用 GB 的真实几何 coverage；描边视觉壳不能进入 ray、Hi-Z 或 history。
- 不建立通用反射 `Custom0`。专用数据必须使用具名的 SurfaceBuffer 通道或反射 feature 自有 RT。

## 2. 反射来源与优先级

| 表面 | 主要来源 | fallback |
| --- | --- | --- |
| 镜子、明确的光滑平面 | PLR | Probe/Sky |
| 光滑地面、普通 glossy opaque | SSR；明确平面可优先 PLR | Probe/Sky |
| 水面 | PLR；后续可补 SSR 细节 | Probe/Sky |
| 玻璃/OIT | 第一阶段 PLR + 透明专用消费 | Probe/Sky |

任何 source miss 都不能直接变黑。Probe/Sky 继续使用 lilToon/URP 已有的 cubemap、roughness mip、probe blend 和 box projection 路径；探针摆放与用户工作流另文处理。

## 3. 输入归属（迁移已完成）

v2 的 SurfaceBuffer / 三轴迁移**已经落地**，反射不再有任何 MetadataBuffer 桥：

| 输入 | 语义 | 归属 |
| --- | --- | --- |
| GeometryBuffer `NormalDepth` | 几何法线、线性深度、物理 coverage | GB，保留 |
| SB `Color` | RGB 线性 HDR 表面色（**A 不是 coverage**） | SB |
| SB `Normal` | 应用法线贴图后的着色法线 | SB |
| SB `Material` / `Reflection` | perceptualRoughness / metallic / thickness；reflectance / PLR strength | SB 具名通道 |
| 身份 / 覆盖率 / 具名遮罩 | 谁占了这像素多少、是不是指定部件 | **OB（身份池 + 覆盖率）经 AC 查询** |

“通用反射接收”和“PLR 专用接收”必须分开命名，不能让 SSR 复用含混的 PLR strength——这条纪律从桥接期沿用至今；`_UseReflection` 仍是不可绕过的总开关。

## 4. v2 目标输入契约

### GB：几何轴

- geometric normal
- linear/device depth 与真实几何 coverage
- 可选 sky 输入
- outline visual surface 保持隔离

GB 不拥有 roughness、metallic、reflectance、着色法线或材质 mask。之前设想的 GeometryBuffer `Custom0` 不再建立；若透明 PLR 确实需要逐像素 source id，由 PLR feature 生产具名 receiver RT。

### SurfaceBuffer：表面轴

反射至少需要以下具名字段，具体 RT packing 在实现前一次性冻结：

| 字段 | 语义 |
| --- | --- |
| `Color.rgb` | 线性 HDR 表面色，不钳制 |
| `Normal` | 应用法线贴图后的着色法线，供 SSR/反射响应使用 |
| `perceptualRoughness` | `1 - finalSmoothness`，已应用贴图与 GSAA |
| `metallic` | 已应用 metallic map |
| `reflectance` | dielectric F0 标量或等价参数 |
| `reflectionEnable/strength` | 服从 `_UseReflection` 的通用反射接收 |
| `plrEnable/strength` | 额外服从 `_UsePlanarReflection` 的 PLR 专用接收 |

规范化计算：

```text
roughness = perceptualRoughness²
F0        = lerp(reflectance, saturate(baseColor), metallic)
```

### ObjectBuffer / AC

- ObjectBuffer 只负责身份、对象 coverage 与逐物体辅助量。
- **AC 提供反射消费者需要的最终具名遮罩**（typed 查询门面 + runtime catalog；当初设想的独立 `Ho-Cryptomatte` feature 没有实现，遮罩统一归 AC）。
- SurfaceBuffer 不再用 alpha 重复声明 coverage。

## 5. PLR 当前完成度

已完成：

- 镜像相机、oblique clip、反转 culling 和 color-only HDR source。
- 每 Renderer PropertyBlock 绑定独立 source；opaque 多平面可用。
- ForwardLit PBR 响应：smoothness/metallic 贴图、GSAA、F0、Fresnel、能量项。
- 13-tap tent HDR 预过滤金字塔，按 perceptual roughness 采样；不支持 mip 时安全退回 sharp source。
- `_UseReflection` 总开关门控。
- 旧 fullscreen composite 默认关闭；多 surface 时自动禁用，避免全局 source 串面。

仍需处理：

- 水面/玻璃的着色法线扰动和透明/OIT 消费边界。
- 是否真的需要多平面 fullscreen resolve；能在材质/OIT pass 直接消费时不创建 receiver RT。
- 评估 GGX importance-sampled 预过滤是否明显优于当前 tent pyramid。

PLR 的使用和排查只在 [`PlanarReflection.md`](PlanarReflection.md) 维护，本文件不重复 Inspector 参数。

## 6. SSR 目标方案

SSR 第一版只支持 opaque，质量基线为全分辨率或可验证的高质量半分辨率，不做移动端简化版本。

```text
GB depth/coverage
+ SB shading normal/material
+ opaque color pyramid
    -> linear ray march
    -> thickness/front-face/edge test
    -> binary refinement
    -> reflection radiance + confidence
    -> temporal accumulation + disocclusion rejection
    -> spatial denoise/upscale
    -> Probe/Sky fallback
```

输入与输出：

| 项目 | 契约 |
| --- | --- |
| depth | GB；先明确 device/linear depth 与 Hi-Z reduction 方向 |
| normal | SB shading normal；GB geometric normal 只做几何有效性/保守测试 |
| material | SB roughness、metallic、reflectance、reflection strength |
| radiance source | opaque color pyramid，HDR，不提前钳制 |
| output | `ReflectionColor.rgb + Confidence.a` |

GTAO/SSGI 现有 depth pyramid 不直接复用。只有在编码、coverage、reduction 和 mip 生命周期完全一致后才抽共享 producer，避免为了“共用”制造隐式契约。

## 7. 后续实施顺序

### P0（✅ 已完成）：输入迁移

1. ✅ 冻结 SurfaceBuffer 的 `Color / Normal / Material / Reflection / Classification` packing（6 MRT = 五张数值图 + owner）。
2. ✅ 实现 SurfaceBuffer producer 与 DebugTile。
3. ✅ PLR 特殊 composite 已从 MetadataBuffer 迁到 SB + AC（`MetadataBuffer` 整块删除）；opaque ForwardLit 不读回 SB。
4. 反射开关回归矩阵（总开关、PLR 开关、source 有效性、Probe 有无）——**按实机验收继续做**。

验收：全仓库无 `_HoMetadataBuffer` 引用、无 MB 反射桥（已达成）；关闭 `_UseReflection` 后所有反射输出为零。

### P1：SSR 基础闭环

1. 建立 HDR opaque color pyramid。
2. 实现全分辨率 linear march、thickness test、front-face rejection 和 binary refinement。
3. 输出 color/confidence，miss 走 Probe/Sky。
4. 增加 hit UV、ray length、confidence、roughness、fallback source 调试视图。

验收：镜子/光滑地面在屏幕内有稳定命中；屏幕外和 miss 不变黑；描边不参与命中。

### P2：SSR 质量

1. Hi-Z traversal。
2. motion-vector temporal reprojection、disocclusion 与 history clamp。
3. roughness-aware spatial filter、firefly suppression 和必要的 upscale。
4. 多相机、动态分辨率、Scene View 验收。

验收：移动相机无明显拖影/漏光；薄物体和屏幕边缘退化可预测。

### P3：透明与水面

1. 冻结 OIT/玻璃读取 PLR、SSR、opaque color 的时序。
2. 水面加入着色法线扰动、厚度/吸收和 Fresnel transmission。
3. 只有确需 after-transparent fullscreen resolve 时才实现 PLR receiver/source-id RT。

验收：水面、玻璃、镜子、光滑地面各有独立测试材质；不会双重累计反射。

### P4：输出与工作流

1. 冻结 reflection AOV：source、resolved contribution、confidence。
2. 建立 Reflection Probe 命名、体积、Box Projection 与更新模式约定。
3. ~~删除 MetadataBuffer 反射桥和旧 composite/debug 名称~~ **已完成**（R6/R7 整块删除）；余下的是旧 debug 名称清理与 AOV 冻结。

## 8. 不再维护的记录

- Custom0 的 smoothness/wetness/normalStrength/reflectionStrength 打包。
- “所有反射统一由 ScreenProcess/fullscreen composite 施加”的旧方案。
- lilToon 卡通反射与低质量/mobile 专用分支。
- 为未验证的共享 Hi-Z、receiver RT 或完整 deferred GBuffer 预留槽位。
