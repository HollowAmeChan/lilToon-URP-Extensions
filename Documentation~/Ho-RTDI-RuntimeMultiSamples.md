# Ho-RTDI：可选三最终样本的Runtime接入

第78步把已验证原型接入真实feature：`finalSampling`默认Single，可选TripleAnalytic。主ReSTIR估计仍占解析贡献的一半，两fresh解析辅助各四分之一；主样本为环境时保留一份完整天空贡献。没有改变最终lilToon接收/合成、GI、SSS或lilPBR。

## 使用与成本

Ho-RTDI设置中的「finalSampling」可选择单样本或三样本。三样本增加两次解析灯最终可见性查询，目标是降低动态阴影/多灯噪声；没有有效GPU毫秒证据，不能承诺任意场景帧耗时。单盏解析delta且无天空保持原确定性路径，zero/sky-only不分配无意义的辅助资源。

Opaque在实际shade/无NRD的raw shade中直接生成、查询、消费两个辅助，第一辅助复用旧scratch slot2，第二辅助局部消费。存在基础Cutout时native raygeneration生成两辅助，final R8编码三个可见性bit，第二辅助每像素8 bytes。1080p native second sidecar约15.82 MiB仅容量估算；opaque没有该全分辨率分配。CDF每项4 bytes、按相机持有。

## 实际snapshot与资源

辅助proposal从真实 `LightFrame.current` 的numeric AnalyticLight snapshot生成，包含local/infinite实际索引顺序、线性RGB/intensity/temperature与spot cos参数。选择数在pass Setup固定，RG/compat snapshot分别在天空输入有效性确定后求effective budget，并上传本帧CDF；不再依赖Editor live Light数组。

Global RGB-power proxy保留15/16概率，1/16概率用于uniform support；shader按上传FP32 CDF的实际相邻区间计算inverse PDF。极端功率不能表示严格正区间时回退均匀，不丢弃灯源支持。只支持当前Point/Spot/Directional解析辅助，area/emissive UV采样不由此payload承担。

每相机CDF/secondary独立，实际scene同步后的Cutout状态决定容量。旧辅助buffer重分配/预算关闭时，按最后使用的GraphicsFence放入retired列表，passed后释放；每次multi调度结尾记录AllGPUOperations完成点。[Unity GraphicsFence API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.CreateGraphicsFence.html)说明其跟随前面的GPU命令完成。默认single不新增活动辅助分配，shader单分支的必要dummy绑定复用现有buffer而不访问它。

Budget进入历史signature，增删/类型变动保留既有identity-map策略；geometry/材质/环境变动沿现有失效逻辑处理。多样本confidence用cached primary的旧物理贡献比较同一个primary，避免将averaged colour与单一旧样本误比；默认single的原路径保留。

## 接入中发现并修复的实际回归

首先沿原型纯power CDF接入，Editor固定灯域通过，但实际Player的「近处4强度灯＋远处25000强度灯」mixed测试失败：raw spec约1.036，filtered spec .635、diffuse .901。不是把该Player失败归成旧天空问题。全局功率提案过度偏向远处强灯，近处BRDF贡献变成rare大权重样本；补入1/16 uniform support后，同一Player门槛恢复。

最终两路径三样本Player：mixed filtered spec=.973051/diff=.978648；temporal spec=.963734/diff=.978532，全部在原5%内。原单样本仍通过。首次shader因结构体ternary不被Unity DXC接受而编译失败，改为明确if/else后完整重跑；早期失败不计通过证据。

## 最终验证

- Runtime、没有Editor shader替换的Cutout/opaque × RG/compat，共16解析灯配置全部通过。Cutout快速spot L1=.324357、step=.202892（changed=.203049）、32灯=.311013、HDR=.308588；新uniform support使32灯稍高于纯power原型.307974，仍在.35内。GPU actual index/CDF inverse PDF/cache检查通过；opaque local second未借调试侧记录伪装为已读回。
- 两路径多相机各128点×1024样本逐点均值通过：raw最大.9860%、filtered .9462%；各context的CDF/native secondary资源独立，预算、projection尺寸/skip/disposal与实际后续GPU消费检查通过。
- 两路径连续72帧切换single/multi、Opaque/Cutout和尺寸，中途无CPU GPU读回：36single、18native multi、18opaque multi均符合实际分配；retired peak在最终运行各1，读回后下一prepare全部回收。单灯/零灯shortcut与极端power支持检查通过。
- 默认single两路径16配置reuse noise、完整primary motion通过；实际两份Win64 Mono release single Player pass/exit0，raw/filtered MSE及既有环境数据保持。
- 两份multi release构建errors0，实际D3D12 RG/compat Player pass/exit0，覆盖raw/NRD、mixed、recolor/off、HDR/环境、基本Cutout、非readable/skinned、边界和primary origin。raw MSE=.0191031、filtered=.000207006；默认single约.050400/.000510。两路径mixed指标一致。
- 实际D3D11不支持分支pass/exit0，零指标只表示未激活。
- 最终Roslyn errors0；原vendor SHA/NRD派生check、GUID唯一、whitespace通过，native DLL SHA不变。无新增DI shader诊断；外部SolarHD division-by-zero及lilToon重复keyword构建警告保留。

Legacy prototype生成器保留Single预算下的独立对照；opaque/native原型回归通过。它不是生产依赖。Player build helper增加multi独立目录，没有覆盖用户项目或修改古堡。

## 仍保留的质量与范围限制

中心mixed天空镜面最终仍2/4，约5%偏差与第77步对照一致，原正式失败保持。动态灯域仍有native8/9、opaque6/9的短窗口/colour-tail失败，报告独立保存；不能把实际Player通过等同于任意场景无偏或无漏光。两种预算的primary distance guide仍取主样本，实际应用运动输入协议、IL2CPP/XR、GPU时间及目标场景预算均未在本步证明。

第84步[diffuse凸裁剪](Ho-RTDI-DiffuseConvexClamping.md)已消除Clamping生成的关闭通道残色，opaque动态由6/9提升8/9；100:1短窗口能量和mixed天空镜面失败继续保留，四份实际Player均通过。

报告 `rtdi-runtime78-lightdomain-*`、`rtdi-runtime78-env-*`、`rtdi-runtime78-dynamic-*`、`rtdi-runtime78-state-*`、`rtdi-runtime78-multicamera-*`、`rtdi-runtime78-player-single-*`、`rtdi-runtime78-mixedcdf-player-*`，保存隔离工程。初期pure-power Player失败另保留，不以成功结果覆盖该发现。

下一项转向实际feature使用、目标场景的几何/天空壳支持与有效GPU预算，而不是继续停在Shader生成原型；保留独立质量fixture、上述有效失败和最终NPR/PBR合成边界。
