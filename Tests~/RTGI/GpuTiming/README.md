# 步骤011：真实D3D12 GPU timestamp资格与古堡分段

本步解决“没有有效GPU时长”这个缺口，**不解决步骤010的filtered能量失败**。`evidence.json`分别记录gpuCostQualified=true、filteredQualityQualified=false。

## 如何得到数据

测试桥只在专用RTGIValidation/Assets/Plugins，源码在Native/。使用安装Unity6000.3的官方IUnityGraphicsD3D12v7接口，在Unity正在记录的commandList插入Timestamp EndQuery；不创device、不拦descriptor、不接NRI/DLSS。ConfigureEvent区分submission queue访问（频率/初始化）与render command recording。EndQuery位于实际pass标记，ResolveQueryData延后到camera外独立command，避免在native raster render pass里resolve。

接受条件：queue频率有效、begin非零/end有序、唯一slot、结束/resolve已记录、非零目标frame fence已完成；CPU等待不作为duration。空区间可量化为0，已知非空Compute必须正值。资格测试24样本（8空/8短/8长），约0/.006/.041ms，长负载显著大于短/空，实际exit0。

Runtime只增`UNITY_EDITOR`条件测试回调/标记，Player调用被C# Conditional去除，无Native依赖。测试DLL平台限制Editor Windows/x64，所有Standalone禁用；额外Win64 Mono release实跑315请求/全部旧功能gate通过，payload没有HoGIGpuProbe.dll。正常HoRTDINRD不改。

## 实际古堡结果

七项正式进程actual exit0、fresh/marker/SHA与shader/C#诊断0。每普通case 40帧、每marker40个fenced GPU样本；另有资格、Total-only插桩对照和AS/PDF强制重建。RTX4060/D3D12，原18灯/15RT实例/12Cutout/1skin，正常生产原版GI+NRD、.25合成。

| 区间中位数ms | 256 RG | 256 compat | 512 RG | 512 compat |
| --- | --- | --- | --- | --- |
| GI生产Total（含NRD，不含合成） | 1.188 | 1.361 | 3.992 | 3.926 |
| 合成 | .0164 | .0195 | .0236 | .0307 |
| 空间重采样 | .283 | .315 | 1.151 | 1.114 |

Total-only 256 RG中位数1.192ms，全分段1.188ms；在该run对照中总时长接近，未按空标记盲扣开销。p95和原始tick/fence全部保留，不能把median当稳定帧预算。阶段中位数不可直接相加当总数；Total是单独嵌套区间，Composite单列。

强制重建第一帧AS区间2.694ms、PDF .0164ms；稳定场景没有AS build调度，对应标记量化为0，**不是CPU扫描/hash/Bake无成本**。SourceSynchronize CPU、GB/SB/Atmosphere/Forward成本不在GI.Total内，这也不是完整Player presented-frame或1080p预算。

这些是带插桩的Editor graphics-queue区间，host可能有其他Unity/GPU活动，p95有波动。256/512尺寸比较为整个camera/input域改变，不冒称已经实现“全分辨率GB/SB上的半GI重建”。显存/更高尺寸/正常自动camera和质量5%仍待交付。

## 复现与保留失败

```powershell
& Tests~/RTGI/GpuTiming/prepare_probe.ps1
& Tests~/RTGI/GpuTiming/run_validation.ps1
python Tests~/RTGI/GpuTiming/verify_validation.py
```

需要本机VS2022 x64/Windows SDK/Unity PluginAPI；仅自有工程生成dll/lib/obj，不进产品目录/仓库。源码/编译器/DLL hash在native-build.json。run.json绑定8份源hash、真实PID/exit/report/log SHA；结果文件保留480等实际样本。所有进程已结束。

初版empty严格end>begin在第4空样本拒绝（报告rejected-empty-interval-strict.json），没有当通过；分开空与已知工作负载后，非零tick+完成fence的合法量化零区间可接受，非空仍必须正。初版编译缺DXGI类型/UTF8选项也已修，未改官方headers。

测试DLL第一次按默认AnyPlatform导入，配置复核发现Linux/OSX仍enabled；已明确禁全部Standalone，并实际Release排除。Native既有不改，产品不加载probe；额外Release回归单列release-regression.json。保留既有GB fallback/resolve与退出GC/非GI构建警告，不称完整生命周期通过。

下一步按已测主成本推进分辨率/显存与真实应用条件，不能用采样削减掩盖010能量失败。该质量路线sigma4/6仍拒绝，更宽色盒/增益已搁置。
