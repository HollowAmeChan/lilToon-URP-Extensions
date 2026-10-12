# Ho-RTDI 漫反射历史的非负物理域

第56步根据第55步GPU阶段证据修复生产DI私有History Clamping的负漫反射。改动位于历史反馈与moment更新处，不增加发布pass、资源或参数；镜面后续滤波偏差仍独立保留。

## 原因与修复位置

第55步混合绿色天空/红蓝解析灯中，raw/Temporal均非负，Clamping首次产生约-1的RGB值，最终仍残留约-.2。核对固定NRD `RELAX_HistoryClamping.cs.hlsl`：YCoCg轴向colour box转换回RGB、随后history acceleration/reset可以离开非负RGB域。亮度二阶矩又依赖这份实际反馈颜色，发布端截零不足以修复历史与统计。

DI派生body只在RADIANCE模式，对完成clamping/acceleration/reset的normal与responsive diffuse RGB取max(0)，然后继续原second-moment correction。moment跟随修正后的outDiffuse亮度，fast反馈也保持物理域。specular逻辑、SH分支、HDR scale、时钟/预算、alpha格式及native ABI不变；未改变F0/光强/材质颜色。

生成器严格检查唯一的diffuse moment边界并重建，原始36文件SHA与版权/许可保持。`HoRTDINrd06.compute`说明更新，body位于`HoRTDINrdHistoryClamping.hlsl`；`generate_nrd_variance_adapter.py --check`校验全部派生。Runtime GI没有引用这些DI NRD body，本步未改共享ApplicationBridge或GI实现。

## 实际阶段结果

原运行时mixed none/T/S：Clamping及后续所有观测的negativeValues均0；此前Clamping为2973/3866次、发布为1/18次。发布min从-.199707/-.193726变0。none发布diffuse红/绿/蓝1.009376/.996860/.975970；T/S .993739/.999924/.959922。平均能量变化很小且仍在5%内。

修正贡献归属的三样本原型T/S Clamping此前2次负值也消失，发布保持非负。镜面源代码未改，比例保持第54/55步，mixed约1.05的边界失败仍存在，不据漫反射修复宣称整个混合环境验收通过。

## 回归与部署

两条64多灯/core通过，四个最大core仍.010407/.010699/.012265/.012363<.02，raw core红0。256/512动态漫反射六组、64动态镜面三组、两条完整primary运动、两条16组彩灯noise质量均通过；512个别末位浮点微变保留，门槛未改。

最初脚本中的`RunReuseNoiseQuality`入口不存在，未作质量证据；按实际入口`RunReuseNoise/Compatibility`重跑通过。Roslyn Runtime/Editor errors0、原SHA/派生check、diff whitespace通过，无新增DI Shader诊断；原lilToon重复keyword构建警告保留。

两个Win64 Mono release构建errors0。实际D3D12 EXE两条pass=true、退出0，输入/边界/HDR/环境/NRD/混合灯及non-readable Cutout动态门槛通过；raw MSE .050400、filtered .000510，HDR filtered相对误差约1.09%，environment raw/filtered约.999261/.999130，environment spec约.994583/.984608。实际D3D11 unsupported分支pass=true、退出0。CPU DLL SHA仍5EFF9D42D56DA8B38D305BC483D84263AEE3C31A6C6C3A0A637370DA68220EDB，未更新native库。

## 下一项

先处理已定位的镜面后续Atrous能量变化，保留非负/core/彩灯/primary/环境/Player门槛。三样本采样仍为隔离原型，opaque、动态CDF、多相机、实际GPU时间与最终产品接入未完成，不扩大材质合成/PT范围。

第57步[镜面固定交换隔离对照](Ho-RTDI-LateSpecularExchangeAudit.md)将混合环境能量恢复到5%内；还需广泛噪声/边界/部署回归后才接纳。
