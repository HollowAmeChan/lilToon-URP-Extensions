# 步骤020：pre-spatial历史候选有收益，等待生产接入

**接纳进入生产接入阶段，尚未修改Runtime。** 隔离候选保留同帧时间/空间复用，将历史改为时间阶段的packed结果。原5% filtered/raw门槛仍失败，完整目标未完成。

## 实验合同与真实进程

参考`ReSTIRGIContext.cs`的TS直接保留packed Spatial结果；Ho额外load/unpack→store/repack到buffer0。因此拆开比较legacy、packed Spatial1→0、packed Temporal2→0三项，后者在S关闭时复制1。所有其它代码相同，当前帧照旧读Spatial1做最终可见性/求值/NRD；未改SDK、Jacobian、gain、signal scale或NRD设置。

在自有项目`Assets/HoRTGIHistoryFeedback`生成独立compute，将三个include等义改为绝对package路径，仅替换一处Final历史store。8个kernel逐项解析，实际pass绑定受断言约束；finally恢复原shader并真实渲染核对重新绑定。原Shader未改。`generated/`与`packed-temporal.patch`可逐行review。

| 进程 | 实际范围 | Unity exit / debug / compile / source drift |
| --- | --- | --- |
| 40296 | 三项×四模式×2seed，16warm+16观测；24段/803总请求 | 0 / 0 / 0 / 0 |
| 10500 | 三项×TS×8个019固定seed，64warm+256观测；24段/7715总请求 | 0 / 0 / 0 / 0 |
| 30436 | 24个空/128/2048迭代GPU计时资格样本 | 0 / 0 / 0 / 0 |
| 35936 | 三项各64warm+40个实际GPU样本，含合成；1440个区间 | 0 / 0 / 0 / 0 |

RG/full256²/Linear/D3D12 debug，18灯/15实例/12Cutout/secondary sky，与017/019同支持域。小资格none/T/S的逐帧全图RGB与选点publication记录在三项间完全相同。两组共96个端点，6291456个pixel packed history/source比较全部相同；其中原版32个端点也与Spatial1逐字节相同。**额外编码在该实测域没有误差，不能认定它是偏亮根因，也不泛化到任意HDR/gamut。** packed Spatial没有画质收益。

实际矩阵中，三个policy同seed的首尾selected secondary与完整RIS hash全同，primary/域/actual temporal/frame断言过。参考仍为同模型15个收敛点与一个未收敛暗点，有事后选点偏差；未升级为独立全图真值。复用的capture DTO的旧`protocol`文字提到四模式，完整矩阵实际只测TS，范围以本节、`qualification`和每段mode为准；没有冒称完整四模式长矩阵。

## 质量与噪声

比较的是8个segment均值，SE按seed均值估计，不把相关帧/像素当iid。无复用比较源为019的同seed/full256捕获。legacy当前逐帧full数据与019 TS完全一致，packed Spatial与legacy亦一致。

| 指标 | legacy / packed Spatial | packed Temporal |
| --- | --- | --- |
| raw相对无复用RGB均值 | +25.70 / +22.21 / +18.75% | +7.73 / +6.77 / +6.28% |
| filtered相对无复用RGB均值 | +15.70 / +11.58 / +8.08% | +1.22 / −1.87 / −3.28% |
| full平均raw时间方差 | 5.00366e−6 | 4.54467e−6（约−9.17%） |
| full平均filtered时间方差 | 2.83890e−8 | 1.27640e−8（约−55.04%） |
| 选中15点45分量的raw非加权相对L1 | .41785 | .13950 |
| 同组选点filtered非加权相对L1 | .65422 | .46454 |
| 原filtered/raw full5%通过 | 0/8 | 0/8 |

选点L1有选择偏差且暗分量相对误差会放大，不是全图指标。点(128,128)红raw/reference从2.1819（seed SE/reference .1456）降至1.1333（.0286），filtered/reference从1.7208降至1.0944；(185,180)filtered/reference仍仅约.520，局部质量明显未完成。原5%、参考不确定性及所有旧失败保留，不能用新对照替掉失败门槛。

`results/matrix-images`有2048观察帧/policy的raw/filtered时均EXR及同曝光预览。PNG明确×100，EXR是原物理值；它们不是正常相机GI合成图。已实际查看原版/候选filtered预览，场景形状与材质细节保留，亮度有变化；这不替代动态或独立真值验收。`image-artifacts.json`绑定全部12个文件的SHA。

## GPU成本与边界

资格空/短/长中位数为0/.006144/.039936 ms，非空严格正、负载分离、频率/完成fence与slot检查过。每policy 40样本：

| policy | GI.Total median / p95 ms | Final median / p95 ms |
| --- | --- | --- |
| legacy | 1.043456 / 1.611776 | .027648 / .035840 |
| packed Spatial | 1.056768 / 1.793024 | .046080 / .049152 |
| packed Temporal | 1.018880 / 1.552384 | .046080 / .054272 |

Final增加约.018432 ms，GI.Total相近但波动很大且顺序固定，不宣称总性能提升。无新reservoir分配，候选读取另一份32 B/receiver；不把逻辑读取量说成实际DRAM/VRAM，亦不由256推算1080已过。clock sidecar仍仅Editor，没有新增产品native依赖；本步没有新Player payload证明。

## 证据与下一动作

`run.json`是最终GPU两进程；qualification-original/matrix-original/gpu-original均有实际run及精确source。246个source hash中234复用既有内容寻址blob，其余新存；原始大JSON仅gzip，解压字节/SHA不变。完整日志、源/生成shader/报告/图片/分析均保留。总归档约7.03MB。

运行`python Tests~/RTGI/HistoryFeedback/analyze.py`与`verify_validation.py`重算/核对四进程、完整端点与反事实控制、8seed差异、15点边界、GPU资格/1440区间和原5%失败。旧Seeds verifier的当前源码SHA因合法Editor扩展过时，按4019bd7验证，不回滚新工具。

下一项021直接生产接入与回归：Runtime Final仅在S开启时原样复制packed Temporal2至history0，S关闭保留原store以限制未测HDR行为；同帧T/S/visibility/NRD不改。加实际生产history源断言，跑RG/compat、原环境/材质/Cutout/蒙皮及真实动态响应，构建两个独立Release Player并确保无需GPU探针DLL。结果有硬退化则撤回。020 Generate依赖旧legacy store合同；生产改后不可原样调用它并错标baseline，需使用本步历史源码或显式适配。

完整5%、static base/F39、moment-HDR、正常enabled-camera、1080/完整预算仍未交付。不要因本步有收益标goal complete。


041诊断语义更正：当时CPU Packet字段标签偏移与SDK不符，旧normal@12实为misc/M/age、misc@16实为LogLuv radiance、radiance@24实为packed normal。32字节全包Equal覆盖全部word，因此原packed-source复制/texture质量判据仍成立；旧分项normal/radiance计数和JSON标签不能按字面使用。原源/原报告不改，映射和实际GPU ABI验证见[ReplayContract](../ReplayContract/README.md)。
