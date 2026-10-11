# Ho-RTDI 单个解析灯精确路径

只有一盏 point/spot/directional、且当前没有有效环境采样时，灯的选择与 UV 都没有随机自由度。此时继续做 reservoir 时空重采样会引入无收益的权重方差，再送 RELAX 可能使硬阴影失真。本步保留 SDK Initial 与必要的历史/灯映射，跳过 Temporal/Spatial；有降噪资源时把实际 raw 复制到原有发布输出，跳过 Confidence/RELAX/Resolve，不改变 NRD 算法或材质接收。

多灯或有效天空仍执行原完整 ReSTIR/NRD 路径。单灯变成多灯时，NRD history 标记无效、从当前信号重新累积；light snapshots/remapping 与主表面历史仍保持有效。`lastTemporal` 表示本帧实际执行的 reservoir temporal，精确路径为 false，不用这个优化伪造旧 stage 的累积。

## 证据与验收

独立平面接收器、骨骼驱动的 Cutout 平面、点灯，原生 anyhit 仍读取真实纹理。阴影包含几何边缘与中央 alpha 洞，CSV 保存最后观察帧的像素参考/raw/发布值；不是古堡整体场景。64帧×896像素/模式，camera 做小幅平移，caster 做骨骼平移，静态时分别打开 T/S/TS 验证退化条件。

改动前单灯 staticS/staticTS 的 raw 能量约 .9983/.9992，但逐帧 L1 方差约 .2081/.2256，NRD 能量约 .9253/.9157。关复用时静态 filtered 很准确，但 camera 运动 L1≈.1005，距离边界1/2像素内的最大 leak≈.1026/.0508；不能将总能量近1当作边缘准确。

改动后两条路径六组 raw/发布一致：静态/相机/遮挡物能量约 .999689/.999687/.999672，L1≈.000311/.000313/.000328，1/2像素 margin 的阴影 leak 均0；原 3% raw能量、5%发布能量、15%发布L1、2%阴影core gate通过。这是去除无噪声条件下不必要的处理，不是 NRD 已修复所有运动/多灯边缘。

旧动态灯 transport/remapping/age fixture 增加一盏0通量控制灯，保留原解析能量参考，在非单灯调度域实际执行时域；这不证明非零双灯质量，后者仍有原双灯统计/降噪 gate。旧 snapshot 断言按 Light 身份取索引，避免假定slot0。单灯的相机/颜色/强度断言改查有效历史与精确输出；多灯 RELAX 与启停仍检查真实dispatch。

NRD官方将RELAX定位为RTXDI辐射降噪，并要求对应信号/命中距离/运动输入，见 [集成说明](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md)。用户参考 ComputeGradients/ConfidencePass 与 pinned SDK resampling 已核对；本步未改vendor、native ABI、guide布局或最终BRDF。

旧核心、NRD、Cutout各两条路径，以及材质边界/HDR、环境窄镜面、曲面与必要GI两条回归通过。两次Win64 Mono release构建和实际D3D12 EXE通过原HDR/混合/NRD/环境/VNDF/曲面/Cutout等gate，D3D11 inactive通过；Roslyn与派生SHA通过。NRD单灯启停检查精确发布，多灯检查实际native dispatch和噪声/能量，不把跳过单灯denoiser当成多灯降噪已验证。

## 性能证据的限制

原生最终可见性新增 `HoRTDI Final Visibility` profiling scope；`RunCutoutGpuTiming` 在隔离Editor开启GPU Recorder，逐Editor update渲染720p/1080p，至少32个收到的GPU样本才报告耗时，不足输出-1。批处理实测只收到少量样本，且不同尺寸的原始值不稳定，不能据此给GPU预算或优化百分比。RenderDoc MCP注入的隔离Unity尝试超时，无.rdc文件；已确认所启动的测试进程终止，未重启/终止用户Unity。

因此本步仅证明单灯省去两个resampling dispatch及RELAX阶段、无新增纹理或额外光线；保留既有NRD资源与frontend/normalization/输出copy，尚未证明显存节省。正式GPU成本仍待可复现capture/计时，不用CPU batch时间冒充GPU时间。

入口 `HoRTDIValidation.RunCutoutQuality/RunCutoutQualityCompatibility` 与 `RunCutoutGpuTiming`。日志/CSV前缀 `rtdi-cutout28-*`，留在隔离 `.codex-research/RTInputValidation`。后续优先验证真实多灯的局部边缘/运动与GPU成本；单灯结果不缩减整个DI目标。
