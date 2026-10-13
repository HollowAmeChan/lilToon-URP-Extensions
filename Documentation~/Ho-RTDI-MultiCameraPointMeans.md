# Ho-RTDI：多相机逐像素期望验收

第73步修正第71步随机单帧判据的适用范围，不修改生产采样或输入。第72步核对fork/SDK来源表面MIS权重后已证明，瞬时inverse weight不恒定并不等于偏差；只有区域积分也不足以排除局部误差。

## 验收设计

沿相同A点灯64²、B方向灯96×80、各两盏灯fixture，96轮预热后增加1024轮真实交替渲染。两相机各8×8点，每点严格1024个样本、16个完整64帧块。每点按自己的独立CPU解析参考归一化，要求raw长期均值3%内、filtered均值5%内；不能把空间梯度混成时间方差，也不能仅用区域平均替代逐点门槛。

每点还记录时间方差和64帧block-mean标准误差。它只是有限样本的诊断，不假定时间样本严格独立，不据此证明任意场景无偏，也不以大标准误差自动放宽均值门槛。

之后完整执行第71步类型变更、改尺寸、跳过/恢复与销毁流程。各动态阶段的区域raw3%/filtered5%积分也必须通过。隔离、当前灯mapping、actual NRD dispatch、reset和下游实际GPU颜色检查全部继续成立；absent colour/非有限/负值仍逐次严格检查。

`instantaneousPass`和旧`qualityFailures`字段保留原1%/5%单帧观测计数，后者在均值入口中是瞬时诊断，不能解释成均值验收失败数。旧第71/72入口及其报告没有被覆盖成通过；新均值入口的`pass`要求逐点均值、区域积分和全部状态断言。测试读回纹理按尺寸缓存，避免1024轮累积新Texture2D。

## 实际结果

Unity6000.3.15 / D3D12，1024样本/点。Cutout/opaque × RenderGraph/compatibility四入口全部exit0，路径间逐项一致。

| 入口 | 最大raw均值相对误差 | 最大filtered均值相对误差 |
| --- | --- | --- |
| Cutout temporal+spatial，两路径 | .0194280 | .0138351 |
| opaque temporal+spatial，两路径 | .0194280 | .0117230 |
| Cutout no reuse，RenderGraph对照 | .000651128 | .00362879 |

128个点全部均值门槛通过，所有阶段积分通过。每入口2328次render、80项实际GPU内容隔离、2135项局部reset检查、skip/disposal各1。absent colour0。复用raw最大block标准误差约.013094；no reuse约5.53e-15，符合初始同位置同功率灯几乎确定的对照。

瞬时最大raw仍1.399074，Cutout filtered .649377，并有67,125个旧瞬时阈值观测；没有让这些数据消失，也没有通过关闭生产reuse来“修复”。当前证据支持第71步最大值主要是采样波动，不能据此宣称GB/SB坏了或需要改MIS。实际应用帧、透视/XR、任意灯域/材质与全屏边缘未在本步覆盖。

报告 `rtdi-multicamera73-temporal-spatial-cutout/opaque-rg/compat.json`、`rtdi-multicamera73-noreuse-cutout-rg.json`。Roslyn errors0、GUID唯一/diff whitespace通过，无新增DI shader诊断。只有Editor/文档，没有重建未变Player；第70步生产历史失效及其他环境/动态的有效质量失败仍保持。

## 下一项取舍

无需基于这些瞬时极值修改正确的MIS归一化。下一项回到DI原型实际接入成本：第二辅助样本目前24-byte完整SDK reservoir，而它仅承载fresh解析delta样本。评审哪些字段确实被消费，以更小的sidecar保存同样索引、可见性和完整inverse PDF，再做bit/物理输出/动态CDF与原型失败回归；不修改SDK ABI，不把未验收的多样本选项塞进产品。
