# Ho-RTDI 静态彩灯能量：NRD阶段定位

第36步交付可复现阶段证据，当时未接受生产修正。目标仍包括主表面完整5%能量门槛、既有Cutout/镜面/立体Player门槛；候选不能只修其中一项。当步实验均恢复。第37步的[方差/固定交换修正](Ho-RTDI-DiffuseExchange.md)已通过上述门槛，并记录增噪取舍；以下保留第36步证据。

## 固定对照与定位

沿用第34步静态Quad、两非零红/蓝点灯，Cutout移远而仍在世界集合，F0=10/255、roughness=102/255；64² central256ROI，192预热/256观察。即时复制真实NRD diffuse输出，按实际linear signal scale恢复物理单位，0输入像素也评分。固定用户NRD 4.17.3头/RELAX源码及本地集成：pipeline4 Temporal、6 HistoryClamping、9首Atrous、10后续Atrous最后调用。

| 阶段 | 红/参考 | 蓝/参考 |
| --- | ---: | ---: |
| raw | 1.001966 | .981079 |
| Temporal | 1.000924 | .979831 |
| HistoryClamping | 1.000925 | .979830 |
| 首Atrous | .997767 | .981529 |
| 最后Atrous | .946180 | 1.086831 |

缺少近处Cutout时用原opaque profile2/2，最终仍.929641/1.115953，不能把问题归为Cutout profile独有。关闭history clamp结果约.946172/1.086845，历史8帧约.945394/1.087582；关闭ReSTIR spatial仍.960988/1.077340。此案例的主要偏差发生在后续空间滤波，大步长越过局部彩灯梯度；不是简单history长度或亮度比例问题。

固定其他设置，Atrous3最终.992161/.993944；请求最低2次约.997040/.983303。NRD允许范围[2;8]，历史名为AtrousOne的入口请求1会被SDK归到下限，实际不是单次；本步明确使用2并锁定旧A/B的五次baseline。

## 未接受的候选

- 共用迭代数降到3/4并按分辨率选profile，部分主表面/平面门槛通过，但镜面阶跃或立体Player能量失败。例如3次镜面frame L1 .357087>.35，4次候选Player蓝约.920688，不接受。
- DI-only RGB最大分量的重要性目标保持物理Shade不变，raw仍通过，但不能同时保住滤波/阴影门槛；五次主表面蓝约1.15，减少迭代后部分阴影红低约6%～8%、镜面变化像素误差约.487。不接受，RAB/compute/host实验全部撤去。
- 派生Atrous暂时让小尺寸diffuse大步长输出保留center、spec继续五次，平面/主表面通过，但立体Player阶跃蓝仍约.928451，未通过原5%条件。派生shader及生成器已恢复，无新产品分辨率策略。

没有放宽任何完整gate、补偿亮度、用更易的单灯替代实际双灯，或把局部通过宣称DI完成。本轮定位空间阶段的证据仍有效，但参数或硬半径截断不足以构成同时通过的修正。

## 保留的入口与后续

`RunPrimaryStages`、`RunPrimaryStagesNoCutout`、`RunPrimaryStagesAtrousTwo/Three`、`RunPrimaryStagesNoClamp`、`RunPrimaryStagesNoSpatial`、`RunPrimaryStagesHistoryEight` 固定静态阶段对照，diagnostic exit0仅代表raw及采集完成。完整 `RunPrimaryMotionQuality` 及新增Compatibility入口仍原门槛；恢复后实际exit1，qualityPass=false，不把阶段采集当质量通过。

Runtime/Editor Roslyn和原始SHA/派生重建检查通过；恢复后的current/atrous3阶段入口复现上述结果，完整失败入口exit1。生产/vendor/native/GI和Player模板未改，本步只提交Editor阶段探针、旧诊断参数范围修正及记录。实验Player构建/失败保存在隔离日志 `rtdi-primary36-*`，不称作当前生产部署验收。

下一项应以物理信号/方差及颜色梯度响应继续定位后续Atrous的取舍，保留立体与镜面压力门槛，不再按单一窗口调参数。仍需改善能量、噪声和历史响应，以及验证真实motion源和GPU成本；这些未被本步缩小或完成。
