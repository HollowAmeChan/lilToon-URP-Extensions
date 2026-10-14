# Ho-RTDI：零F0弱彩灯提案生产修复

第93步将第92步隔离验证的平方根RGB重要性接入共用ApplicationBridge：只在 `HoDIUsesRgbImportance` 成立（DI启用且量化F0全零）时改变选样target。实际Shade、BRDF、Fresnel、source PDF、SDK、采样数、历史预算、默认Single、辅助1/PDF、NRD和材质类别保护不改；没有新增设置/pass/资源/射线。

弱彩灯在既有96帧100:1变化里，主reservoir贡献偏高且方差大。目标由完整RGB贡献长度改为其平方根，使弱贡献得到更多相对选样权重；初始、时间、空间、偏差修正仍用同一目标。非零F0亮度分支保持。零贡献保持零，真实输出由最终权重和原radiance/BRDF求值，没有对画面做gamma或tone-map。

沿用户fork target/SDK合同生产化；测试生成器增加明确marker及exact-expression守卫，识别已生产化的目标，不重复开平方，也不接受其它未评审表达式。GI明确 `HO_RTDI_RGB_IMPORTANCE_ENABLED=0`，仍走原亮度分支；实际共享GI合同回归通过，未改GI功能。

## 接入后验收

- 默认Single RenderGraph的16组reuse/noise通过、完整primary motion质量通过，实际exit0。
- 生产Triple实际动态九阶段全部通过：100:1 weak red raw1.014597、filtered .999766；原3%/5%/dark .001门槛不变，实际shader结果与第92步候选匹配。
- 两个Win64 Mono release构建成功；实际Single RG与Triple compatibility Player全部probe pass/exit0，D3D11 inactive pass/exit0。Single/Multi filtered MSE .000526306/.000219972，HDR相对误差 .010916/.010861，材料边界、深度隔离、彩色镜面、环境和初始可见性门槛保持。
- 共享GI RenderGraph实际五阶段、secondary analytic/sky/emissive能量、world遮挡、temporal/spatial/history通过。没有把GI文件间接引用等同实际运行。

第92步额外Single32项256/512 F0边界、六项运动、HDR旋转天空是同表达式的隔离证据；第93步没有宣称全部再跑一遍。运动changedL1余量仍较小。未做IL2CPP或四个Player预算/路径组合全面重建，也不将通过的两个组合扩称全平台验收。

日志前缀 `tempered93-`；实际Player报告 `rtdi-tempered93-player-{single,multi-compat,dx11}.json`，位于隔离工程。Roslyn errors0、diff通过，DI/GI无新增shader warning/error。release日志仍有SolarHD/Shadows除零和lilToon重复keyword警告，在第84步原构建日志也存在，本步没有混入这些其它模块修改。

## 剩余限制与下一项

弱红primary相对时间方差在隔离固定序列165.87→15.01；该结果不代表任意材质/视点/灯域普适改善。GPU毫秒仍未证明，平方根额外算术不称零成本。保留混合天空/局部灯的非零F0镜面filtered能量失败、最终NPR/GI接收边界；古堡仍只作特殊合并几何支持审计。

下一步回到已知混合天空镜面filtered偏高的可控失败：raw能量已接近解析值，应先定位真实NRD阶段与信号合同，核对用户NRD参考及固定vendor实现，不按失败样例直接改输出或放宽5%门槛。
