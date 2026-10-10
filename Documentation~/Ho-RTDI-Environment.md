# Ho-RTDI 天空环境光

Ho-RTDI 的「天空环境光」直接借用前置 Ho-Atmosphere 的原始 HDR radiance、旋转、增益和 tint；没有新增天空 Feature、材质合成或天空捕获副本。环境候选数与局部灯候选预算独立。没有可用天空输入或关闭此选项时，不分配环境 PDF，解析灯路径继续运行。

## 采样与概率

DI 在128×128的等立体角参数域建立luminance PDF mip：`phi=2πu`、`y=1-2v`、`L=(sqrt(1-y²)cos(phi), y, sqrt(1-y²)sin(phi))`。每个texel立体角为`4π/N²`，无需lat-long的sin(theta)权重。PDF使用已应用HoAtmosphere旋转/tint/gain的radiance；极低权重保留非零floor，维持提案支持域。稳定天空按revision缓存PDF，SDK RIS每帧使用新的随机序列预采样。

沿用固定RTXDI的`RTXDI_PresampleEnvironmentMap`和`RTXDI_SampleEnvironmentMap`，不改vendor算法。RIS存储离散texel选择概率的倒数与16-bit UV；`RAB_LightSample.solidAnglePdf=N²/(4π)`表示texel内部条件密度。target与final shading都除这个条件密度，reservoir inverse PDF还原完整源概率；因此既能与local/infinite候选MIS，也不会把PDF分辨率当成照明增益。初始阶段只使用天空候选，当前BRDF扩展见下段。[官方采样API](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/ShaderAPI.md)，本地固定`InitialSampling.hlsl`/`PresamplingFunctions.hlsl`是对应版本的签名与单位依据。

