# 接收面独立叠加验收

日期：2026-11-25。取代首版 HoAO 合并路径。现行使用说明见 [Capsule AO / Cross](../../../Documentation~/角色特化/CharacterSpecialization_CapsuleAO.md)。

测试直接读取最终相机颜色，使用普通 URP Unlit 和关闭 AO 的 lilToon 材质；同时由独立 probe 读取原 HoAO，检查开启／关闭接收面叠加时它保持不变。不是仅检查一张中间 AO 能否画出来。

## 关键回归

- HoGTAO 和 GB 都禁用时，Capsule/Cross 仍将地板最终颜色乘以预期的 0.6；材质 AO 来源关闭、材质 AO 压暗关闭时仍可见，材质 JSON 不变。
- 白色 HoAO 与正常计算 GTAO 的纹理内容在接收面叠加前后相同；正常 GTAO 测试只在 RG 进行，Compatibility 仅验证白色发布。
- Cross 脚骨中心在地面、胶囊部分穿地时仍可见；整个胶囊位于接收面背后或抬脚超过范围后消失。倾斜地板仍可接收。
- 两个重叠接收面不反复乘暗；普通未登记前景不受影响；接收／发射组件停用恢复、静态合批、脚部移动与第二相机均有最终颜色对照。
- GPU 单元回归检查双头渐变、有限范围、颜色乘法与 alpha 保持；Volume 真实 Inspector 布局／重置和原五项颜色合成合同继续验证。

## 环境与结果

Unity 6000.3.15f1，Ho URP 17.3 fork，RTX 4060，独立项目 `D:/Temp/HoCapsuleAOValidation-20261125`。使用扩展包固定源码快照，排除并行 Rim 改动；lilToon 消费端沿用只读基线 `d134eed394860018080818ccf5dda9da087fa2be`。源码提交与原始报告哈希见 [results.json](results.json)。

源码收口提交为 `72f71a48`。最终 D3D11、D3D12 各 **164/164** 全仓 EditMode 测试通过，0 跳过；其中 Capsule/GTAO 各 30 项。覆盖 RG 正交、RG 透视、RG 4× MSAA、Compatibility 正交；逐 API 结果见 `results.json` 和 [Results](Results) 中原始 XML。[Reference](Reference) 是实际最终相机颜色预览，白色为普通 Unlit 地板，已关闭 HoGTAO/GB；不再是首版公共 AO 的可见度截图。

```powershell
$project='D:\Temp\HoCapsuleAOValidation-20261125'
$env:HO_CAPSULE_AO_OVERLAY_PREVIEW_DIR="$project\ReceiverOverlayPreviews"
unity test $project --mode EditMode --output "$project\receiver-overlay-final-d3d11.xml" --timeout 600 --format json -- -force-d3d11 -logFile "$project\receiver-overlay-final-d3d11.log"
unity test $project --mode EditMode --output "$project\receiver-overlay-final-d3d12.xml" --timeout 600 --format json -- -force-d3d12 -logFile "$project\receiver-overlay-final-d3d12.log"
```

Compatibility 测试需在独立项目启用 `URP_COMPATIBILITY_MODE`，具体命令见 [首版环境搭建](../README.md)。测试基线显式请求相机深度，使 AO 开／关的 MSAA 对照使用同样的深度配置；实际效果启用时会自行向 URP 请求深度。颜色读回对多采样目标先 resolve。

联测修复了白色发布 Compatibility Pass 未向 URP 声明自身目标的问题，避免后续不透明颜色画到错误附件。接收面 Compatibility 直接消费 URP 已发布的 GPU 深度绑定，不通过 CPU 查询尚未执行的 Shader 全局状态判断纹理。

本轮没有验收 Player、其它图形 API、XR、Overlay、非全 viewport、动态分辨率、位移／复杂裁剪／蒙皮接收面或完整角色美术与批量性能；对应运行时跳过项与限制见现行使用说明。原型失败报告和 Editor log 留在独立项目，最终原始 XML 随本目录归档。
