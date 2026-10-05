# GeometryData Tension 组件与 lilToon Tesion设计

日期：2026-10-05。状态：组件、共享 Renderer 寻址、Compute 调度和 lilToon Tesion 首版已落地；正式验证状态见文末，原型结果与生产验证分别记录。

## 1. 名字与职责

| 对象 | 名字 | 职责 |
| --- | --- | --- |
| 动态张力组件 | `HoGeometryDataTension` | 参考状态、当前几何测量、专用 TensionData、资源寿命 |
| 参考系组件 | `HoGeometryDataReferenceFrame` | 世界参考系 |
| 描边组件 | `HoGeometryDataOutlineCorrection` | 专用描边方向/厚度数据 |
| URP 接入 | `HoGeometryDataRendererFeature` | 时机、GPU 任务和资源发布 |
| lilToon 可选组分 | **Tesion** | 使用张力控制材质自身的 BaseColor/NormalMap 混合 |

所有 GD 的 MonoBehaviour / RendererFeature 名字包含 `GeometryData`，菜单继续使用 `Rendering/Ho-GeometryData ...`。非组件的来源、buffer、builder、绑定 helper 可保留职责名字。现有组件已按此规则重命名，文件与 meta 一起改名，没有添加旧配置迁移代码。

代码中的数据类型使用标准拼写 `Tension`；材质 UI 按用户指定显示 `Tesion`。

GD 不持有 BaseColor/NormalMap，不裁决材质效果优先级。两对纹理与其混合规则属于同一个 Tesion 材质组分；这不是多个 GD 生产者依次覆盖通用属性。

## 2. 组件外形

`HoGeometryDataTension` 挂到对象，首轮目标为已验证来源的 SkinnedMeshRenderer。可以选择关联 Renderer，不要求有 OB；需要拓扑和参考数据时显式准备。

### Inspector

| 配置 | 默认值 / 行为 |
| --- | --- |
| 目标渲染器 | 同对象的 SkinnedMeshRenderer，或显式指定 |
| 参考状态 | Mesh 原始姿势；后续支持显式捕获当前 GPU 姿势 |
| 初始化时准备 | 开；没有有效缓存时提出一次准备请求 |
| Play 前准备 | 可选；编辑器生成/验证静态依赖，不靠 importer 触发 |
| 生成 / 更新参考状态 | 手动按钮；也提供运行时 API |
| 动态更新 | 按来源样本更新，与 Camera 独立 |
| 边长 / 面积测量权重 | 描述几何测量组合，不是材质纹理混合强度 |
| 状态 | Pending / Valid / Invalid / Stale、原因、参考版本、顶点/面数 |

组件只生产数据，不自动修改材质、UV、原始 Mesh 顶点属性或 OB 配置。纹理响应关闭与组件关闭是两个独立控制：其他消费者仍可需要同一份 TensionData。

### 基准与更新

1. Prepare 构建必要拓扑、CSR 邻接、参考位置和面角量。
2. 在 native skinning 后取得 GPU 位置，转换到与基准一致的 Renderer-local 分析空间。
3. 计算边长、面积与角点两边夹角的变化；跨面二面角不混同为本轮角点量。
4. 每顶点 gather 输出，按实例发布。
5. 同一来源样本被多相机消费时不再推进/重复生产；相机只影响后续消费者自己的观察相关计算。

运行时不逐帧 BakeMesh。当前来源接口的 D3D11、D3D12 / rootBone / float32 位置布局限制仍如实保留，后续逐项扩展。

## 3. TensionData

首轮输出为每 render vertex 的 `float4`：

| 通道 | 含义 |
| --- | --- |
| X | Stretch，非负拉伸信号，中性值 0 |
| Y | Compression，非负挤压信号，中性值 0 |
| Z | AngularChange，角变化信号，独立保留 |
| W | Validity，0 表示不可消费，不是“张力为零” |

边长/面积用 log ratio，角用差值归一化。先分离正负变化再聚合，避免一个顶点周围的拉伸和挤压互相抵消。退化、无邻接、非有限值显式处理。

AngularChange 在初版材质设计中不自动指定给拉伸或挤压纹理；两对纹理的含义分别由 X/Y 驱动。如果需要角变化参与某一路，后续作为该消费者的明确响应参数，不改通道定义。

同一个 Mesh 的不同 pose / 参考版本不能共享动态结果。共享身份寻址属于 transport，数据和效果仍属于专用模块。

## 4. 材质 UI：额外属性 → Tesion

新增一个默认关闭、默认收起的可选组分，不放进基础主贴图或默认光照流程。先接普通完整 lilToon 的 URP 材质；Lite / Fur / Gem 等特殊材质在有对应实现和验证后再开放。

