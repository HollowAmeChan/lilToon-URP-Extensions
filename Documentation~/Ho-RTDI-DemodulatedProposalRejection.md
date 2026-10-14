# Ho-RTDI：去调制信号域目标对照拒绝

第98步基于第97步无辐射采样噪声输入控制，检查真实选样目标与NRD两lobe的量级是否匹配。核对用户RAB target和固定SDK归一化、NRD material factors，不继续试滤波指数或历史系数。

两种隔离目标仅作用于非零F0；零F0保留第93步目标。把真实diffuse/specular BRDF响应分别除以原NRD材质因子后：（1）相加取亮度；（2）把六个RGB分量取联合L2长度。初始/时间/空间/偏差修正统一调用同一目标，source PDF、物理Shade、NRD输出、预算/可见性保持。仅生成临时compute/bridge，native最终求交不消费目标，GI未改。

这是信号域选样实验，不称上游推荐默认或有限RIS最优；material factors仅影响提案，实际去调制/还原沿原路径。增加算术与NRD header依赖的成本没有GPU计时证明。

## 实测与拒绝

| mixed复用 | 原filtered spec红/蓝 | 去调制亮度目标 | 六路RGB目标 |
| --- | --- | --- | --- |
| none | 1.050745 / 1.048828 | 1.030241 / 1.056897 | 1.034669 / 1.061376 |
| TS | 1.044358 / 1.050032 | 1.025967 / 1.057352 | 1.024874 / 1.054326 |

亮度目标两环境路径一致、2/4/exit1；RGB环境RG同样2/4/exit1。红改善但蓝仍5%外，未据单通道下降接纳。

亮度目标64动态镜面3/3通过，slow/rapid/step frameL1 .110533/.341897/.342144；完整primary第一组static diffuse raw蓝.964130超3%，实际exit1、在JSON写入前中止，仅日志证据。未把未生成的报告当成通过。

RGB目标primary全部通过，但64动态镜面step失败、整体exit1：raw3%与平均5%通过、frameL1 .342007在.35内，前三帧变化点transitionChangedL1 .482866超原响应门槛。其余slow/rapid frameL1 .101211/.308381。噪声/平均能量改善不能替代短期响应。

共七个实际进程，保留各自失败出口。Roslyn errors0、GUID唯一、日志无新增DI shader warning/error或SourceAssetDB时间错误，diff通过。报告 `rtdi-runtime78-env-baseline-demodproposal98[-rgb]-*`、`rtdi-demodproposal98[-rgb]-motion-*`、`rtdi-demodproposal98-rgb-primary-rg.json` 在隔离工程；亮度primary失败只有日志。

## 下一项

不生产化，不给产品增加NRD提案依赖或新设置。下一步在原提案下检查两辅助最终样本的随机分层，分别覆盖CDF两半并随机交换槽位，使每个槽位仍具有原边际PDF；不使用固定反相关索引，因为任意灯排序下可能增加方差。保持final权重、可见性、ray数与compact合同，实测灯域、动态/环境再决定接纳。该步骤仍只做DI；距离lobe合同和最终材质接收另行保留。
