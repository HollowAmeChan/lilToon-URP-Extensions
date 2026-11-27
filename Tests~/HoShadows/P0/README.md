# Ho Shadows P0 基线

本页记录 2026-11-27 在 `BREAK_URP` 的只读盘点与旧 HoShadowCast 的隔离 GPU 对照。阶段目标和生产切换门槛见 [执行计划](../../../Documentation~/计划/Ho-Shadows-Execution-Plan.md)。

## 生产资产快照

| 仓库 | 取证时提交 | 当时工作树 |
| --- | --- | --- |
| `lilToon-URP-Extensions` | `49a1f795` | 执行计划已提交，运行时保持 `f6fd8f0a` 的源码 |
| `HoUrp17.3.0` | `bcc15df9` | 没有运行时改动 |
| `HoShader` | `d0898c7b` | 编译报告已提交，另有 Hair 本地编辑 |
| `BREAK_URP` | `8588b2bd` | 项目已有大量本地改动。PC Renderer、默认 Volume、依赖 manifest 和 lock 以取证时磁盘快照为准 |

- Unity `6000.3.15f1`。生产 PC 配置为 RenderGraph、Forward+、逐像素附加灯。原生主光和附加灯阴影都开着，各自 atlas 配置为 4096。原生主光为四级联和 90 距离。
- 旧 HoShadowCast 自动收集可见灯，磁盘配置是 8192 主 atlas、4096 第二方向灯 atlas、PCSS High 和 Medium 灯容量。没有找到已保存的 `HoShadowCastLight` 实例。舞台生成与验收仍直接查找旧 Feature，并有脚本写入容量、PCSS 和材质接收范围。
- 角色自阴影与 RTShadow 都存在已开启的配置。当前默认 Volume 含角色局部图集和 RT 主光 Raw 的启用覆盖。羽啾的局部 Volume 覆盖角色图集配置，并关闭 PCSS 和 RT。接入时应按相机实际生效的 Volume 复核。
- 四个主要材质家族的序列化接收条件中，实际开启阴影响应且选择 Main 的磁盘计数为 Cloth 69、Environment 10、Skin 14、Eye 10，共 103。Full 为 0。另有 Environment 75 份显式 Full 和 1 份继承 Full 均关闭 `_UseShadow`。这是资产清点，场景是否绘制相应材质需由实际重放确定。

建议重放圣诞万花镜的多灯镜头、羽啾的角色自阴影镜头和 WTD 的 RT opt-in 镜头。圣诞万花镜场景保存了 239 盏启用的 Soft Spot 和 13 盏启用的 Soft Point，实际可见和 atlas 分配数量从运行时报告读取。

## 隔离 A/B

使用真实 `Ho/Environment/URP/Lit` 和真实旧 HoShadowCast Renderer Feature。每对在相同灯和材质设置下只切换 Feature 的启用值，顺序为关闭、开启、再次关闭。原生附加灯阴影始终开启，合成场景未挂载 Character／RT。目标为 128×128 线性浮点颜色，阈值为 RGB 最大分量差 `>0.002`。

| 场景 | Main 改变像素 | Full 改变像素 |
| --- | ---: | ---: |
| 原生点光与聚光 | 0 | 8714 |
| 未声明参与 Main 的第二方向灯 | 0 | 2156 |
| 声明参与 Main 的第二方向灯 | 2156 | 2156 |

D3D11 和 D3D12 对应的 18 对线性 EXR 与 18 对 PNG 各自哈希相同。全部关闭重放都回到同一画面。诊断确认实际 RenderGraph 图集开关和原生附加灯真实开启。该证据验证了旧 Ho 图集在普通 Full 和显式 Main 角色中的影响，属于合成夹具。正式取证在 RTX 4060 上完成，入口参数通用化后的夹具已用 D3D11 重放通过。

## 重放方式

在独立线性 Unity 工程安装当前 HoURP、lilToon-URP-Extensions、Environment_URP 和其 Inspector 依赖。复制 [HoShadowCastP0Probe.cs](HoShadowCastP0Probe.cs) 到该工程的 `Assets/Editor`。分别配置实际物理工程目录与新的输出目录，再启动 Unity CLI。

```powershell
$env:HO_SHADOWS_P0_PROJECT = 'D:/Temp/HoShadowsBaselineProject'
$env:HO_SHADOWS_P0_OUTPUT = 'D:/Temp/HoShadowsBaselineD3D11'
New-Item -ItemType Directory -Path $env:HO_SHADOWS_P0_OUTPUT -Force | Out-Null
unity run $env:HO_SHADOWS_P0_PROJECT --editor-version 6000.3.15f1 --timeout 240 --no-tail `
  --log-file "$env:HO_SHADOWS_P0_OUTPUT/unity.log" -- -force-d3d11 `
  --burst-disable-compilation -executeMethod HoShadowCastP0Probe.Run
```

D3D12 使用另一输出目录并换用 `-force-d3d12`。`render-ab.json` 保留逐对统计、灯和图集信息，以及对应 EXR／PNG 名称。当前生产资产不参与该夹具。
