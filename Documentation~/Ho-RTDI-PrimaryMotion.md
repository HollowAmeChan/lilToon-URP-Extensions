# Ho-RTDI 移动主表面：梯度起点修正与未通过基线

第34步区分移动caster与移动接收面。灯光保持两非零红/蓝点灯，Cutout caster移到远处、保留其最终可见性路径；只让实际主表面Quad沿深度运动。两lobe均由解析Lambert/GGX和当前平面射线交点评分，不沿用z=0参考。

## 问题与修正

核对用户固定FullSample `ComputeGradients.hlsl`：上一帧选中样本的surface先加worldMotion，再对当前TLAS求可见性，避免自遮挡。当前raygen虽找到近邻主表面，却仍从旧worldPos发射射线；接收面向灯移动时，旧起点可能位于当前几何后方，命中自身并产生假的照明变化。

修正为从**通过位置/法线重投影匹配的当前表面位置**重求旧样本，保留旧reservoir、UV、权重与BRDF信息。未改shader/vendor采样或NRD参数，不添加运动纹理/资源。这个近邻对应不是真实物体motion；它修正当前已匹配接收面的射线起点，不宣称任意动态对应已正确。

## 控制与结果

64²、central16²=256ROI，192预热/256观察，F0=10/255、roughness=102/255；静态、z=.025*sin(frame*.11)、每16帧z=±.012阶跃，diffuse/specular分别检验。NRD不重置，平滑/阶跃分别254/16帧使用原生梯度。原raw各通道3%、filtered各通道5%和frame L1<.35的完整门槛保持。

| 模式 | lobe | 修正前frame L1 | 修正后frame L1 | 修正后filtered红/蓝 |
| --- | --- | ---: | ---: | --- |
| 静态 | diffuse | .094117 | .094117 | .946252 / 1.086598 |
| 平滑Z | diffuse | .457433 | .093396 | .940400 / 1.108769 |
| 阶跃Z | diffuse | .134944 | .105428 | .938281 / 1.119596 |
| 静态 | specular | .051243 | .051244 | .971207 / 1.030375 |
| 平滑Z | specular | .503624 | .049972 | .977225 / 1.012116 |
| 阶跃Z | specular | .305553 | .051900 | .979219 / 1.017678 |

两条硬件路径一致，全部raw原3%内。起点修正显著减少假可见性变化噪声，但diffuse能量仍失败，而且静态控制已经有偏差，不能归咎于运动输入或宣称此步修完整NRD。

报告同时保存 `originPass=true` 与 `qualityPass=false`。起点回归另外要求非静态case raw3%及frame L1<.15；完整能量/噪声gate没有缩小。`RunPrimaryMotionQuality` 实际exit1，起点专用入口两条exit0，诊断入口只收数据。原CPU native gradient oracle、Cutout diffuse/specular八组回归通过，Roslyn和NRD派生SHA通过；无共享GI变更。

两个Win64 Mono release errors0，实际D3D12 RG/compatibility EXE通过原gate及新增移动主表面镜面起点回归：raw红/蓝.998981/1.012912、spec frame L1 .047741。该Player入口也明确只证明起点回归，不替代完整主表面gate。D3D11 inactive通过，无新增DI shader诊断。

## 下一项与复现

当前NRD `IN_MV`仍黑纹理、worldMotion=1，表示零物体运动。[NRD输入契约](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md#non-noisy-inputs)要求对应表面运动，尤其不能用2D向量冒充沿视线的位移。已找到HoURP17 `UniversalResourceData.motionVectorColor`、`ScriptableRenderPassInput.Motion`及lilToon MotionVectors pass；lilToon输出currentUV-previousUV，符号需与NRD old=new+MV匹配。下一步先验证真实帧时机、覆盖和主表面对应，再直接借用/转换，避免AC复制或新Feature。静态局部diffuse能量偏差单独继续定位，不能靠运动向量解决或放宽gate。

入口 `RunPrimaryMotionAudit/Compatibility`，`RunPrimaryMotionOriginQuality/Compatibility`，`RunPrimaryMotionQuality`。完整JSON为隔离工程 `rtdi-primary34-rg/compat.json`；各入口均保留完整case和false，起点通过不覆盖完整失败。本步未改源古堡、最终NPR合成、NRD/vendor算法或GI。
