# 步骤023：GI高精度颜色池与NRD输出已接入，剩余门槛保留

最终策略只影响GI实例：四个RGBA16F颜色池提升为RGBA32F，承载PrePass/最终结果的external output也提升为RGBA32F。SignalScale仍64，signal/normal/filtered仍原格式；Shader/NRD参数/算法不改。共享core新增构造参数默认false，GI显式true，DI默认不变。Editor override按native format重建明确的baseline。

## 从失败得到的边界

首HDR RG48476/compat17312实际exit1，夹具用Color.white×amplitude同时缩放alpha；lilToon emissionBlend还乘alpha。源/失败保留`rejected-emission-alpha`，未改算法/阈值。改为明确RGBA(amplitude,amplitude,amplitude,1)，且在断言前记录数值。

34708/12596仍在最低1e−4信号处实际exit1：raw通过，filtered蓝低约7.7%，不能当输入错误排除。47580/37696把失败延迟到完整HDR/生命周期记录后，仍actual1；13764原生16F对照actual0仅表示测量完成，它的低信号5%也失败。pool-only已有改善但external output仍是PrePass和最终信号共用16F，不能称全范围通过。所有原记录在`low-signal-failure`和`pool-only`，没有改绿。

只增加GI output32后，38732 RG、36228 compat的三个范围均按原5%通过，actual0/debug0/compile0/binding0/source drift0；不再扩其它格式。这是有观测依据的后续策略，保留pool-only反事实。

## 最终HDR与生命周期

| 发光面物理幅度 | RG raw约 | RG filtered约 | slow m2 max | 原5% |
| --- | --- | --- | --- | --- |
| 1e−4 | (.000100595,.000099897,.000099164) | (.000099164,.000099164,.000095348) | 2.38832e−12 | 通过 |
| 1 | (1.00587,1,.992188) | (1.00543,.999890,.992081) | .000244474 | 通过 |
| 20000 | (20102.5,19974.6,19833.4) | (20101.6,19972.2,19817.7) | 97653.0 | 通过 |

compat同样通过；每次幅度变化首帧重置。明确测试范围1e−4..20000，不是任意HDR保证。S关+T开用于解析能量，避免F23单像素MC误用。四实际GI颜色池32F、default constructor control仍四16F；96→64、NRD禁用全部旧pool handle释放、重新启用新instance、原双camera/最终32尺寸生命周期均验证。旧NRD基本均值/方差/相机/启停门槛继续执行，完整检查见报告。

## 实际生产与真实Player

16612实际Runtime long：256观察帧/full/16phase/stage，actualexit0、debug/compile/drift0。Root原stride0通过，但fullRGB filtered/raw仍−4.49450/−5.05869/−4.72749%，fullgreen及7/16phase仍失败，**不算完整质量通过**。阶段捕获自动按production32模式用经过位检的float32 copy，避免旧CopyTexture半格式路径失效；不设置pool override。

最终两个Win64 Mono Release build42680/43124，Player2932/17740均actual0/debug0/runtime diagnostics0/source drift0。独立PrecisionOutput资产/目录保留pool-only旧payload。各315真实Update请求、39 checks、23 history与23 pool状态：四pool32F/无half颜色池、signal16/output32/构造flagtrue、disabled frame270旧handle释放。原pre-S 0=2、反事实1/2不同、nonreadable base4、骨骼/Morph/Cutout/启停/相机/能量/合成门槛继续。空间L1约.105689/.105668，未放宽原.15；完整payload SHA，无HoGIGpuProbe.dll。

pool-only build5432/3456与Player6252/3340也保留，但它们不是最终output32证据。最终Player分backend保存截图，避免021的通用名覆盖。仍是一次真实Update对应一次SubmitRequest，不冒称普通enabled-camera自动呈现。

## 成本与资源

GPU clock32200资格有效；pool-only baseline43588/32F19896每项40样本/fence完成，但GI.Total median47.46/38.37ms、NRD p95出现25.12/12.91ms尖峰，不能作为稳定比较。事后nvidia-smi 21:41:11快照显示GPU100%、6296/8188MiB；它不是并发跟踪，不能证明根因，未干预其它进程。

最终完整策略8372另40帧实际生产记录：GI.Total median1.042432/p95 1.891328ms，NRD .129024/.131072ms，所有区间freq/fence/tick有效。不将不同时间/负载的记录宣称总加速，也不算目标分辨率或完整Player预算。

颜色池额外2MiB加output额外.5MiB，共+2.5MiB@256；1080同布局投影+79.1015625MiB。只是逻辑payload，不是物理VRAM或实际1080资格。未在原F31条件重试HD。

## 证据与接手

22个真实进程的源/日志/SHA、原输入/低信号失败、pool-only与最终格式、所有build/Player/payload及统计保留。1197个source hash仅25个新blob，其余复用旧blob/Git对象；原大JSON只gzip，解压逐字节一致。归档约6.14MB，`analyze.py`与`verify_validation.py`验证真实范围/失败/池/Player/成本/当前唯一生产策略。

本步保留精度与低信号范围改善，未解决完整full/local质量、AS增长/F39、normal enabled-camera与1080/完整预算。下一项优先完成正常enabled-camera呈现证据：先读DI原隐藏宿主记录，只有新且具体的机制才重新试，不把请求相机冒称自动帧，不绕过可见窗口授权限制。后续质量需多个独立seed/局部误差与有限MC不确定性，禁止为5.05869%放宽阈值或盲调增益/单位/格式。
