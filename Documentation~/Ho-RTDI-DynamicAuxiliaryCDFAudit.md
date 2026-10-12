# Ho-RTDI：动态灯集与辅助 CDF

第69步仅 Editor 隔离审计；生产未增加多最终样本。核对用户 UnityPathTracing 的 DI TemporalResampling 对 `RAB_TranslateLightIndex` 的调用、本地 LightFrame 的双向 identity map 与 GPU Shade 路径。提案必须对应本帧灯顺序和逆 PDF，不能沿用 profile 开始时的旧 CDF。

## 执行与契约

128²、三盏 RGB 灯，点灯/聚光/方向灯，顺序经历 balanced、100:1 功率与色温、关闭蓝灯、蓝灯改方向光、红灯改聚光、关闭红灯、全部关闭、恢复。每阶段96帧；16帧后20,480点观测检查能量与暗通道，raw全程要求暗通道为零。门槛保持 raw3%、filtered5%、无输入通道最大绝对值 .001。

测试在修改灯参数的同一同步边界上传 CDF，按fixture灯的 local/infinite范围和 InstanceID 得到期望顺序；随后严格核对实际 collector 结果。此处是接入契约验证，尚未把测试枚举复制为生产 collector。固定最大3项 CDF buffer、无灯时填dummy且不抽样。每帧 GPU 读回两辅助 inverse PDF，以及两方向映射；开启/关闭后检查 -1，类型跨采样范围后检查同一Light身份。

逐点按实际GPU primary/两辅助 packed weight与当前CPU灯参考重构 .5/.25/.25 raw。归一化误差 `<.003`（分母=max(predicted,reference)+.001），实际最大约 .000974，匹配FP16输出精度；没有把错误PDF导致的偏差归为统计噪声。

opaque原型修正了零灯时仍读取旧辅助scratch槽的缺口：仅在当前analytic count>0时读取/平均辅助。native路径自身每帧写empty样本。两路径全部关闭时raw与filtered均为0，恢复后索引/PDF/主辅助贡献正确。

## 正式失败保持

RenderGraph/compatibility 四个短窗口入口均实际执行且结果逐项一致：Cutout6/8，opaque5/8，全部质量入口exit1。契约和raw分解检查通过，不等于质量通过。

| 失败项 | raw比例 / filtered比例或暗通道峰值 |
| --- | --- |
| 100:1功率短窗口（两可见性域） | 红 raw1.149439；filtered红1.092061/1.089855，绿.938443/.938265 |
| 关闭蓝灯 opaque | raw蓝0；filtered蓝峰值 .00114059 |
| 关闭红灯 Cutout / opaque | raw红0；filtered红峰值 .00156689 / .00269127 |

100:1红主样本积分1.263596、两辅助1.036106，二者平均与raw1.149439吻合，问题不能仅归给辅助CDF。将该阶段扩为2048帧，raw红收敛1.010760，primary1.018977、auxiliary1.003278。Cutout filtered绿.953100过5%，opaque绿.949082仍失败；没有用长窗口替换短窗口失败。长窗口改变后续随机序列，随后关闭红灯依然有Cutout峰值 .00162792（opaque报告另保留）。无法据一次长窗口证明任意功率动态都满足误差门槛。

正常阶段两路径没有NRD reset，Cutout每次灯参数/列表改变仅首帧trace confidence，opaque没有native confidence trace。raw关闭通道为0但filtered尾部有值，说明下一项应审查灯集变化后的去噪历史，而不是改CDF index、钳制输出颜色或放宽门槛。

报告 `rtdi-dynamiccdf69[-long]-cutout/opaque-rg/compat.json` 保存隔离工程。short每非空阶段768辅助PDF检查；mapping分别576/576/385/575/576/385/2/573，跨启停/类型变化通过。长阶段16,384 PDF、12,288 mapping检查通过。无新增DI shader错误/警告；Roslyn errors0，GUID唯一/whitespace通过。native/Runtime/GI/Player未变，未构建未变Player。

## 下一项取舍

先验证灯集增删/类型改变触发去噪历史失效的保守策略，分离颜色残留与短窗口采样收敛；保持普通灯移动/功率变化和caster动态confidence路径。若策略有收益，必须同时检查生产单样本、原动态遮挡、两渲染路径与实际Player，再决定是否进入生产。多最终样本仍有环境镜面和显存/性能限制；古堡仅作合并天空/模型支持审计。
