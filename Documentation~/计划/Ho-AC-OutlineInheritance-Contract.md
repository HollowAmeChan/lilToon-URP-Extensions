# AC 描边继承：当前契约与回归检查

## 当前职责

此处 SurfaceBuffer（用户称 SC）与 ScreenProcess 后处理是两个模块。当前仅为可见描边继承**现有** renderer 身份、角色组与语义；SurfaceBuffer 的身份组覆写尚未实现，不在本次修复内。

描边外扩发生在独立材质 pass。GB 原有描边捕获现在同一 draw 输出：

| 数据 | 格式 / 含义 |
|---|---|
| normal-depth | 既有 RGBA16F，壳层法线与线性眼深 |
| owner packet | RGBA8：RG=原始 renderer 的组/槽位字节，B=材质语义权重，A=可见壳层份额 |

owner 来自 RSUV，与主体一致；不通过邻近 ID 扩张或最近像素猜测。语义权重由主体/描边共享的 `lil_ho_semantic_weight.hlsl` 计算，显式零值保留。描边与 SurfaceBuffer 语义 writer 都显式声明 mask keyword 的 shader variant。

用户现场最终确认：owner 清理修正后 AC 整体拖影消失；剩余景深现象来自材质描边 Z 偏移，将壳层推离主体焦平面。原始 owner 的继承不等于将壳层深度改成主体深度。

没有新增镜像主体功能的材质 pass：六类既有 outline 模板扩展 MRT，并补齐 renderer-user-value、mask variant 和 forward 的 stencil 状态。复制主体深度/stencil 到独立 outline 附件后，描边遵循材质 OutlineZWrite/Stencil；壳层之间可以遮挡，但不修改物理 GB 深度。

AC 保留原始 OB identity 与物理 Selection，再生成视觉 identity / Selection：全屏、Objects、Outline 范围默认消费视觉结果；Geometry 范围绑定原始池。CS、SP、SSS 等经 AC 句柄/门面取输入，PLR 的 coverage 引用也改为 AC 产物。SB 数值有效性仍由真实 surface owner 决定，不凭描边 mask 创造法线/材质数值。

## 投影边界

本版使用 resolve 后的 owner packet 做**像素投影**：可见壳层份额 A 替换相应物理份额，语义按原 owner 标签与材质权重继承。它不宣称 GB 相机 MSAA 与 OB 自建 MSAA 的逐样本精确联合。多个壳层共享像素时 packet 保留最近选中 owner 的份额；五个不同像素级身份竞争四个槽时保留最大四份额，不归一化。原始物理 C/W/V 的精确声明仍只针对原来的对应样本域。

原始 owner、原始 identity/Selection 和最终视觉 identity/Selection 分开存放，是将来 SurfaceBuffer 身份覆写的接入边界。未来需要定义原始 owner→有效身份的映射和额外 pass 的传输契约，不能把当前 RSUV 身份当作未来覆写结果；本次不实现该映射。

## 必须遵守的资源规则

1. **稀疏描边 owner 捕获不得声明 `WriteAll`。** 空白像素没有 fragment 写入，必须保留附件的 clear load。仅在 callback 中写 `ClearRenderTarget(Color)` 不能替代每个 MRT 附件的 load/clear 契约。
2. 每帧 GB 私有深度显式清 color/depth/stencil；outline 拷贝包括 stencil。不能依赖池化附件的新建默认值。
3. 合成 identity 的三附件 draw 和 Selection 的四附件 draw 使用固定 shader 输出布局、独立材质与本 draw 的输入绑定；不在多个相机共用材质上临时切换 MRT 布局 keyword。
4. 读、写和全局绑定分别声明。Compatibility 绘制私有 MRT 后恢复相机目标，避免 URP 因默认目标缓存省略重绑定而把场景画进 ID 图。
5. 停用 writer、相机移动、相机切换与同帧重复 RenderRequest 后均不得读取上一帧 owner。FrameData 保存原始与最终句柄，Geometry 查询不能意外取到视觉池。

## 已抓到的回归

新 owner 捕获最初声明 `AccessFlags.WriteAll`。D3D12 native RenderGraph 可以省略未写附件的 clear，上一帧轮廓的 owner 留在背景；AC 随后把残留传播到总覆盖、角色组和语义。用户现场旁路视觉合成后拖影消失。

永久移动测试在该声明下第二帧检测到 **341 个当前轮廓之外的残留像素**。只改回 `AccessFlags.Write` 后四个移动 / stencil 组合通过；将声明恢复成 `WriteAll` 的隔离负对照再次失败。这个测试针对时间残留，允许当前描边宽度与 OB 自建 AA 的窄边缘，不把两种采样域的边界差当作拖影。

## 永久验证入口

`Tests/Editor/ScreenProcess/ScreenProcessRegressionTests.cs`：

- 出货 DoF 的轻微描边污染阈值 0.05%，三模式、直/斜边、LQ/HQ、非零/零 coverage 与输入失效；失焦描边必须仍可模糊。
- 真正的 GB→AC→SP，camera MSAA 1/2/4，RG/Compatibility；验证 identity、group、TotalCoverage、Semantic、Outline 范围、物理域与写零。
- 真实 lilToon outline，移动相机、两个角色重叠与 forward 可见描边颜色对照；native 语义贴图 R=0.75。
- 10 帧移动的 stencil 角色，检查总覆盖/组/语义是否在当前轮廓之外留下历史；1/2/4 样本的深度-only stencil 拷贝负对照。
- 高度雾 UV 重建及真实正交深度 fallback。

AC 新增 Outline Owner 调试：颜色区分原始 owner，黄色=可见但未登记，洋红=有描边 coverage 却没有 owner writer。原始 C/W/V 调试继续使用物理 identity，不用视觉 rank 对齐原始统计。
