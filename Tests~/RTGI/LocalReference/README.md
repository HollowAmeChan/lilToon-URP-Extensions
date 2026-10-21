# 027 新局部坐标的同模型参考与实际 NRD guide

本步确认了值得处理的局部欠亮，**没有修改生产算法或完成画质修复**。026 raw/filtered 差不能直接当物理误差；本轮为固定6个中心点及12个邻点生成新的无复用高样本一跳参考。

固定 y=204/206/226/227/252/255，每行 x=85/86/87，共18点，全部报告。使用017原sample-lattice/primary-loader包装，复用原Generate→次级SDK RIS→alpha final visibility→Final；参考不做T/S/NRD，主相机仍是当前生产默认TS+NRD。256²同古堡、18活灯/15实例/12Cutout/次级天空、8 local/4 environment、.001 bias/1000距离，黑事件计入、reference FP32。新点有既有探索选择偏差，同材质/light实现的参考也不等于独立绝对着色真值或全图验收。

初资格18160 actual0，2批×512，只证明18 primary/guide链路；正式22748 actual0，2048独立seed批×512=每点1048576样本，共18874368。18/18满足原SE≤2%/split≤5%及positive/低信号标准，最大相对SE .84477%、最大split2.97214%、最少positive250448。compute/full/history primary最大差约1.89e−6，raygen/compute差0；场景/light/sky revision及normal/albedo稳定。主流程请求36/2082，不是普通相机/性能测试。

与026实际8seed publication maps 对比：

| 固定域 | raw/reference 归一化L1 | filtered/reference 归一化L1 |
| --- | --- | --- |
| 6中心 | .067544 | .494028 |
| 12邻点 | .046567 | .423096 |
| 全18点 | .056952 | .458213 |

中心6点 RGB总量 raw 相对reference +1.490/−1.242/−.994%，filtered −42.161/−59.933/−62.224%；逐点filtered/reference约.280～.651。邻点也存在真实结构差异，并非只一个极值。全部18点、8seed及heldout后4seed的RGB均值/SE/比值保留 analysis，不挑合格位置重新定义全图门槛。Reference report.mainFrames=0，旧mainRaw/mainFiltered没有本轮均值，比较明确来自026 maps。

首尾批次读取同一主context的normal/roughness、viewZ、scaled signal、raw、filtered：normal/signal/raw/filtered均RGBA16F，viewZ32F，scale64。按实际NRDConfig编码2/roughness1独立重建pack/decode：最大pack误差.000228882、decoded normal差.000324177、viewZ差0。signal×64×albedo与同帧raw在max(5e−6,raw×.002)输入精度界内，最大绝对误差约1.077e−5，不能解释中心约40%～60%的欠亮；这里只检查首尾，不外推所有帧/HDR。

相邻输入确有结构差：前4行x85较中心viewZ低约.155，法线接近平行，albedo由约.02跃迁到约.4；后2行x85已在同一较后平面。具体NRD阶段及跨边界机制尚未归因，不能据geometry相关性直接定性bug。

最终review增加normal/depth显式finite guard，防NaN绕过大小比较；36548重新编译并实际36请求资格0。正式参考在新增guard前运行，review精确绑定唯一源差异；verifier对其所有实际guide独立检验finite，未伪称重新跑完整参考。三进程runtime API/Shader/Property/UAV/fence/drift0，Runtime/SDK/vendor/NRD设置无改动。

242精确source对象、9个新小文本（含场景及NRD编码来源）、3完整logs、3原JSON lossless gzip、026输入SHA、analysis/review/verify，约2.5MiB。`verify_validation.py`从原2048批重算全部收敛条件/黑样本计数并独立解码guide。原C# LINQ Average(float selector)先返回float再赋double，verifier复原这一既有精度语义，避免把decimal解析误差误报为split不一致；门槛及原结果未改。`run_validation.ps1 -Mode qualification|reference`自有项目隐藏运行，15分钟/fence只停自有进程。

下一028：固定相同18点和4个预声明seed（0/104729/5347609/6396185），以原单位64和默认设置读取NRD input、PrePass、Temporal slow、条件HistoryFix/fast、Clamp slow、首末Atrous及physical publication；用F43合格32F typed copy/UAV/binding guard和真实绑定，保留全图/16phase/原5%，以新参考量化阶段差再选机制。未归因前不调gain/sigma/单位/格式、不复活F36同box线段候选。原全图5%/phase/F39/static/复杂动态/1080完整Player预算继续，目标active。
