# 029 Atrous 5→3 单候选

**Editor候选有收益，尚未接入生产。** 依据028首Atrous到末Atrous局部误差显著增加，只把GI实例的atrous从5改3，保留原单位64/production32F/时间历史/采样/其余参数；没有迭代数扫描。默认Runtime仍5，DI不改。

资格8792 actual0，2seed×16warm/16观察、98请求；正式632 actual0，原4seed 0/104729/5347609/6396185、各64warm/256观察、1314请求。API/Shader/Property/UAV/fence/source drift0，F32 copy资格继续。148字节RelaxSettings只有offset92的uint由5改3；实际Native每帧9一次+10两次=3轮。finally恢复hook/capture，之后正常render实际AppliedSettings回5。全部原字节/stream/报告保存。

候选正式测量前保存review-contract：固定18点加权L1至少下降20%；原full/phase通过项不能转失败；每个点/通道的absolute reference error增加超过max(5e−6,ref×5%,2×配对seed及参考组合经验SE)判硬退化；全域filtered/raw方差比先以.15作筛选代理。经验SE不是正式置信保证，代理方差不替代原动态/解析夹具，原5%全图门槛不动。

| 同4seed指标 | 5轮baseline | 3轮candidate |
| --- | --- | --- |
| 全18点/reference normalized L1 | .462545 | .307095 |
| 原full通过seed数 | 2/4 | 3/4 |
| 合并full RGB误差 | −4.718/−5.107/−4.807% | −4.538/−4.545/−4.091% |
| 每seed phase失败数 | 7/11/11/7 | 6/4/11/5 |
| 合并phase失败 | 8/16 | 6/16 |
| 全域平均像素时间方差filtered | 5.60620e−9 | 7.76781e−9 |

L1下降33.6076%；原相位没有pass→fail，合并full过原门槛，仍有一个seed full与6合并phase失败。方差提高38.56%（各seed约34%～44%），candidate filtered/raw方差比.001822；这是诊断场景静态结果，不是完整动态/Player/预算资格。局部仍有明显欠亮；暗邻部分通道小幅变差，未超预设硬退化界，全部18点/通道及时间方差在analysis，不能用加权平均隐藏。

原raw全帧均值及所有局部raw样本与028逐bit一致，input、Pre、Temporal、条件fast、Clamp、first Atrous局部原流也逐bit一致；差异发生在减少的后续空间滤波。报告参考沿027同模型新18点，选点/同材质light限制与旧未定参考保持，不是独立全图真值。

246精确source、仅3新小文本、2完整logs、2原JSON+6原HGS1流lossless gzip、review-contract/028与027输入SHA/analysis/verify约2.2MiB。新增设置scope/记录仅Editor，捕获和settings恢复已审查。`run_validation.ps1 -Mode qualification|matrix`独立输出，隐藏Editor、自有进程15分钟/fence上限；028旧数据不覆盖，旧verifier在所属commit核验。

下一030先完整8seed及旧合格参考点/暗控制/全图/16phase/噪声边界，沿同一个固定3轮，不扫描2/4，不用4seed通过代替整体。若继续有收益，再实际动态/HDR/两Release Player/GPU资格后决定是否改生产；期间Runtime保持原5。所有F39/static非零base/复杂动态/1080完整预算继续，目标active。

后续状态：030完整8seed/旧合格点出现硬退化，固定3轮已搁置，当前候选入口已移除，生产仍5。029是历史初筛收益，不能当最终接纳；见../AtrousQualification/README.md。