```text
额外属性
  Tesion                      [启用 □]  默认关闭
    总体强度                      1

    拉伸
      BaseColor                   [纹理] [颜色倍率]
      NormalMap                   [纹理] [法线强度]
      响应范围                    起点 / 全响应
      混合强度                    1

    挤压
      BaseColor                   [纹理] [颜色倍率]
      NormalMap                   [纹理] [法线强度]
      响应范围                    起点 / 全响应
      混合强度                    1
```

**两对**是正式设计：拉伸 BaseColor + NormalMap，挤压 BaseColor + NormalMap。两路均可单独不提供某类纹理，允许只改变颜色或只改变法线。

每路使用同一套 UV0 与该路的 tiling/offset；第一版不增加独立 UV 改写。颜色倍率默认白，normal 默认中性。未提供的颜色/normal 目标不参与该类混合，不因默认白图把原材质漂白。

总体强度默认 1，但“启用”默认 0。初始响应范围可用 0.02 → 0.2，属于可调整的材质响应参数，不宣称是物理常量。

## 5. 与原贴图的混合

### 5.1 数据到权重

设有效的拉伸、挤压分别为 S/C。每路自己的响应权重：

```text
uS = smoothstep(stretchStart, stretchFull, max(S, 0))
uC = smoothstep(compressionStart, compressionFull, max(C, 0))
wS = saturate(moduleStrength * stretchStrength * uS)
wC = saturate(moduleStrength * compressionStrength * uC)
```

数据 invalid、模块关闭或总体强度为零时，权重为零。某类纹理未提供时，该类权重为零。range 相等/顺序错误需要明确定义硬边响应或钳位，不能发生除零。

### 5.2 两路同时存在：不依赖绘制次序

顶点邻域及插值可能同时存在拉伸和挤压，不能连续两次 lerp，让后面一组永远更优先。

对当前纹理类别的两路权重使用：

```text
d = max(1, wS + wC)
WS = wS / d
WC = wC / d
WB = 1 - WS - WC
```

只有拉伸时退化为原值与拉伸目标的普通 lerp；只有挤压时同理。两路均满响应时各占一半，基准权重为零，没有“挤压覆盖拉伸”的规则。

### 5.3 BaseColor

```text
resultRGB = WB * originalRGB
          + WS * stretchRGB
          + WC * compressionRGB
```

originalRGB 是主贴图与已有主颜色处理结果；Tesion 在主颜色阶段生效，再让既有额外颜色层和光照按原顺序继续。BaseColor 的混合不默认改变 alpha、clip、透明 coverage 或 ID。

初版是**向目标 BaseColor 混合**，不是隐式添加或乘一层颜色。纹理采样与颜色在现有 shader 的颜色空间规则下处理。

### 5.4 NormalMap

三份 normal 都在切线空间解码：原有普通 normalmap 组合结果 nB、拉伸目标 nS、挤压目标 nC。每路法线强度在解码时作用，再采用独立权重混合：

```text
n = normalize(WB * nB + WS * nS + WC * nC)
```

接近零或非有限的混合结果回退 nB；不先在世界空间把不同含义的法线叠加。无原 normalmap 时 nB 为 (0,0,1)。缺少某一路 NormalMap 不影响对应 BaseColor 的独立响应。

用户要的是 normalmap 与原 normalmap 混合，因此初版采用目标方向插值，不把额外图默认解释成 RNM 细节叠加。以后若需要细节模式，在这个材质模块中明确提供。

## 6. Shader 与运行时接入边界

- 材质使用本地关键字启用专用变体；默认关闭时不增加张力采样、四个纹理采样或额外 varying，不抬高原材质编译目标。
- vertex stage 按该 Renderer 的专用输出取 S/C，再插值到 fragment；不逐像素遍历拓扑。
- normal 路径在原普通 normalmap 合成后、转换到 world-space/TBN 光照前应用。
- 材质与采集 pass 共享相同的 BaseColor/normal 求值；GB/SB 是否输出该材质结果是这些材质消费者的行为，不由 GD 另设覆盖优先级。
- 首版保留明确的无组件/无数据回退：完全维持已有颜色和法线。
- 不写回 Unity 原始 normal，也不要求其他后处理直接读取逐顶点 TensionData。

### 描边与 Tension 共存所需的 transport 改造

原 RSUV 高 16 位为描边专用槽；首版实现已将其改成共享 GD Renderer 槽。描边与 Tension 不再分别写入高位。

实现使用**稳定的 GeometryData draw 关联槽**，描边与 Tension 在各自的有类型表里按该槽查询。低位仍为 OB 身份。公共 transport 只解决对象寻址/寿命，不做属性规则或效果裁决。

