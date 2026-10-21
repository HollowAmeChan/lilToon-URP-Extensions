# Ho-RTGI 持续推进入口

本目录为当前任务的持久化入口。目标：沿本机 UnityPathTracing 参考，交付场景可用的一跳漫反射 GI；每步 review、过滤提交、记录收益与失败，并支持压缩后的连续推进。

接手顺序：

1. [实施规划与强制交付条件](../计划/Ho-RTGI-v0.1-实施规划.md)：功能目标、用户要求、范围、阶段与验收。
2. [当前交接](Ho-RTGI-交接.md)：当前真实状态、进程、未提交工作和下一动作。
3. [踩坑与禁止重复尝试](Ho-RTGI-踩坑与禁止重复尝试.md)：已知失败与重开条件。
4. [过程记录](Ho-RTGI-过程记录.md)：逐步依据、实现、验证、review 和提交。
5. [既有原型说明](../Ho-RTGI.md)：历史实现与测试，涉及当前支持域/资源数值时以当前源码重新核对。

状态：目标active。M0、次级天空、正常相机加法合成和独立RELAX_DIFFUSE已有实测；步骤005降噪矩阵/回归通过，步骤006两个Linear D3D12 Mono Player各304次请求证明真实应用时间、非均匀反弹与对应响应/合成。正常enabled-camera自动呈现、复杂薄边/小高亮源、目标场景GPU预算及完整Player矩阵仍在后续范围。代码/文档存在不等于全部功能已验证；DI第108步关闭GI的记录不能充当GI验收。

步骤007已提供独立材质身份过滤；步骤008基础Cutout已通过10项Editor与两个309请求Mono Player，覆盖初始/灯光/天空/重采样/最终alpha、NRD距离和真实应用帧响应。骨骼GI vertex fetch、完整实际场景、有效GPU预算仍未完成，不能由registry或这些局部夹具替代。

步骤009补齐骨骼GI实际UV/法线/BlendShape、遮挡/反弹证据，六项Editor及两个315请求Mono Player通过。完整实际场景、复杂角色动态接收、正常自动camera与有效GPU预算仍继续，不将这些小夹具扩称整体目标完成。

步骤010已实际导入/渲染古堡并输出GI开关图，但发现原版filtered RGB能量门槛失败；两轮色盒诊断搁置、Runtime不改，失败保留。目标active，接下来继续有效GPU取证与该实际质量缺口，禁止用功能四组通过重定义完成。

步骤011已有严格fence/频率/已知负载验证的GPU timestamp，古堡256/512两路径与重建/插桩对照实测通过；这补GPU成本证据，不解除010质量失败，也不等于完整Player帧/显存/半分辨率重建预算。计时桥仅隔离Editor，Release无probe依赖且实跑通过。

步骤012补实际256/512的28项资源清单：自有payload16.588/65.151MiB；AS/pose统计分列。1080p约514MiB仅布局推算，HD首次Unity fence失败后终止，稳定预算不接纳。见[资源证据](../../Tests~/RTGI/Resources/README.md)、F31和[半GI设计](Ho-RTGI-半分辨率设计.md)。下一阶段先验证GI输入/历史/NRD中心映射，半GI尚未实现，010质量失败继续。

步骤013坐标helper已有实际GB/SB GPU对独立CPU几何证据：RG/compat各12case/18190receiver/5961前次depth，补偿后的网格/历史/NRD投影通过，负对照确认会检出漏映射/漏补偿。helper尚未接生产GI或重建，不算半GI交付。两次raster轮廓夹具失败和修正限制保留F32，下一步接GI私有桥和全域输出。

步骤014已实际完成半GI链路/全域重建实验：平面和57.497%payload收益通过，但两路径薄前景能量full约.992、half全0，原5%失败；Runtime/Editor原型已过滤撤回、源码/patch/图归档，F33搁置。当前回到010实际质量调查，不能重复上一段当时计划或以内存收益接纳画质失败。下项先取GI低信号moment/尺度证据，不先认定根因，010/012/正常camera/完整预算缺口继续。

步骤015确认默认64下slow moment精度问题：实际Temporal/Clamp正亮信号m2=0约98%；两个可逆Editor单位对照降至.00315%，物理raw256帧均值完全相同，但RGB仍未全过5%，生产默认/Player仍64。F34排除HistoryFix条件fast alpha，F35停止纯单位调参。下一步全图/16相位与局部阶段误差取证，原失败继续，不挑方便指标。

步骤016 full256²及所有16相位仍失败，原64全图RGB约−7.25/−8.42/−8.78%，否定“只因相位”的解释。DI同box凸裁剪候选恶化RGB/L1，已过滤移出产品并按F36搁置。下一步对高波动热点和安静控制点做同支持域高样本无复用一跳参考/收敛，先确认真值再决定降噪修正；原5%与完整应用验收继续。

步骤017发现并修复大合并蒙皮mesh的AS范围越界：自有pose absolute indices/base0使同场景D3D12错误14→0，顶点字节与三角元组保留；Editor双路径/真实UV命中、两个315请求Release Player过。合法AS重采后15/16选点收敛，暗点8M仍不确定；旧非法AS参考不接纳。全图原5%仍失败，静态非零base尚须安全镜像，正常camera/1080预算继续，见[证据](../../Tests~/RTGI/DenseReference/README.md)。

并行任务共享工作区，提交必须列明路径。避免占用其他任务的验证工程；同一个 Unity 工程一次只能运行一个实例。

步骤018静态owned镜像实际157功能checks完成，但实例3→5增长有scratch debug错误；原Ho及直接Unity API均能复现，初始5干净。多轮无收益方案已撤回、F39搁置，产品保留017实现，static非零base仍未交付。仅接纳最小复现及[失败/恢复证据](../../Tests~/RTGI/StaticMesh/README.md)，不声称具体上游根因。下一项转合法AS古堡独立seed/T/S/none质量对照，所有原质量/应用/预算缺口保留，goal active。

步骤019完成RG256²四模式×8seed矩阵，实际exit0/API0/源漂移0。TS raw相对none全图RGB高25.70/22.21/18.75%，T或S单独仅约2.4～3.0%；所有32段filtered/raw仍原5%失败。当前只有定位收益，Runtime不改。两次byte-gate拒绝、精度合同与最终真实重跑保留F40和[完整证据](../../Tests~/RTGI/Seeds/README.md)。下一项020仅Editor尝试pre-spatial history，同帧T/S保持，再评质量/方差/成本，不关闭复用当完整通过；正常camera/1080/Player等目标仍active。
