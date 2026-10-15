# Ho-RTDI：稳定Clamping颜色guide隔离控制

第104步在103候选采样失败后，核对固定vendor/用户旧fork RELAX_Atrous：每轮spec range距离都来自该轮已过滤的RGB，后续会再改变guide。仅在隔离pipeline10改为使用本帧Clamping6颜色快照计算中心/邻居亮度距离，保留当前variance、phi、normal/view/roughness/material权重、row normalization、实际颜色和variance传播、采样与visibility。

临时ARGBHalf一份，每128² 131072B，追加一次stage6 CopyTexture及range读纹理；没有新ray，但GPU毫秒未测。原callback/shader引用退出恢复，反复写未变shader内容避免mtime错误。捕获704次、late pass2816次/环境组，前一快照需消费、每快照最多四late pass，避免漏捕或复用上帧。输出变化及实际调度支持消费检查，没有声称逐shader指令抓帧证明。

## 原门槛实际结果

正交环境两路径4/4、exit0，mixed：

| 复用 | 原filtered spec红/蓝 | 固定Clamping guide |
| --- | --- | --- |
| none | 1.050288 / 1.048948 | 1.022619 / 1.013773 |
| TS | 1.043948 / 1.050162 | 1.022141 / 1.018990 |

Raw/diffuse保持，无NRD参数变化；正交控制成功不代表泛化。64动态镜面slow通过，rapid/step frameL1 .353885/.354206超.35，整体exit1；原约.343487/.347752。step changed .310602在原响应门槛内，仍不代替逐帧质量。

透视环境2/4/exit1，mixed none红/蓝1.081850/1.076208，TS1.079261/1.088431，5%门槛仍失败。没有只按正交四组通过生产化，也未创建失败候选Player或新Feature。

四实际进程，Roslyn errors0、GUID唯一、无新增DI shader warning/error或SourceAssetDB时间错误，diff通过。报告 `rtdi-runtime78-env-baseline[-perspective]-stableguide104-*`、`rtdi-stableguide104-motion-rg.json`，在隔离工程。Runtime/vendor/generator/native/GI/Player未改。

## 下一项

固定颜色仍搭配随每轮传播缩小的variance，range带宽可能不再描述该guide的噪声。下一步以首Atrous9的颜色和其现有scalar variance一起作稳定guide，对照当前variance与匹配variance；不改raw second moment或按NRD normal预算全局换算（第66步已否决）。必须同时过正交/透视、动态/响应、shape与能量，临时资源成本明示，不继续调phi常数压线。仍只做DI，最终NPR和距离lobe合同另行保留。
