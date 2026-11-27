# Skin／Eye 受管角色 Capture

夹具用生产的接收表面登记、原生阴影解析和绘制绑定，调用当前 Skin、Eye 的 `HO_CHARACTER_CAPTURE` Pass。它在独立 Render Graph 中重放 Face → Eye，将色彩和光学 MRT 写到可读回目标。矩阵、标签、深度和 stencil 由生产写入器处理，颜色来自实际材质。每种材质开启 `_UseShadow` 与局部 `_HO_SHADOWS_SCREEN_PREVIEW`。

第一组使用真实方向主光。第二组关闭主方向光，由一盏真实 Spot 在 Full 模式投影。两组分别检查只含 Face 的取样点、完全由 Eye 遮盖的取样点、亮处对照、光学 MRT 和撤掉投影后的颜色。Spot 组同时切到 Main 模式检查接收范围。全量角色合成、Volume 及 AC 语义选择独立于本夹具。

## 复跑

1. 将这三个 C# 文件复制到独立 Unity 6000.3.15f1 工程的 `Assets/Editor`。安装 Ho URP、Ho URP Config、当前 Extensions 和 Skin URP。使用支持 D3D12 的图形设备。
2. 将 `HO_CAPTURE_MANAGED_OUTPUT` 设为一个现存的空结果目录。以 batch mode 执行 `HoCaptureManagedProbe.Run`，使用 `-force-d3d12`。
3. 读取结果目录的 `capture-managed-rgb.json`、`capture-managed-dxbc.json` 和四个 DXBC 文件。运行 `python verify_capture_dxbc.py <结果目录> <Skin_URP 目录>`，生成 `capture-managed-disassembly.json` 和汇编文本。

编译检查覆盖 Skin 和透明 Eye 各自的主光 PCF High 与仅附加灯变体。它验证专用屏幕纹理绑定，并核对片元字节码中实际执行的点读取和比较指令。RGB 回放使用方向硬阴影与 Spot 硬阴影。

## 实测

Unity 6000.3.15f1，D3D12，128 × 96 相机与 64 × 48 Capture。表中记录线性 RGB 的 R 分量。

| 场景 | Face 受遮挡点 | Eye 受遮挡点 | 变化像素 |
| --- | ---: | ---: | ---: |
| 方向主光 | 0.78408 → 0.28848 | 0.64320 → 0.23664 | 111 |
| Spot Full，主方向光关闭 | 0.18271 → 0.06758 | 0.17680 → 0.06539 | 218 |

亮处、光学 MRT、丢失 atlas 后的颜色、关闭 Capture inputs 后的主光颜色和 Spot 切回 Main 后的颜色，最大读回误差均为 0。

四份 D3D 片元字节码的阴影比较指令均为 0。Skin 与 Eye 的主光变体各有 4 条专用纹理点读取，Full 变体各有 7 条。
