# Ho-RTDI Cutout 镜面质量验证

第33步只扩展验证，不修改生产采样、NRD设置、shader、native接口或最终材质合成。第32步的动态可见性confidence和共用history clamping profile需要分别验证两lobe，不能用漫反射通过代表镜面通过。

核对用户固定RTXDI `ComputeGradients.hlsl` 的相同历史reservoir重求，以及本地Raygen对GGX/FP16亮度的处理。镜面参考使用独立解析GGX分布、height-correlated Smith项和Schlick Fresnel，按实际主表面输入量化F0=10/255、roughness=102/255；点灯range衰减和CPU平面/alpha可见性独立求值。镜面与漫反射共用同一实际ReSTIR/RELAX调度，不改参数或换确定性输出。

使用原64²、896ROI、Near红/Far蓝两非零点灯，192预热，静态512帧、运动256帧。新增 `signal=specular/diffuse` 报告字段明确评分信号；读取实际raw/filtered specular，避免误用diffuse纹理。原门槛保持raw各通道3%、filtered各通道5%、mean L1<.15、frame L1<.35，阶跃前三帧变化像素红通道L1<.35。

## 原八组镜面结果

两条D3D12路径正式入口均exit0、qualityPass=true，结果一致。

| 模式 | filtered红 | filtered蓝 | mean L1 | frame L1 |
| --- | ---: | ---: | ---: | ---: |
| 静态 | .984996 | .991624 | .028006 | .050447 |
| 相机TS | 1.001179 | .995240 | .028293 | .095501 |
| casterTS | .981498 | .990512 | .042027 | .108094 |
| 静态T | .986625 | .990658 | .031219 | .058745 |
| 静态S | .988329 | .992252 | .027469 | .050237 |
| 静态TS | .992520 | .987386 | .026440 | .052306 |
| 全透光 | .987221 | .990255 | .034362 | .052317 |
| 全透光TS | .983435 | .996192 | .031316 | .052380 |

## 额外运动与余量

生产调度下慢速、快速与16帧阶跃，正式两条均通过。

| 模式 | filtered红 | filtered蓝 | frame L1 | 阶跃变化像素L1 |
| --- | ---: | ---: | ---: | ---: |
| 慢速 | .980905 | .989384 | .110073 | — |
| 快速 | 1.020173 | .973393 | .343487 | — |
| 阶跃 | 1.006984 | .991845 | .347752 | .309657 |

快速/阶跃frame L1非常接近.35，阶跃最差单帧L1 .591305、前三帧整幅L1 .504166；正式gate评分的是观察期逐帧均值和变化像素，并不限制每一帧峰值。保留这些数据，不宣称动态镜面大余量或完全平滑。此处是固定F0/roughness的静态平面接收者和局部caster，不证明窄镜面、动态主表面、动画法线、环境或任意材质同样通过。

旧漫反射原八组回归仍qualityPass=true，Runtime/Editor Roslyn通过。本步只有Editor测试和说明，不重复未变的Player构建、GI或vendor验证；镜面Player部署并未新增证明。

## 下一项与复现

下一步单独审计移动接收面。现有NRD `IN_MV`绑定黑纹理、worldMotion=1，只描述零物体运动；用户参考用motion重建对应表面，[NRD输入契约](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md#non-noisy-inputs)也要求真实表面运动。已确认HoURP17的 `UniversalResourceData.motionVectorColor` 和lilToon MotionVectors pass存在，应先检查其可用时机/覆盖和当前主表面关联，再决定直接读取与转换，避免增加AC副本或另开Feature。不能以静态接收面caster通过代表移动primary正确。

入口 `RunCutoutSpecularQuality/RunCutoutSpecularQualityCompatibility`、`RunCutoutSpecularMotionQuality/RunCutoutSpecularMotionQualityCompatibility`；Audit版本仅收数据，不把exit0当gate通过。报告为隔离工程 `rtdi-cutout33-spec-all-rg/compat.json/csv`、`rtdi-cutout33-spec-motion-rg/compat.json/csv`，信号字段明确为specular。
