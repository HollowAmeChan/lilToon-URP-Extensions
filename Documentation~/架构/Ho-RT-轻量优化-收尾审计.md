# 轻量优化阶段收尾审计

2026-10-24。针对本次用户授权的有界轻量资源优化目标；不将原完整GI质量研究视为完成。审计基于当前源码、旧实现独立GPU对照和实际run，而非只看pass清单。

| 目标要求 | 当前权威证据 | 判定 |
| --- | --- | --- |
| 公共NRD解耦为基线 | A01的6d7d3dc6/8740faa7；本步Runtime/NRD与RTDI无diff | 保持 |
| 生命周期不重叠纹理复用 | HoRTGINrdContext.filtered=>signal，独立filtered分配/释放移除；Shader/Feature原字节不改，执行顺序FinalNRD→NRD→Resolve | 完成 |
| 数值不变 | 冻结旧wrapper，同一实际输入/common/settings、独立旧池/旧Resolve；RG与compat各41帧1318912分量全RGBA逐位同，正式两条report一致 | 完成（已测范围及生命周期推理） |
| 独立历史/reset、raw/32F与公开设置保留 | 原Feature/两个Shader SHA不改、DI/公共NRD无diff；world reset/单模块关闭/resize/release及原GI41项回归 | 完成 |
| 原输入后读诊断 | 6处读取/请求迁移；8帧可选snapshot与旧输入逐位同，关请求后快照对象确实释放；Player不编译快照字段 | 完成 |
| 实际资源账本与收益 | 真实RT instance/format/尺寸去重；64²−32768B、32²−8192B；44→36 B/像素，snapshot开启时额外8 B/像素 | 完成 |
| GPU与双路径 | RG/compat配对各74请求；曲面、零albedo、物理HDR1015、正signal5.96e−8；原GI NRD各243请求41check | 完成 |
| Release Player | 构建actual0，3条精确历史warning原metadata保留；隐藏D3D12/Linear Mono304请求16check，运行时diag0且库hash一致 | 完成 |
| 前后review/取舍 | 过程记录A02预审/后审、filter-review、禁试/交接和原始run/源SHA | 完成 |
| 过滤提交与提交后复核 | 待本步骤精确白名单提交后记录实际hash与再次验证 | 提交前待办 |
| 保留047/他人改动 | 原过程预审和PlanarReference在开始时已存在；不加入本步白名单，未覆盖/清理 | 保持 |
| 自定停止/重路线搁置 | 目标与停止条件预先明确；primary帧时序/pose共享/heap alias/合并历史/降精度/原GI研究均不做，scratch低收益搁置 | 达到收尾条件 |

范围说明：1080节省15.82MiB是描述符布局计算，没有在1080分配/计时，不声称总GPU内存/FPS改善。可选snapshot启用的诊断帧无本项节省；raw和NRD32F输出不删。没有宣称所有潜在CPU/显存重复分配都已清零。本阶段达到用户允许的停止条件：取得可靠低成本收益，后续较重路线明确搁置。

当前公开GI功能不启用NRD原生validation pass；未来新增该路径必须重新审查输入/OUT_VALIDATION别名。该将来扩展不列为当前功能已交付。

提交、复核、交接完成后，本有界目标可标完成。
