# 028 真实 NRD 局部阶段归因

本步确认了主要欠亮发生的阶段，**没有修改生产算法或修复画质**。固定027同18点和预声明seed 0/104729/5347609/6396185；每64warm/256观察，当前默认TS/scale64/production32F策略不变。

资格43892 actual0，2seed×16warm/16观察，98真实Editor请求；正式14436 actual0，4seed×64warm/256观察，1314请求。D3D12/Shader/Property/UAV/fence/source drift0。实际copy self-test的64分量逐bit相同；capture均256²RGBA32F、UAV=true。core原ValidationDiffuseStage后立刻typed copy，观察帧实际写入次数为pipeline3/4/5/6/9各1次，pipeline10四次。10记录末次结果，不把中间4次误当一个iteration。

实际绑定3=external OUT_DIFF_RADIANCE_HITDIST，4=transient0 slow，5=transient1条件HistoryFix/fast，6=permanent0 slow，9=transient0，10依次transient1/0/1/external output。每帧读取实际primary history albedo，以×64×albedo重调制RGB；5不是slow m2，所有stage只解释RGB。原HGS1二进制包含shader/application帧号、6个writer count及9阶段×18点×RGB，Float32存档；CPU double累计均值。负分量保留并计数。

| 同18点／4seed相对027参考 | normalized L1 |
| --- | --- |
| physical raw | .094054 |
| NRD input还原 | .094075 |
| PrePass | .094619 |
| Temporal slow | .112661 |
| 条件HistoryFix / fast | .097161 |
| Clamp slow | .113427 |
| first Atrous | .130687 |
| last Atrous | .462370 |
| physical publication | .462545 |

全部18点 RGB相对参考：首Atrous−12.987/−12.023/−12.743%，末Atrous−38.709/−57.173/−60.188%。主要误差增加发生在首轮之后的四个Atrous dispatch中，不能再无依据优先追normal编码/输入单位/宽HistoryClamping box。没有逐个保存pipeline10的中间轮次，因此不声称其中某一轮已被单独证明有错。

四seed的全图raw与filtered逐帧RGB均值均与026同seed逐条一致；18点raw/filtered均值也分别与026 maps逐bit相同。这证明本次观察在这些已核验输出上没有改变原结果，不外推所有GPU纹理逐帧逐byte一致。与027的8seed raw/ref L1 .057不同是预先固定4seed子集，不能当新回归。

原质量失败完整保留：full通过2/4，逐seed phase失败7/11/11/7；合并full误差−4.71818/−5.10748/−4.80745%，full仍失败、8/16phase失败。平均全域像素时间方差raw4.26391e−6、filtered5.60620e−9，后续候选必须面对噪声代价。这不是新GPU预算：copy/readback/文件输出均诊断开销。

已复读本机参考RenderingPlugin/External/NRD/Source/Relax.cpp：iteration count被clamp后gStepSize=1<<i；运行时Atrous shader与该参考版本按LF逐字节相同，gStepSize>4有随机offset。CPU上下文/原shader均在code-references及SHA，实际Native dispatch另有binding/count数据；不凭CPU源码声称当前DLL所有常量已逐字节反编译核验。

246精确source hash、7新小文本（含helper/meta）、2完整logs、2原JSON+6原streams lossless gzip、027参考与026输入SHA、analysis/verify约2.3MiB。Runtime/SDK/vendor/参数无改动，hook/finally释放与恢复已审查。`run_validation.ps1 -Mode qualification|matrix`隐藏Editor、自有进程15分钟/fence上限。`verify_validation.py`重算所有local stage均值/负计数、真实writer binding/count、完整帧序列、copy资格、原full/phase失败和observer一致性。

下一029只做一个Editor候选：GI私有ValidationSettings把atrous=5改3，保留前3轮、去掉后两轮最大步长；其它字段/采样/单位/格式/时域策略不变。先资格与相同4seed stage/local reference/full/16phase/noise对照，真实Native writer count验证3轮。原raw必须匹配；全部18点含暗邻点均报告，不能只看加权L1。若没有实质收益或有硬退化则搁置，不继续扫2/4迭代数。若有收益，再8seed/旧合格参考点/动态/HDR/两Player/GPU资格后才考虑生产。原F39/static/复杂动态/1080完整预算继续，目标active。
