**实现与取舍：** 全局phi=.5使原opaque filtered MSE升至.004099，放弃全局改动。仅DI有Cutout最终可见性时收紧diffusePhiLuminance=2→.5；无Cutout还原2，每帧向native更新切换。五次Atrous、历史预算、specular、ReSTIR/native/Shader/guide ABI均不变，无新资源或pass；Editor单项A/B不加产品开关。这是相机级条件，任意Cutout场景噪声仍要实测。

**实际结果：** 两条原8组完整对照raw仍3%内，filtered原门槛7组通过：静态红1.004658/蓝.980818、meanL1 .057627，camera红.993814/蓝.993022、meanL1 .059636，静态TS meanL1 .083784。caster均值接近1、frameL1仍1.059944，因此qualityPass=false和正式失败保留，不宣称动态遮挡物修复。普通opaque noise filtered MSE .000183恢复原表现；Cutout/opaque/Cutout配置.5/2/.5及filtered能量.997938/.999160/.989462，两条通过。

**完成前复查：** 核心两条、NRD两条、Cutout两条、材质边界、HDR镜面、环境VNDF、曲面回归通过；Runtime/Editor Roslyn及NRD派生SHA通过。两个Win64 Mono release构建errors0，实际EXE D3D12两条与D3D11 inactive均pass=true，无新增DI shader诊断，仍有原lilToon重复keyword警告。未改共享GI行为、原古堡资产或最终接收，不重复无关GI测试。过滤提交本步owned文件；GPU预算仍未证实。详见 [Cutout滤波](Ho-RTDI-CutoutFiltering.md)。

**下一项：** 核对世界revision与NRD主表面历史的区别；现有confidence的相同灯shortcut只在static-world成立，且保守射线跳过Cutout。先用可复现诊断评审保留历史的噪声收益/拖影失败，再设计真实动态可见性confidence，不能简单取消全局reset上线。

## 31. 动态遮挡历史收益与残影失败对照

**动手前评审：不能直接放宽world reset。** 固定世界revision混合pose/transform/实例集合，现有confidence相同灯捷径和保守inline跳过Cutout，尚无真实动态alpha变化信号。核对用户RTXDI固定FullSample ComputeGradients的同reservoir再求可见性、官方NRD History Confidence的同采样梯度契约。

**实现：** 仅Editor隔离入口对比原重置/保留NRD世界历史，ReSTIR temporal仍原策略。原慢速caster、正向快速caster、16帧阶跃，独立CPU逐像素alpha参考且断言Far始终无遮挡；新增最差帧/阶跃前三帧变化像素红通道误差，恒亮蓝不稀释。无生产开关、资源、Shader/native/vendor或合成修改。

**证据与取舍：** 慢速frameL1 1.064071→.125951，快速1.024179→.237205，raw各case完全相同且3%内。但阶跃变化像素L1 .157649→.570931，整体frameL1却.204079→.176262，单看均值会误判。新增变化像素35%门槛后保留历史qualityPass=false，不上线忽略世界变化。原保守策略仍生产默认；下一步做真正动态最终可见性confidence后再评审保留历史，不能用平均噪声掩盖残影。

**验证与复查：** Runtime/Editor Roslyn通过，两条硬件对照、正式失败入口和默认核心回归核对后独立过滤提交；无需重复Shader/native/Player未变的构建或GI测试。详见 [动态可见性](Ho-RTDI-DynamicVisibility.md)。完整DI目标继续，GPU预算和任意动态主表面仍未证明。

## 32. 同历史采样的动态最终可见性confidence

**动手前评审：必须补真实变化信号，不能直接取消reset。** 用户固定RTXDI FullSample同reservoir再求可见性及官方NRD同采样梯度契约作为依据。native raygen读取上一帧实际Shade的样本/UV/权重、灯映射/主表面，在当前TLAS求opaque/Cutout anyhit，比较已存亮度；不混新RNG。检查3×3历史格内至多9个样本，含旧亮度0，保留各lobe最大相对变化，复用原gradient/confidence/history资源与原4字节payload，无新Feature/纹理/buffer/native ABI。

**调度取舍：** ReSTIR仍拒绝完整world变化；仅Cutout最终可见性路径，NRD对pose/transform保留历史并用真实confidence，对新增StructureRevision硬重置实例/材质/掩码/源mesh及显式geometry dirty。追踪skinned原source，防止同拓扑换源误归pose；共享GI仍原Revision策略。世界/灯稳定跳过新射线。4/2次梯度模糊扩散低confidence，快速运动噪声仍高；原生格内信号直接转换/采样后通过运动与残影门槛，静态compute处理保留。

**立体部署反馈：** 平面通过后，新增Player实心Cutout cube移动，phi=.5/sigma2蓝≈.918失败；仅phi1阶跃蓝≈.923仍失败。GPU阶段即时副本按实际scale恢复单位，发现裁剪与反馈压低稀疏高亮样本；固定phi1下sigma2→4，阶跃Temporal/Clamping/final≈.971260/.960514/.967756→1.001048/1.000647/.998615。最终Cutout phi1/sigma4，无Cutout仍2/2，时间预算/其他NRD设置不变，显式切换；sigma共用于两lobe，新增动态镜面仍需后续验证。未改vendor/派生算法/DLL，不补偿亮度。

**质量与余量：** 原双灯8组正式两条qualityPass=true，全部raw3%内、filtered各通道5%/meanL1<.15/frameL1<.35；caster frameL1 .127042。额外慢/快/阶跃生产frameL1 .128248/.213909/.179925、变化像素.209548<.35。静态红1.045045/全透光TS蓝1.046886近门槛，保留余量，不宣称普遍无偏。CPU native gradient oracle216格/169变化，两条通过；匹配FP16解析容差.003及导数/量化传播confidence.03，不改质量gate，稳定跳射线与结构reset通过。

**部署与复查：** 核心、NRD、Cutout、scene skin与必要共享GI两条及HDR回归通过；最终profile切换能量.997681/.999160/.990697，opaque noise filtered MSE仍.000183。Runtime/Editor Roslyn及派生SHA通过；两个Win64 Mono release errors0，实际D3D12 EXE通过原gate及non-readable cube连续/阶跃，RG frameL1 .157265/.173242、变化像素.109085，compat .164167/.172591、变化像素.113743；D3D11 inactive通过。Player部分蓝能量≈.950761近5%边界，不能扩张结论。初次测试入口namespace拼错只重跑正确入口；CPU oracle补FP16/解析误差传播后通过，未改生产梯度换通过。无新增DI shader诊断，原lilToon keyword/C#警告另计。

**成本与下一项：** 有变化时最多额外1可见性射线/有效历史像素，scope已加，无新显存；实际GPU预算未证实。下一步验证动态Cutout镜面、移动primary、灯/身份变化及余量，再优化采样成本，不放大为整古堡质量或最终NPR接收。过滤提交本步owned代码/验证/文档。详见 [动态visibility confidence](../技术/Ho-RTDI-VisibilityConfidence.md)。

