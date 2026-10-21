# 步骤022：GI私有NRD池精度有收益，生产格式未改

**RGBA32F候选继续验证，尚未进入生产。** 只将GI自有实例四个RGBA16F池改为RGBA32F，scale64、原NRD/epsilon/settings/历史预算、external signal/output格式保持。DI实例与Player没有选择该策略。

## 机制与实际资格

Temporal4的slow `gOut_Diff`写transient0，Clamping6的slow写permanent0；AtrousSmem从alpha二阶矩求variance，后续Atrous继续使用alpha。四个RGBA16F池覆盖slow/fast history和variance ping-pong，因此本次先测整个颜色池精度上界，未改shader编码或猜单一alpha消费者。`ValidationPoolFormat`只存在UNITY_EDITOR，默认null，回调只设置在这一GI core实例；格式改变重启该实例的NRD历史。

阶段审计使用独立float32 `Texture.Load`→RWTexture store kernel。16个Vector4、64分量，包括1e−12数量级，先用GPU seed/copy/readback逐bit检查。不能把高精度源再CopyTexture至half而称精度审计。

| 实际进程 | 终态 |
| --- | --- |
| 47656 | 首64²资格exit1：selector刚设置，尚未经过下次Prepare，实际格式未切换；拒绝保留 |
| 48792 | 补一次真实Prepare/render后资格exit0，64分量copy一致、4池实际32F、debug/compile/drift0 |
| 9360 / 24456 | 首长比较baseline exit1、candidate exit0，但stage copy目标漏UAV标志；两组均拒绝作为阶段精度证据 |
| 37676 / 26252 | 修正capture后的正式长比较baseline exit1、candidate exit0，debug/compile/binding/drift全0 |

长比较均原古堡256²，32初始warm+256settle+256观察，同已合法AS、18灯/15实例/12Cutout/sky，按Editor tick分散。两组256条raw frame RGB均值逐条完全相同，表明采样输入不变；copy额外工作仅用于诊断。完整池格式/Native绑定、full/原phase0/全部16phase、阶段RGB和slow moment记录保留。

## 正式结果

| 指标 | baseline16F | GI pool32F |
| --- | --- | --- |
| full filtered/raw RGB % | −5.8515 / −8.0171 / −8.9279 | −4.5586 / −5.2075 / −4.9433 |
| 原stride0 gate | 失败 | 通过 |
| full原5% gate | 失败 | **仍失败：green** |
| 16相位失败数 | 16 | 8 |
| Temporal正均值但m2=0 | 972674/982725（98.9772%） | 256/984788（.0260%） |
| Clamping正均值但m2=0 | 972865/982725（98.9967%） | 256/984784（.0260%） |
| full filtered平均时间方差 | 1.261775e−8 | 5.534688e−9（−56.14%） |
| pool payload bytes | 2818304 | 4915456（+2097152） |

每阶段1048576个stride4样本；这次采样网格未见m2饱和，不能外推HDR范围。仍有256个正均值/m2=0样本，不称全部矩缺陷修复；severe-moment指标沿用旧half舍入容差，不能当32F严格误差界。四池为permanent0/1、transient0/1，external input/output仍R16G16B16A16。额外2MiB是256²逻辑payload；1920×1080同布局投影63.28125MiB，**没有实际1080/VRAM/性能验证**。

正式candidate真实exit0只表明旧Root的stride0 gate通过。full绿色仍低5.2075%、8相位失败，完整画质不接纳；不能挑stride或因矩恢复就complete。默认单位64未改，不重启F35纯单位扫描。

## 捕获失败与更正

首长比较的self-test和pool格式均成功，但实际阶段目标RenderTexture没有UAV flag，Unity报`Attempting to bind texture as UAV ...`，所有stage RGB/moment读回0。旧runner只匹配Shader error/warning漏计该绑定错误，所以其`shaderCsDiagnostics=0`原样保留，并明确拒绝，两组不能支持moment结论。原publication仍有值，不把它冒称capture有效。

修正仅给precision捕获目标加enableRandomWrite，Copy入口还显式拒绝无UAV目标；runner新增绑定错误匹配。重跑后获得非零正确stage与moment，原两attempt/source/log未改绿。小资格selector未执行Prepare同样保留，不伪装成Native格式不支持。

## 证据与下一步

六实际进程、全部源/报告/log/SHA归档。250个source hash中仅9个新blob，其余引用既有blob/Git-backed来源；原JSON仅gzip，解压字节/SHA原样。归档约.98MB。`analyze.py`重算统计，`verify_validation.py`核对六attempt、真实格式/绑定/64分量copy、raw一致、矩/计数/相位、两种捕获失败和仍失败的full gate。

下一项先完成HDR、动态与成本资格，再考虑生产：可用GI-only constructor策略，DI默认保持16F；Editor callback必须能显式覆盖native格式供baseline，不把新默认误标旧baseline。HDR建议用同支持域解析发光面从低信号到20000物理辐射，在scale64下实际slow m2可超过65504，验证32F有限/无溢出及重置；S关闭的解析能量与默认TS状态分别验收。还需分辨率/启停/释放、GPU已知负载计时和两个真实Player资源格式/动态回归。先评额外payload与GPU成本，不能因本步有收益盲改所有NRD或尝试HD。原core AS/F39、normal enabled-camera、full局部/HDR与1080/完整预算继续，goal active。
