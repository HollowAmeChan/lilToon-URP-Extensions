# 公共层重构踩坑与禁止重复尝试

A01 / 2026-10-24：

1. 不把代码去重当显存收益。必须按同一物理纹理/格式/尺寸去重统计，permanent与transient分开；本步资源未减，1080匹配scratch仅约1.985MiB布局值，不能声称大幅节省。
2. 不将DI和GI的permanent历史混合；信号、单位、lobe、reset与更新时机不同。共享primary历史也须所有消费者读完旧值后才统一提交，不能让先执行的Feature覆写。
3. 不向公共NRD层塞DenseDI默认参数。调用者显式提供profile；DI策略在DI层，GI供原RELAX_DIFFUSE参数，保留独立历史和原精度。
4. 测试冻结代码只重命名C#类型。初基线生成器粗替HoRTDINrd前缀，把Shader的HoRTDINrdConstants也改了，frame0 parity失败；不改Shader/门槛，修正生成器只替完整类型后原GPU严格一致。错误源/报告保留。
5. RTHandle池分配前须初始化URP。初独立旧实现就在ReAllocateIfNeeded处NullReference，属于夹具未初始化，不是新架构证据；修初始化后才执行GPU对照。
6. 生产GI不应依赖DI类型，但集成测试会合法同时实例化DI/GI。初批量去using误伤两个测试导致CS0246，失败已保留；只恢复集成测试的依赖，不把它当运行时循环。
7. 旧合成测试动态增加AS实例命中已知F39 scratch debug，actual0也不能通过。本步保留错误并使用固定初始实例夹具，不修改AS/驱动或豁免D3D12错误。
8. 新组合夹具的60.字面量不符合C#语法，改60.0前已保留失败源/日志。已保留原SurfaceBuffer CS0108告警，不改本任务之外文件。
