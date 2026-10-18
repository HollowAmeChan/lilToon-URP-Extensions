# Ho-RTDI：dispatch参数的数值快照

> 状态：技术专题与接入证据。步骤中的测试数值及“下一项”保留当时语境；当前默认值、支持范围与剩余问题以 [Ho-RTDI 总文档](../../Ho-RTDI.md) 为入口。

第85步修复实际pass仍持有可变HoRTDISettings引用的问题。此前只有final budget在Setup固定，其它环境候选、时空重采样、样本预算/半径与ray bias在执行时继续读取原对象；记录后修改设置可能让shader参数与已准备的signature/resource模式不一致。

现在Setup构造readonly PassSettings值，Snapshot/Data复制该值。收集阶段已使用的geometry/light layers与material排除列表不放进dispatch快照，denoise资源也由前置准备确定。当前帧继续使用已捕获值，下一次Setup读取修改后的设置；不把settings克隆成新的每帧heap对象或引入通用配置框架。

参考用户fork `NrdDenoiserHelper.CommonFrameInput`的numeric frame input与`RtxptCameraFrameState`的current/previous帧状态；本步只补DI参数的相应契约，不搬入其PT/native框架。

两路径contract测试实际调用当前pass Snapshot：记录后修改所有dispatch消费字段与finalSampling，确认旧Data和新Snapshot（尚未下一Setup）仍保持原值；下一次真实GPU render/Setup后，全部新值和预算才出现。各32项检查通过、exit0，next-frame shade有限且有效。测试不是只比较一个手工新struct。

无中途readback的72帧single/multi、opaque/native、尺寸切换两路径通过。Single RG及Multi compatibility两个Win64 Mono release构建errors0，实际D3D12 Player完整suite pass/exit0；原raw/filtered MSE保持约.0504/.000526与.019103/.000206693。Roslyn errors0、GUID唯一/diff whitespace通过，无新增DI shader诊断，旧外部构建警告保持。

这证明settings对象变动不会改变记录后消费的数值，不宣称scene/geometry/资源可以在已记录pass执行前任意变更；它们仍遵循现有同步和输入stamp契约。Shader/native/GI/最终NPR合成未改，已知其它质量/性能限制继续保留。

报告`rtdi-settings85-rg/compat.json`、`rtdi-snapshot85-player-*`及snapshot85日志位于隔离工程。下一项继续实际DI质量/场景支持，按明确的新发现推进，不以更多快照抽象替代功能工作。