GPU Compute 写入与实际材质读取要建立声明完整的资源链。首版同步 graphics queue，不在依赖尚未闭合时打开 async compute。

## 7. 实现顺序与验收

1. 完成 GD 组件命名规范，并保持文件 meta 的 GUID；编译检查。
2. 改造 draw 关联，使同一 Renderer 的描边与 Tension 共存、单独关闭不清掉另一模块。
3. 实现 HoGeometryDataTension 的准备/参考/动态更新和专用结果；GPU 当前源已验证。
4. 添加额外属性的 Tesion、四纹理、独立响应与默认关闭关键字。
5. 接 BaseColor 与 normal 路径，补正常材质、数据缺失、禁用与特殊 pass 验证。

验收至少包含：

- 默认关闭材质与原版颜色/法线逐像素一致，旧材质没有开启新关键字。
- 基准/刚体运动无响应；拉伸只驱动拉伸贴图，挤压只驱动挤压贴图。
- 只有 BaseColor 或只有 NormalMap 时另一类保持原值。
- 两路同时存在的权重与无顺序偏置公式一致。
- 无组件/无数据/组件禁用时回到原材质，alpha 与 coverage 不受影响。
- 同 Mesh 多实例、描边共存、多相机不会串数据或重复推进。
- 明确 supported source / topology / graphics API，真实角色和 normalmap 导入验证。

验证原型、fixture、截图和日志继续放在本地忽略的 research 目录，不进入生产包。

## 8. 首版实现范围

`Runtime/GeometryData/Tension/HoGeometryDataTension.cs` 提供显式 Mesh rest pose 准备；缓存可序列化，GPU 资源可重建。`Resources/HoGeometryDataTension.compute` 依次 Extract / TriangleMetrics / VertexGather；`HoGeometryTensionPass` 声明 RenderGraph buffer 的读写，再发布专用表。参考版本、帧号和测量参数共同决定结果是否可复用。

描边与张力在 `HoGeometryRendererBinding` 中分别持有同一 Renderer 槽的关联；关闭一个生产者不清除另一个。每种专用数据表独立处理自己的有效性，重复的同类型来源被判为无效，不引入覆盖优先级。

材质使用 `_HO_GD_TENSION` 本地关键字，前向 / GB / 角色颜色采集使用同一颜色/法线求值。普通 DepthNormals pass 保留既有几何法线策略；本轮没有改动它的通用 normalmap 行为。特殊材质不开放 UI。

首版暂未包含：当前 GPU 姿势基准捕获、跨面二面角、D3D11 / D3D12 以外的 API / skin 布局、blendshape 或骨骼在同帧多相机之间再次变化的自动来源版本检测。需要同帧重算时显式更新参考或改变测量参数；普通动画帧以 Time.frameCount 复用。

正式验证入口为本地 `research~/GeometryData/U/Assets/Editor/ProductionTensionValidation.cs`，结果 `Results/production-tension-results.json`。真实角色的美术响应范围仍需要在目标场景调节。

正式测试 90 项通过；GPU 数据最大误差约 3.65×10⁻⁶，关闭后的像素误差 0。包括完整 URP 材质、真实 GB normalmap、双路混合、多实例 / 多相机、缺图 / 缺组件、alpha clip、描边共存和资源重建。不是仅用数学原型替代正式消费验证。

## 9. Volume 调试与导入模型准备

调试模式、Scene / Game、Layer Mask、热图范围与时机移至 HoGeometryDataVolume，Feature 只保留运行兜底与数据源只读状态。每相机解析当前 Volume stack，不把调试状态存到数据生产组件中。

编辑器的 HoGeometryDataTensionPreparation 使用 MeshUtility.AcquireReadOnlyMeshData 读取关闭 Read/Write 的导入 Mesh，临时只拷贝参考位置 / 索引后完成 Prepare；sourceMesh 仍指向原资产，临时输入销毁。不更改 importer、不在导入阶段烘焙。Runtime 源支持已验证的 D3D11 / D3D12。

## 10. 输出 WS 加权平均

用户指定默认平均 1 次 / Lerp 1，上限 3 次。实现为当前变形位置的拓扑一环 WS 逆距离加权，分别平均 stretch / compression / angular；有效性保持原值。中心以有效平均邻距计权，最终一次性 lerp 原始结果与完成 N 次后的结果。

在原 Extract / Triangle / Gather 后执行 SmoothVertices N 次与 BlendSmoothing，GPU buffer 交替读写；读写范围按 Renderer 的专用 atlas offset 隔离。禁用参数跳过此步，多个相机复用处理完成的发布结果；材质和 Volume 都消费同一专用输出，不新增覆盖层或屏幕后处理 Feature。