**后续BRDF环境MIS：** 现在额外默认4个BRDF候选（可设0对照），使用既有Donut余弦半球/GGX VNDF sampler。两种lobe都有响应时采用0.5/0.5提案，纯diffuse/纯specular只采对应lobe；这是采样分配，不改变最终BRDF。bridge自身按[Heitz VNDF推导](https://jcgt.org/published/0007/04/01/paper.pdf)评估反射方向pdf `D(H)*G1(V)/(4*NdotV)`，与余弦pdf使用同一混合概率。固定vendor的命名PDF helper未改；没有直接用其`D/(4*VdotH)`表达式代替VNDF方向概率。0/30/60度独立积分覆盖了该匹配，未把PDF更改扩展到共享GI分支。

沿用SDK `RTXDI_SampleBrdf`与环境PDF的balance MIS，无brdfCutoff截断。BRDF方向查询HoRTScene，opaque命中不贡献环境，miss才转为环境候选；射线范围与环境visibility一样上限100000。解析delta灯不可被这些射线求交，mesh emitters仍未接入。每个被接受的BRDF提案会增加一次几何查询，额外候选与后续visibility成本不能据没有新增纹理就视为免费。

环境灯使用保留的31-bit index `0x7ffffffe`，不占Unity解析灯数组或改变local/infinite分区；UV可跨表面重建同一个世界方向。visibility从真实表面沿该方向查询现有HoRTScene，远端范围100000 world units，NRD距离沿用当前65504上限。天空revision（内容/旋转/tint/gain）变化时重置DI/NRD历史；不需要保存第二份前帧Cubemap。环境参数/纹理为缺失路径显式清零/绑定black cube，避免pooled RG data或兼容发布残留。

## 资源和成本

有效环境输入增加一张128² R32F全mip PDF（约0.0833 MiB）与256×64的uint2环境RIS段（0.125 MiB），合计约0.2083 MiB/context，与输出分辨率无关。local Power和environment共享同一RIS buffer中的不同段。首次/天空revision变化时增加PDF build及mip生成；每帧增加RIS预采样与环境候选/对应visibility工作。不增加全分辨率光照纹理；没有据此宣称免费或生产FPS。

## 验证与边界

`HoRTDIValidation.RunEnvironment` / `RunEnvironmentCompatibility` 使用真实lilToon receiver与前置HoAtmosphere。常量`(1,.5,.25)`的Lambert参考为同色，实测raw平均约`(.996,.498,.249)`；无/T/S/TS状态均通过3%条件。解析灯+天空MIS、隐藏于camera的封闭世界阻挡、black/disable/absent生命周期通过。

HDR Cubemap各面使用独立颜色，CPU hemispherical quadrature作真值。yaw0、gain2、tint(.8,.5,1)的raw/reference约(3.757,1.114,1.242)/(3.735,1.111,1.223)；yaw90约(11.012,.224,2.769)/(11.009,.223,2.785)，各通道3%条件通过。宽GGX（rough≈.698、F0=10/255）的独立半球积分约.027514，raw≈.027515、NRD≈.027149，原5%能量条件通过。两条路径一致。第一轮prototype的PDF Texture2D元素类型与固定SDK签名不匹配，已修正为对应纹理类型，失败结果不计入通过。

这些初版受控case只覆盖环境radiance重要性候选，未证明窄镜面/小太阳；后续BRDF/VNDF环境MIS的具体边界见下一节。normal-map、对象运动、TAA、mesh emitters或最终NPR接收仍未交付。HoAtmosphere现有Unity天空盒捕获方式不变，DI验收覆盖Constant与原始HDR Cubemap，天空盒材质的解码/捕获属于既有生产方。

### 窄镜面和小亮源的后续验收

`RunEnvironmentSpecular` / `RunEnvironmentSpecularCompatibility`用double自适应Smith GGX积分，避免均匀低密度数值积分漏掉rough≈.098的峰。192预热/512观察，无ReSTIR reuse。旧4个天空候选的窄镜面raw/filtered比例≈.904668/.436120，MSE≈802.055/7.634846，5%gate失败；同总预算8个天空候选也只有.929567/.490027、MSE≈441.509/4.215629。新4天空+4BRDF比例≈.997485/.985933、MSE≈5.234457/.029439，三个roughness的5%gate均通过。预算对照入口为`RunEnvironmentPdfBudget`，保留失败，不把增加候选数本身称为采样改善。

30/60度×rough≈.098/.298/.698用独立H-space积分与reflection Jacobian验证；raw比例约.9982..1.0115，filtered约.9849.. .9923，5%条件通过。`RunEnvironmentSun`使用128² Cubemap的- Z面中央2×2像素、radiance1000与background.02，独立解析方形角域覆盖加自适应积分参考≈17.720746；raw/filtered≈.994679/.983982，5%条件通过。这里证明特定静态、normal-incidence小亮源case，不是任意天空太阳尺寸/方向、复杂normal-map或动态几何的通用质量结论。

两个Win64 Mono release构建0 errors，实际D3D12 RG/compatibility EXE通过新增rough=25/255窄镜面天空gate，独立double积分下raw/filtered比例≈.996075/.985061，满足原5%。旧环境diffuse比例≈.999838/.998784、封闭遮挡0、禁用清理和原曲面/混合/visibility/HDR/noise等检查通过，D3D11实际EXE inactive通过。HDR最差约1.095%、旧filtered noise MSE≈.0000958，不把新增测试推进RNG后的数据当作单项消融。packed NRD DLL SHA相同，Roslyn无错误，原vendor/派生SHA检查通过，无新增本步DI/NRD shader/command-buffer诊断，既有其他模块警告另计。旧两条核心、NRD、材质/HDR边界、曲面运动及共享GI回归通过；解析-only运行没有开启BRDF环境提案。

两个Win64 Mono release构建0 errors，实际D3D12 RG/compatibility EXE通过新增天空部署gate：raw/参考=1.005200、NRD/参考=1.001977，隐藏于camera的封闭遮挡=0；禁用/缺失清理、旧曲面/混合/初始visibility/HDR/noise等检查继续通过，D3D11实际EXE inactive通过。packed NRD DLL SHA相同，Roslyn无错误，原vendor/派生NRD SHA检查通过，无新增本步DI/NRD shader或command-buffer诊断，既有其他模块和lilToon duplicate-keyword警告另计。该64²Player fixture只验证常量天空，不替代Editor HDR Cubemap或复杂天空的质量验收。

**与当前Ho-RTGI的路径重叠：** 旧GI的primary-ray miss也贡献直接可见天空。若将DI/GI最终相加，应在两者现有环境开关中选择一份直接天空贡献，直到后续按bounce归属完善拆分。此步不改变旧GI辐射含义、不自动关另一Feature，也未补secondary-surface的间接环境NEE。GI独立性fixture显式关闭DI环境项，以继续验证纯解析DI不被GI开启影响。
