# 专职材质的 RT 消费合同

Ho 管线继续保留现有包 ID、命名空间及 Hidden Shader 名称。它们属于生产者，不要求安装 `jp.lilxyzw.liltoon` 材质包。本次删除 RTGI hit Shader 对该材质包的运行时 HLSL 引用；材质各自的 UI 与运行时代码仍归各 UPM 持有。

## 当前边界

`HoRTMaterialFamilies` 只识别实际实现的材质合同，适配器不修改源材质。原 lilToon 的四个不透明／裁切变体作为迁移期输入保留；这不是安装旧材质包的要求。未知不透明材质仍只遮挡，透明材质不纳入当前 AS；不把未实现的材质模型假装成 Cloth。

| 材质族 | 次级表面数值 | 裁切与纹理 |
| --- | --- | --- |
| Cloth | 底色调整、独立／打包 PBR、法线、发光、Tension | 独立原始 UV 遮罩运算后乘颜色 A |
| Hair | 底色、法线；与其 HoSurface 一致的粗糙度 1、金属度 0、F0 0 | 实体覆盖只读主图 A，不读光学透明度／颜色 A |
| Skin | 底色、光滑度图、F0 强度、法线、RGBA 发光、Tension | 独立 Coverage R × 颜色 A，使用主图 ST |
| Eye（Skin 包） | 底色、光滑度、F0 .04、法线、RGBA 发光 | Coverage R；主图 A 仍是 AO |
| Environment | 保留原金属度／光滑度乘图语义、法线、发光 | 原主图 A × 颜色 A |

这是供 RTGI 采样的表面数据合同，不执行各材质的直接高光、丝光或皮肤光照模型。使用 LOD 0，不宣称复制屏幕导数与 GSAA。Effect、Fur、Commercial Skin 未加入次级表面数值模型。旧材质的视差、溶解、抖动等超出已声明裁切域的情况仍拒绝适配。

各纹理保留其实际采样器和 ST 语义；旧 lilToon 的法线／PBR 链接主图 ST，新专职图使用各自规则。光线 hit group 的空纹理绑定不等于 ShaderLab 默认纹理，因此显式提供白图／平法线身份值。非 HDR 主色按当前颜色空间转换到 canonical 线性 HDR tint；alpha 保持不变。

Tension 使用既有 GeometryData 条目／顶点数据，光线在重心插值后应用材质响应。AS 实例通过自己的 `materialProperties` 运输 Renderer User Value 的两个 16 位值，不改 Renderer 属性块，也不改现有 uint4 实例元数据与 64 字节 surface payload。Renderer 槽位变化触发实例重建；没有生产者时保持原始表面。

## 可重复隔离验证

创建工程的工具不会改写已有目录：

```powershell
python Tools~/MaterialPackages/prepare-validation.py --output D:/Temp/HoMaterialIsolation --shader-root D:/Unity_Fork/HoShader --urp-root D:/Unity_Fork/HoUrp17.3.0 --config-root D:/Unity_Fork/HoUrpConfig17.0.3
```

通过 Unity CLI 先执行 `HoSpecializedMaterialValidation.Setup`，退出后以新的 D3D12 进程执行 `HoSpecializedMaterialValidation.Run`，完整命名空间为 `lilToon.URP.Extensions.Editor.RTGI`。分两次启动用于确保实际 GPU 设备采用 Linear，而不只是修改序列化设置。需要支持 DXR／inline ray tracing 的物理设备。

Run 会检查注册包中确实没有旧材质包，验证五个 Shader 的数值、Cloth 五种 alpha 运算、遮罩独立 UV、Skin/Eye 覆盖与发光、Tension 两个实例槽和缺少生产者回退、原生 closest/occlusion 与 RTDI hit group。覆盖图 Clamp 与主图 Repeat 故意不同，用来捕捉错误复用采样器。每次采样还检查源材质没有被改写。此隔离验证不等于 BREAK_URP 生产场景视觉验收。
