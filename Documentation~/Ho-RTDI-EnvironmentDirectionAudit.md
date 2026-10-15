# Ho-RTDI：primary环境方向与MIS单位审计

第102步基于101主样本噪声来源，核对用户fork RAB BRDF混合、固定SDK LightBrdfMisWeight与实际环境方向。只加Editor读取，没有更改提案、样本数、ray、visibility、NRD或输出。

## 合同核对

当前BRDF在两lobe有效时cos/VNDF各.5，sampler和EvaluateBrdfPdf使用同一概率；无spec则纯cos，纯metal则VNDF。NoV=1平面下VNDF反射PDF为D/4，混合为 .5*(NoL/pi+D/4)。VNDF下表面无贡献事件不会重采样或归一化为上半球条件PDF。

HoAtmosphere等面积映射每texel面积4pi/N²。实际texel选择概率乘 `RAB_LightSampleSolidAnglePdf=N²/(4pi)` 后成为方向密度；SDK在此单位与BRDF PDF混合，然后除回这个转换项。因此texel PDF不能直接与sr^-1 BRDF PDF相加。

实际常量绿色PDF每texel相同，归一化后方向密度1/(4pi)，本次max相对误差0。local/environment/BRDF候选由固定SDK按候选数归一化；局部空域的dummy count和下表面零贡献不能按少了有效候选就随意改分母。

## 实际方向读取

128²原四场景，512帧×四固定点，每组2048probe。一次读取两个16²reservoir block，使用实际packed UV、finalized weight与cached visibility；独立CPU GGX按解码方向重建绿色贡献，与raw.g比较。该fixture只有天空携带绿色，不能外推到任意彩色灯；方向分布来自最后选择/复用的primary，不代表初始BRDF候选直方图。

两路径全部数值相同，最大绿色重建相对差 .09488%，保持.3%门槛。mixed none/TS分别1873/1890次environment、175/158次analytic：

| 场景/复用 | NoL>.9的环境样本比例 | 其绿色信号和份额 | 其绿色信号平方和份额 |
| --- | --- | --- | --- |
| sky none | 27.05% | 69.39% | 91.34% |
| sky TS | 24.80% | 68.07% | 91.57% |
| mixed none | 26.43% | 69.05% | 91.44% |
| mixed TS | 25.03% | 69.30% | 92.28% |

平方和不是去均值方差贡献或可独立相加的概率份额；这里仅表明高NoL方向对大镜面值影响强。没有发现此次PDF单位/解码错误，不据此证明任意BRDF、遮挡或MIS组合全部正确。

正式环境仍2/4，两实际进程exit1，报告 `rtdi-envdirection102-rg/compat.json` 明确diagnosticOnly；原始/发布数据保持。初次Roslyn检查发现误用不存在的context字段，改为实际PDF纹理宽度后errors0才启动Unity。GUID唯一、shader/diff通过、无SourceAssetDB时间错误。

## 下一项

单独测试在非零F0且两lobe有效时，将现有四个BRDF候选转向VNDF，保留四个环境PDF候选、实际shade、共同PDF回调、总预算与局部辅助方案。它只是候选分布控制：RIS最终target和重用相关性仍限制收益，不能把初始方向更集中等同最终方差必降。必须同时守住diffuse、混合环境、动态/primary门槛，不按一个镜面值接纳，不新建lobe reservoir或PT/GI框架。
