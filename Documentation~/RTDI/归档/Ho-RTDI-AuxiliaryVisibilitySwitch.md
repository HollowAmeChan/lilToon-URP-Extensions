# Ho-RTDI：opaque/native辅助路径切换

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第76步仅隔离Editor资源与输出对照。第75步opaque局部消费不能在Cutout突然出现后继续使用；本步先验证在渲染前选择匹配compute/ray变体、绑定相应sidecar及其生命周期。没有修改生产默认DI或最终材质合成。

## 依据与变体持有

核对当前 `scene.Synchronize → CutoutCount → PrepareFinalVisibility`、material signature对texture.updateCount的跟踪，以及fork ShadeSamples的final visibility消费。Opaque要用现有inline结果，Cutout要native any-hit；不能以opaque conservative visibility代替透明裁剪。

此前生成器反复写同一个shader asset，重导入会改变已有Unity引用。新增私有资产名，使opaque和native拥有不同compute/ray资产并预先持有；默认旧资产名不变。日志同步修正为实际sample count、CDF/Uniform、visibility和second storage，删除已过时的“两射线/无额外buffer”固定说明。

测试fixture在每阶段渲染前修改caster状态、选择对应shader与buffer，再逐帧核对生产实际classification/finalVisibility，不复制生产material识别逻辑。两个opaque变体用于独立比较：baseline始终保留完整8-byte sidecar，optimized局部消费并保留32-byte四点GPU快照；native两者均完整8-byte sidecar。

## 实际流程与证据

128²、红/蓝Point、移动skinned blocker，九阶段各48帧：opaque实心→Cutout实心→Cutout孔洞→Cutout全透→opaque→Cutout→禁用caster→重新启用Cutout→opaque。alpha内容改变由真实Texture.Apply/updateCount触发，不强行修改history flags。

两路径candidate/baseline四入口全部exit0。每入口432观察帧、每阶段192个实际GPU辅助index/inverse PDF/cache检查（共1728），native缓存还与实际R8及独立CPU alpha参考吻合。始终有NRD真实调度，light ordering保持。

RenderGraph及compatibility各九对完整raw/filtered SHA256一致，共18对。所有raw/filtered积分、transition诊断、reset与confidence记录数值差0。稳态阶段（16帧后）raw3%、filtered5%门槛全部通过；首3帧L1仅作为诊断记录，.26～.46的数值不能当作已通过其它changed-pixel动态响应门槛。

资源/历史表现：

- opaque/full baseline使用131,072-byte第二辅助；optimized opaque只32-byte快照，native使用131,072 bytes。
- native每阶段首帧reset，后47帧因caster pose变化trace confidence；opaque移动每帧reset，无native confidence。
- caster禁用时native texture释放、仅首帧reset；重新启用Cutout时native纹理和完整sidecar正确准备。
- buffer容量改变在渲染前完成，GPU结果读回结束后才释放旧buffer。测试没有把未完成command引用的buffer立即销毁；也没有证明无读回的生产异步回收已安全。

第75步opaque四配置回归通过且全帧hash保持；第74步native compact四配置回归通过。首次及最终Roslyn errors0、GUID唯一/diff whitespace通过，无新增DI shader诊断。原环境混合镜面与其它有效动态失败保持，不因本步稳态/等价通过而删除。

报告 `rtdi-switch76-optimized/baseline-rg/compat.json`，仅Editor/文档，Runtime/native/GI/Player未变。没有新增用户可添加feature，未重建未变Player。

## 下一项取舍

已证明受控fixture中的前置选择与同步生命周期，尚不是通用产品自动切换：fixture知道期望mode，实际生产需在scene同步后以真实CutoutCount选择、按相机持有并使用合适的GPU生命周期。接下来应复查多样本相对生产单样本的收益/回归与已有filter失败归属，明确产品接入所需的配置、帧snapshot/CDF、变体和Player资源；避免不断追加隔离测试却不推进实际feature。保持GPU budget未测、环境/动态约束及DI范围。
