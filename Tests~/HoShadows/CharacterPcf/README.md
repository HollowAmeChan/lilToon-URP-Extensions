# Character Shadow 固定 PCF

[HoCharacterShadowFixedPcf.hlsl](../../../Runtime/Shadows/Resources/HoCharacterShadowFixedPcf.hlsl) 是管线侧的局部投影过滤。它只读现有 Character Shadow atlas，输出接收结果供后续合成。

## 接入

先包含 URP `Core.hlsl`，再包含此文件。若同一编译单元已有 Character Shadow atlas 常量声明，应复用同一份声明，避免重复定义。

- `HoCSFixedEvaluate(positionWS, positionDx, positionDy, owner, soft, radiusTexels)` 接受世界位置和同一接收面的两个世界空间差分。使用当前图素的完整 owner 低 16 位，高字节是组，低字节是部件。没有可信同面差分时传零，避免从不相干的 owner 或深度边界计算斜率。
- 接收端沿用组到切片映射、部件掩码、局部盒和切片有效位。世界位置投影到切片局部 UV 和归一化深度，再用 tile rect 转成 atlas UV。有预投影数据的调用端可直接用 `HoCSFixedFilter(uv, lo, hi, receiverDepth, gradient, soft, radiusTexels)`。其中 `lo/hi` 是 tile 内最外侧 texel 的中心，`gradient.xy` 是 atlas UV 上的深度斜率，`gradient.z` 是归一化深度的接收平面容差。这个低层函数只过滤，调用端负责匹配和有效性。
- `localVisibility` 是过滤可见度按原有灯光阴影强度折算后的值。 `blend` 是独立的局部盒边缘混合权重。 `valid` 是匹配有效位，失败返回 `(1, 0, 0)`。与原生阴影、全局投影和材质权重的混合留给调用方。

硬影读一个点，软影读固定 3×3 个点，使用 `[1, 2, 1]² / 16` 权重，半径约束到 1–2 atlas texels。每个采样点吸附并限制在自己的 tile 内。逐 tap 的接收平面校正保留原有残差保护。固定 PCF 移除了 blocker search、距离相关半影和旧的可变半径。软影仍可能随采样位置跨 texel 产生阶跃。

## 复跑

在装有本地 Extensions 和 URP 17.3 的独立 Linear Unity 6000.3 工程中，将 `HoCSFixedPcfProbe.shader` 复制到 `Assets`，将 `HoCSFixedPcfProbe.cs` 复制到 `Assets/Editor`。探针直接包含包内 HLSL。在本测试目录执行下列命令，将 `$privateProject` 替换为独立工程路径。将 `-force-d3d11` 换成 `-force-d3d12` 并另用新目录，可重复 D3D12。

```powershell
$privateProject = 'D:/Temp/HoShadowsTestProject'
$output = 'D:/Temp/HoCharacterPcf-D3D11'
New-Item -ItemType Directory -Path $output
$env:HO_CHARACTER_SHADOW_P1B_OUT = $output
unity run $privateProject --no-accelerator --log-file (Join-Path $output 'unity.log') -- -force-d3d11 -executeMethod HoCSFixedPcfProbe.Run
py verify_dxbc_budget.py $output
```

探针用离屏 GPU 读回验证清晰边界、软影过渡、邻接 tile 隔离、局部边缘权重、组和部件拒绝、斜面校正与远 blocker。 `pcf-validation.json` 保留断言和数据，EXR 的 RGB 分别记录 `localVisibility / blend / valid`。Windows 校验脚本用系统 `d3dcompiler_47.dll` 反汇编 DXBC。硬／软／运行时切换应分别声明 1／9／9 条采样指令，三个变体均为 1 个 sampler、零循环且无编译诊断。

该夹具验证局部过滤与静态采样预算。真实相机的深度／身份重建、RenderGraph 调度和角色画质需单独验收。
