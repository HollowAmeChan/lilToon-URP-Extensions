# 步骤009：GI骨骼快照实际属性与反弹

本步没有修改生产Runtime。沿共享HoRTSkinnedMesh与DI既有BakeMesh(true)约定，补足GI材质vertex fetch与完整照明的实际证据。

`results/run.json`：六个实际Editor进程全部exit0、marker/fresh/SHA成功、shader/C#诊断0。Linear/Gamma的材质命中各140checks；两颜色空间×RG/compat完整照明各187请求/25checks。

- 独立线性蒙皮矩阵推导的ray distance、着色/几何法线、UV0主色和UV1 emission，与真实GI hit一致。
- 单骨旋转/平移、双骨混合权重、BlendShape位置/法线、非均匀与负行列式镜像、切线法线贴图均覆盖。UV1刻意与UV0不同，避免把两个通道混淆后仍跑绿。
- 离屏skinned emission的raw约(1,0,.001)，filtered保持；骨骼或BlendShape把面移到接收面后方时，首帧raw/filtered归零；恢复首帧重亮并清旧历史。
- Skinned opaque/Cutout roof真实遮挡次级天空；骨骼位移/alpha孔洞后恢复能量。非CPU-readable source可用，稳定姿态复用pose/BLAS/history；无效骨骼移除旧snapshot，恢复后可用。

`player-run.json`：两个Linear D3D12 Win64 Mono release build与实际EXE全部exit0，各315次请求、310有效应用帧、30checks。非readable skinned红蓝发光面与Cutout使用序列化资产，骨骼/BlendShape/无效骨骼变化首帧清旧光，恢复后重亮；既有alpha/非空材质排除/Volume/denoise/相机/合成继续通过。真实Update每次只一次请求，帧号/时间递增，不冒称正常enabled-camera自动呈现。

## 复现与来源

```powershell
& Tests~/RTGI/Cutout/run_validation.ps1 -Skinned
# 恢复专用工程manifest为实际工作区后构建Player
& Tests~/RTGI/Dynamic/run_validation.ps1 -Skinned
python Tests~/RTGI/Skinned/verify_validation.py
```

Editor使用独立`.codex-research/RTGIValidationSkinnedPackage`，base fa64cc6+128份覆盖；不会覆盖步骤008的旧快照。Player使用实际工作区，base/head和builder/probe SHA、日志/报告/完整payload均记录。当前所有测试进程结束。

复审接纳为已有生产路径的支持域确认。没有发现需要修改共享BakeMesh/Shader/NRD的缺陷，也没有为了通过而改参数或门槛。骨骼动画当前每次姿态变化仍保守reset、CPU Bake成本与真实GPU毫秒未测；本步不是完整角色漫游/目标场景/自动相机或生命周期验收。Player既有GB fallback/resolve与退出GC警告、构建非GI警告保留在日志/证据。

真实场景/GPU下一步：只读定位到DI独立工程已有血月古堡依赖副本，尚未导入GI或跑其画面。RenderDoc技能已读，本机CLI已定位，但当前命令/工具只提供事件/资源统计，不据此声称有GPU duration；DI此前隐藏宿主/计时零样本/无capture经验继续保留，不能同条件盲重试。
