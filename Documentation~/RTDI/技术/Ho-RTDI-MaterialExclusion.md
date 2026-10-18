# Ho-RTDI：按材质排除合并网格中的子网格

> 状态：技术专题与接入证据。步骤中的测试数值及“下一项”保留当时语境；当前默认值、支持范围与剩余问题以 [Ho-RTDI 总文档](../../Ho-RTDI.md) 为入口。

第80步为DI加入明确的材质排除列表，用于古堡这类天空壳与主体合并的模型。Ho-RTDI「高级 → 不参与求交的材质」设置 `excludedRayMaterials`；默认空，不改变旧场景登记。

列表按Material对象身份匹配。使用该材质的所有子网格不参与DI世界求交，所以灯光会穿过它们；它不是按名称猜测天空，也不改变raster材质、源mesh或Renderer的sharedMaterials。若多个物体共用同一材质，会一起被排除。该设置只由DI传给其场景登记；GI默认不使用此列表。

## 依据与实现

用户参考 `RtxptGeometryCache`按Unity submesh indexStart建立IB布局；当前HoRTScene同样按Renderer/submesh登记独立实例。因而可在material/topology识别前按真实材质身份跳过指定submesh，保留其余实例，不必引入新的Feature或拆源资产。

过滤使用Material引用相等，忽略null/重复项。被排除的旧实例按已有unseen移除逻辑退场，更新StructureRevision，使DI/NRD按既有规则失效历史；保留实例的identity不变，恢复的实例不复用已移除identity。CutoutCount只统计剩余实例，因此最后一个Cutout被排除时正确释放native final visibility。

SkinnedMesh全部材质被排除时不创建pose snapshot；部分排除仍保留整个合并mesh的快照。这不是网格压缩或GPU skinning优化，不能据排掉一个天空submesh宣称省去整份CPU姿态数据。

## 验证

两路径隔离场景使用一个含两个submesh的真实MeshRenderer：右侧sky材质挡右灯、左侧wall材质挡左灯。实际DI输出验证排sky后右灯恢复解析能量、左墙仍遮挡；清除列表后右侧阴影恢复。检查source mesh/材质数组未改、retained identity稳定、重复/null列表不重建、最后Cutout排除/恢复及完全排除的skin不Bake，全部exit0。

共享HoRTScene的rigid/skinned硬件求交两路径、GI两路径回归均exit0。没有向GI扩展排除功能，仅证明默认可选参数未改变其现有登记。

隔离古堡使用真实过滤接口重跑 `AuditBloodMoonCastleWithoutSkyShell`，不再克隆材质并伪装Transparent：15个accepted submesh、全部12个Cutout、1个sky排除，静止64次无额外Bake或revision改变。唯一排除为`1_mesh submesh 0`。源场景/材质未保存或修改；这仍是登记支持审计，不是古堡完整画面或性能验收。当前pose snapshot为29,924,936 bytes，约28.54 MiB。

三份Win64 Mono release（Single RG、Single compatibility、Multi RG）构建errors0、实际D3D12 EXE pass/exit0。新增Player测试在不可CPU-readable的skinned caster上验证排除透光、恢复遮挡和源材质不变，同时原完整suite通过。最初新增测试遗漏恢复denoise，导致后续primary origin用raw作filtered检查失败；现已完整恢复所改状态并重建重跑，single/multi origin L1约.04329/.03818。没有放宽门槛或把首次失败算通过。

Roslyn errors0，GUID唯一、diff whitespace通过，无新增DI shader诊断；既有外部SolarHD及lilToon keyword警告保留。报告`rtdi-exclusion80-rg/compat.json`、古堡without-sky audit及`rtdi-exclusion80-fixed-player-*`位于隔离工程。原环境/动态质量限制继续保持。

## 下一项

现在可在实际DI配置中按天空材质排除合并天空壳；不自动改变用户场景设置。后续应检查目标场景中其它被拒绝材质/灯项，以及真实GPU预算。GI天空/最终NPR接收仍按既定边界推进，不借此扩展通用PT框架。
