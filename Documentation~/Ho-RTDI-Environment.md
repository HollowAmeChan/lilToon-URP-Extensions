# Ho-RTDI 天空环境光

Ho-RTDI 的「天空环境光」直接借用前置 Ho-Atmosphere 的原始 HDR radiance、旋转、增益和 tint；没有新增天空 Feature、材质合成或天空捕获副本。环境候选数与局部灯候选预算独立。没有可用天空输入或关闭此选项时，不分配环境 PDF，解析灯路径继续运行。

## 采样与概率

DI 在128×128的等立体角参数域建立luminance PDF mip：`phi=2πu`、`y=1-2v`、`L=(sqrt(1-y²)cos(phi), y, sqrt(1-y²)sin(phi))`。每个texel立体角为`4π/N²`，无需lat-long的sin(theta)权重。PDF使用已应用HoAtmosphere旋转/tint/gain的radiance；极低权重保留非零floor，维持提案支持域。稳定天空按revision缓存PDF，SDK RIS每帧使用新的随机序列预采样。

沿用固定RTXDI的`RTXDI_PresampleEnvironmentMap`和`RTXDI_SampleEnvironmentMap`，不改vendor算法。RIS存储离散texel选择概率的倒数与16-bit UV；`RAB_LightSample.solidAnglePdf=N²/(4π)`表示texel内部条件密度。target与final shading都除这个条件密度，reservoir inverse PDF还原完整源概率；因此既能与local/infinite候选MIS，也不会把PDF分辨率当成照明增益。BRDF候选当前仍关闭。[官方采样API](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/ShaderAPI.md)，本地固定`InitialSampling.hlsl`/`PresamplingFunctions.hlsl`是对应版本的签名与单位依据。

环境灯使用保留的31-bit index `0x7ffffffe`，不占Unity解析灯数组或改变local/infinite分区；UV可跨表面重建同一个世界方向。visibility从真实表面沿该方向查询现有HoRTScene，远端范围100000 world units，NRD距离沿用当前65504上限。天空revision（内容/旋转/tint/gain）变化时重置DI/NRD历史；不需要保存第二份前帧Cubemap。环境参数/纹理为缺失路径显式清零/绑定black cube，避免pooled RG data或兼容发布残留。

## 资源和成本

有效环境输入增加一张128² R32F全mip PDF（约0.0833 MiB）与256×64的uint2环境RIS段（0.125 MiB），合计约0.2083 MiB/context，与输出分辨率无关。local Power和environment共享同一RIS buffer中的不同段。首次/天空revision变化时增加PDF build及mip生成；每帧增加RIS预采样与环境候选/对应visibility工作。不增加全分辨率光照纹理；没有据此宣称免费或生产FPS。

## 验证与边界

`HoRTDIValidation.RunEnvironment` / `RunEnvironmentCompatibility` 使用真实lilToon receiver与前置HoAtmosphere。常量`(1,.5,.25)`的Lambert参考为同色，实测raw平均约`(.996,.498,.249)`；无/T/S/TS状态均通过3%条件。解析灯+天空MIS、隐藏于camera的封闭世界阻挡、black/disable/absent生命周期通过。

HDR Cubemap各面使用独立颜色，CPU hemispherical quadrature作真值。yaw0、gain2、tint(.8,.5,1)的raw/reference约(3.757,1.114,1.242)/(3.735,1.111,1.223)；yaw90约(11.012,.224,2.769)/(11.009,.223,2.785)，各通道3%条件通过。宽GGX（rough≈.698、F0=10/255）的独立半球积分约.027514，raw≈.027515、NRD≈.027149，原5%能量条件通过。两条路径一致。第一轮prototype的PDF Texture2D元素类型与固定SDK签名不匹配，已修正为对应纹理类型，失败结果不计入通过。

这些受控case没有证明复杂天空中的窄镜面/小太阳、normal-map、对象运动、TAA或最终NPR接收；只有环境radiance重要性候选，没有新增BRDF/VNDF环境MIS或mesh emitters。HoAtmosphere现有Unity天空盒捕获方式不变，DI验收覆盖Constant与原始HDR Cubemap，天空盒材质的解码/捕获属于既有生产方。

两个Win64 Mono release构建0 errors，实际D3D12 RG/compatibility EXE通过新增天空部署gate：raw/参考=1.005200、NRD/参考=1.001977，隐藏于camera的封闭遮挡=0；禁用/缺失清理、旧曲面/混合/初始visibility/HDR/noise等检查继续通过，D3D11实际EXE inactive通过。packed NRD DLL SHA相同，Roslyn无错误，原vendor/派生NRD SHA检查通过，无新增本步DI/NRD shader或command-buffer诊断，既有其他模块和lilToon duplicate-keyword警告另计。该64²Player fixture只验证常量天空，不替代Editor HDR Cubemap或复杂天空的质量验收。

**与当前Ho-RTGI的路径重叠：** 旧GI的primary-ray miss也贡献直接可见天空。若将DI/GI最终相加，应在两者现有环境开关中选择一份直接天空贡献，直到后续按bounce归属完善拆分。此步不改变旧GI辐射含义、不自动关另一Feature，也未补secondary-surface的间接环境NEE。GI独立性fixture显式关闭DI环境项，以继续验证纯解析DI不被GI开启影响。
