# Ho-RTDI 既有运动向量协议审计

> 状态：有日期/协议边界的审计证据。现行完成度与使用入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)；历史失败仍按原门槛保留。

第35步只验证HoURP17/lilToon已有motion，没有给NRD启用未知运动源。产品不新增motion pass、纹理、Feature或AC副本：请求、RG借用和GPU读回仅Editor验证回调启用时发生；Player只有隔离CaptureMotion探针请求Motion。生产DI/NRD保留原路径。

## 来源与已验证数值

HoURP17 `UniversalResourceData.motionVectorColor`由MotionVectorRenderPass生成，R16G16_SFloat；`ScriptableRenderPassInput.Motion`触发所需pass/depth。lilToon有MotionVectors pass，输出currentUV-previousUV，与NRD old=new+MV符号相反。URP MotionVectorsPersistentData按Time.frameCount管理相机矩阵，同一帧多个请求不代表多个应用历史帧。

64²正交相机、宽度2的Quad，同一应用帧依次x=0/.125/.25/0，两条路径UV x=0/.062469/.124939/0、y=0，.0002容差及同帧断言通过。第二次移动累计到应用旧矩阵，不是DI刚保存的上一渲染请求(.125)，不能直接按每次SubmitRenderRequest的历史消费。

RG直接借用并声明读依赖。兼容shader全局纹理不能当临时RT标识CopyTexture：最初零值已通过专用全局采样pass纠正，两条数值一致。读回纹理属于诊断fixture，生产不分配。

## 尚未证明正常跨帧对应

跨Editor update即使Time.frameCount增加，x=.25仍对应UV=.124939，未满足期望增量.0625，正式断言失败。Player手动请求也复现累积。改用自动相机后，批处理和当前隐藏普通启动都没有继续增加Motion捕获计数；手动预热仅1次捕获。

早期空纹理ReadPixels产生无效数据，已补有效纹理和每帧捕获计数检查；最终探针在没有本帧捕获时明确exit1、pass=false，不把旧0值或Time.frameCount变化当有效源。这里证明捕获未发生，不推断唯一原因或普遍引擎缺陷。

所以IN_MV仍原黑纹理/worldMotion模式。尚需在实际自动渲染环境验证物体/相机旧帧、覆盖与当前主表面关联，再决定直接借用和深度运动转换。[NRD输入契约](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md#non-noisy-inputs)不允许把2D向量冒充完整3D运动。第34步完整能量失败仍保留。

## 验证、复现与下一项

Roslyn通过，两条同帧数值gate及原diffuse八组回归通过；两个Mono release errors0，默认实际D3D12 RG/compatibility原suite和D3D11 inactive通过。独立`--motion-protocol`明确失败，不能与默认suite通过混同。本步不改生产采样/NRD参数、vendor/native、GI、源古堡或最终接收。

入口 `RunMotionProtocol/Compatibility` 为同应用帧gate；`RunMotionProtocolRealFrames/Compatibility` 检验跨Editor update增量，当前失败保留。Player `--motion-protocol`以有效纹理、本帧捕获计数、UV增量及JSON pass检验源；默认启动另验光照suite。隔离日志前缀 `rtdi-motion35-*`。Editor读回shader来自已Prepare的隔离Player fixture，缺少时明确报错。

下一项保留实际自动渲染源验证，不绕过历史匹配。第34步静态局部diffuse能量失败不依赖motion，继续对其输入/历史裁剪/空间阶段做对照并修正，避免等待运动源而停止DI质量推进。GPU有效帧/成本证据仍不足。
