# Capsule AO 验证记录

状态：历史首版验证，HoAO 合并路径已被取代；现行验证见 [ReceiverOverlay](ReceiverOverlay/README.md)。

日期：2026-11-25。使用独立空项目，不操作用户已打开的 Unity Editor。运行时源码和测试位于本仓库，未修改 lilToon、Ho URP 或项目业务资源。

## 环境与复现

- Unity 6000.3.15f1，Windows，当前 Ho URP 17.3 / Core 17.3 fork。
- 扩展包：`40f92880`（包含 `bd170148` 的主体实现）；最终测试使用固定包快照，排除并行 Rim 的未提交改动，收口源码与提交 blob 逐项核对一致。
- lilToon 消费端基线：`d134eed394860018080818ccf5dda9da087fa2be`，只读导出 `Assets/lilToon`，不纳入该仓库并行 Rim 修改。
- 项目：`D:/Temp/HoCapsuleAOValidation-20261125`。最终原始 XML 已归档到 [Results](Results)，Editor log 留在该项目目录；汇总及原始快照文件哈希见 [results.json](results.json)。

| 最终版本 | 全仓 EditMode | 其中 Capsule / GTAO | 整链执行组合 |
| --- | --- | --- | --- |
| D3D11 | 165/165 通过，0 跳过 | 31/31 | RG 正交、RG 透视、RG 4× MSAA、Compatibility 正交 |
| D3D12 | 165/165 通过，0 跳过 | 31/31 | 同上 |

最终报告包含用户反馈后重做的双头渐隐 Cross 核。实际设备为 RTX 4060；不同 API 的两组预览 PNG 分别逐字节一致。

将扩展包和配套 fork 配入独立项目 manifest，并在 `testables` 添加 `jp.lilxyzw.liltoon.urp.extensions`。Compatibility 测试需先在独立项目执行以下方法，添加 `URP_COMPATIBILITY_MODE`；正常用户项目不必为使用 RG 添加该定义。

```powershell
unity run D:\Temp\HoCapsuleAOValidation-20261125 -- -executeMethod lilToon.URP.Extensions.Tests.HoCapsuleAOIntegrationTests.EnableCompatibility -force-d3d11
```

全仓 EditMode 回归包含 Capsule/GTAO 用例与已有 RT／ScreenProcess 等回归；指定输出路径避免不同 API 覆盖证据：

```powershell
$project='D:\Temp\HoCapsuleAOValidation-20261125'
$env:HO_CAPSULE_AO_PREVIEW_DIR="$project\Previews"
unity test $project --mode EditMode --output "$project\gradient-d3d11-tests.xml" --timeout 900 --format json -- -force-d3d11 -logFile "$project\gradient-d3d11-editor.log"
unity test $project --mode EditMode --output "$project\gradient-d3d12-tests.xml" --timeout 900 --format json -- -force-d3d12 -logFile "$project\gradient-d3d12-editor.log"
```

只跑本项时可在 Test Runner 中选择以下类，或使用 CLI 的 `--filter` 指定其中一个完整类名。

## 用例覆盖

| 测试 | 验证内容 |
| --- | --- |
| `HoAOPublicationTests` | 默认行为、白色可写输出、奇数尺寸／MSAA 输入转单采样、无需 GB／Color／Motion、相机／尺寸借用合同和重置 |
| `HoCapsuleAOGpuTests` | 实际运行 Shader 的 Capsule/Cross；`.7/.6 → Min=.6、Multiply=.42`；重复接收覆盖不反复变暗、前景拒绝、双头渐隐十字、抬脚、Transform 和剪切缩放 |
| `HoCapsuleAOVolumeTests` | 编辑器真实布局／重置、容器覆盖、旧五项颜色合成 ABI 不变；前置项不加入颜色乘法 |
| `HoCapsuleAOIntegrationTests` | 实际 GB → HoGTAO → 角色特化 → 公共全局／typed handle 消费；RG 正交、RG 透视、RG 4× MSAA、Compatibility 正交 |
| 同一整链用例内 | 无发布者／停用恢复、多相机、静态合批、移动后旧遮蔽清除、前景遮挡、正常 GTAO 的 Min/Multiply、Volume 不回写 Feature、实际 lilToon AO 开关和原材质 JSON 不变 |

Compatibility 整链用例验证白色发布路径；正常 GTAO 计算仅在 RG 组合中验证。MSAA 颜色读回先显式 resolve，避免直接读多采样 RT 的无定义数值。原型失败报告保留在独立项目中，包括测试容器 override 遗漏、全局设置初始化顺序、混合基线未关闭 Capsule 和直接 MSAA 读回，均未通过削弱最终断言规避。

## 画面与限制

[Reference](Reference) 中 PNG 是实际整链生成的 AO 可见度，白色为无遮挡；它们不是成品角色美术渲染。Capsule 使用最近球投影近似，Cross 使用两道长度方向双头渐隐、横向羽化的扁平软带，没有 ZMD Log-SH 等价性结论。

本轮不声称通过其它图形 API、XR、Overlay／非全 viewport／动态分辨率、Player 实机构建、位移／复杂 alpha clip／蒙皮接收面和完整角色美术场景。前三类相机配置在运行时明确跳过。没有 GPU 毫秒或大规模角色预算的性能承诺；低成本模式已从资源与实际发布路径验证跳过 GTAO 计算链。

对真实 SSGI／Debug 的端到端画面没有独立验收；同一 AO handle 的后续消费者读到附加结果已由独立 probe 验证，不能将此写成所有消费者场景已逐一覆盖。
