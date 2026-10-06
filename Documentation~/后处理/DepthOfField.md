# ScreenProcess 景深：描边 coverage 与采样契约

景深使用主体的物理深度和描边壳的视觉深度计算 CoC。描边是独立的视觉覆盖层；不能用 OB 身份覆盖替代描边覆盖，也不能把它无条件当成 AO/GI 的物理表面。

## 必须保留的行为

1. 有效描边像素使用壳层自身深度，主体和描边都可以自然合焦或失焦。不能恢复“所有描边保持锐利”的早期绕过方案。
2. MSAA 描边 coverage 大于零就保留壳层深度，包括 1/4、1/2 覆盖；不能用 `> 0.5` 把这些边缘丢掉。coverage RT 的显式零值优先；1× 没有 coverage RT 时用有效 depth.a 回退。
3. 颜色双线性足迹中的每个邻点必须用自己的视觉深度计算 CoC reach。先判断一个点深度，再整份接受双线性颜色，会把合焦描边混进背景。
4. 完全合焦的邻点没有模糊足迹。软 reach margin 不能为 CoC=0 的描边凭空生成贡献；中心颜色始终保留。
5. 缺失描边输入不能读取上一层或上一相机的纹理。每层明确发布 outline-depth / coverage 有效性；没有任何深度提供者时该效果保留原图。

`ScreenProcessDepthInputs.hlsl` 集中实现主体/描边/URP fallback 的深度选择。AC 的描边查询引用同一组 GB 输入；无 layer mask 的 DoF 使用视觉深度，不依赖身份或 Selection 池。不要把“查询用于选择效果范围”和“描边深度用于求 CoC”合并成一个开关。

描边缺少主体身份/语义时，前序效果可能先把描边当成背景处理，留下已经污染的颜色。AC 现在统一继承当前 renderer owner，保留物理域与视觉域的独立产物；契约见 `计划/Ho-AC-OutlineInheritance-Contract.md`。仅修 DoF 的 tap 权重不能替代这一层职责。

## 资源修改需要同步三处

修改采样源或 fallback 时，同时更新：

- RenderGraph `UseTexture` 读依赖；
- 本层实际纹理绑定和有效性 flags（包括 Compatibility）；
- shader 采样与缺失输入策略。

仅 `ConfigureInput` 请求 URP 生产，或仅设置全局纹理，都不足以声明 RG 消费。DoF/Outline 的无 GB fallback 和正交 DepthFog 都必须显式依赖 camera depth；Outline fallback 同时需要 camera normals。

## 防复发检查

仓库内 `Tests/Editor/ScreenProcess/ScreenProcessRegressionTests.cs` 在 Unity EditMode Test Runner 中直接绘制出货 shader，并运行真实 GB→AC→SP feature。见该目录 README 的运行方式。

回归覆盖：Gaussian/Bokeh/目标跟焦参数模式，LQ/HQ，直边/斜边，1× 深度回退和 1/4、1/2、完整描边覆盖，显式零覆盖、输入失效、失焦描边自然模糊、正交深度 fallback、实际 RG/Compatibility 转接，以及高度雾 UV 的解析值。

合焦描边背景污染上限为 `0.0005`（满量程颜色的 **0.05%**），独立颜色通道标记描边。旧数学检查使用 `0.05`（5%）阈值，曾漏过 2–4% 的轻微渗色；其 PASS 不能替代 GPU 回归。修复的验收还需让旧 shader 负对照失败，以证明测试确实覆盖缺陷。

## 成本与边界

HIRO 现场复测排除了 TAA 和 CharacterSpecialization 的额外描边开关；描边深度来源正确，Signed CoC 在壳层出现近/远侧值。用户最终确认材质启用了 lilToon 的描边 Z 偏移，用来修复轮廓；该偏移使描边 mesh 离开主体焦平面，残留的局部模糊来自壳层真实深度。不会通过覆盖 CoC 或强制描边保持锐利来隐藏这个材质设置；永久测试 `OutlineDepthOffsetCanDefocusAnOtherwiseFocusedOwner` 验证这一行为。临时的 SignedCoC / DepthSource 调试 UI 和输出已移除。

逐邻点 reach 需要四组深度判断；权重相同时仍用一次硬件双线性颜色读取，边界权重不同时才分别读取四个颜色邻点。16/48 tap 数和 CoC 参数保持不变。shader 使用循环避免四组判断完全展开时超过 D3D shader 编译器限制。

本修复不是近场/远场分层景深，也不为 TAA 历史颜色重建深度；前景扩散强度仍受单趟 gather 的限制。降采样 GB、TAA、复杂材质的实际轮廓对齐仍需场景验收，不把受控输入的零污染结论泛化为所有历史颜色都无误差。

早期 CoC gather 修复历史见 `归档/DepthOfField.md`。
