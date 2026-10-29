# PLR 黑色粗糙度 mip 修复与 GPU 回归

Unity 6000.3.15f1 / D3D12，目标工程 `D:/Unity_Project/BREAK_URP`。证据目录为仓库 `Temp~/MmdStages/PlrRegression-20261030/`，原始读回在工程 `Library/HoMmdStages/PLR-validation/孔雀月令/`。

## 已确认的故障

1. **实际反射消失**：镜像 mip 0 有画面，但 mip 1–3 全黑。预过滤第一次缩小时使用隐式 LOD，采样了尚未写入的 mip 1，后续粗糙度层随之全黑。改为显式 LOD 0。没有把地板改成镜面或增大反射参数来掩盖问题。
2. **紫色调试误报**：调试复用了特殊全屏合成的“唯一 surface + composite enabled”开关。不透明 ForwardLit、多 surface、关闭特殊合成时也有有效 source；这些条件不能作为调试缺输入的依据。source 调试改为无接收遮罩的 mip 0；多面时显示最后一个成功渲染的 source。
3. **材质调试全黑**：场景没有为地板配置 OB 身份，SB owner=0，AC 返回空属性。给 27 场景的 46 个接收面补充独立 OB 身份，标签为 None，不赋予角色语义。PLR 也显式声明调试/合成的属性与覆盖率需求。
4. **Renderer 选择不可靠**：URP 的 `rendererFeatures` 是受保护属性，旧代码按同名字段反射失败。现在读取属性并只选择当前相机 Renderer 的实例，不使用其他 Renderer 的全局回退实例。

## 结果与边界

孔雀月令同镜头、原材质，地面 ROI 为 `(x:0..1279,y:400..624)`，8 帧预热后比较开关 PLR 的输出：

| 路径 | RGB 平均绝对差 / 255 | P95 | 最大通道差 >5 的像素占比 |
| --- | ---: | ---: | ---: |
| 修复前 Game | 0.0361 | 0 | 0.119% |
| 修复后 Game | 2.2815 | 9 | 12.492% |
| 修复后 SceneView camera | 6.9158 | 28 | 56.908% |

SceneView 为 CLI 提交的 `CameraType.SceneView` 路径，未操作用户桌面。Game 与 SceneView 各自比较本路径的 on/off，不互相比亮度。

- GPU 恒定 HDR 输入 `(2,.5,.25)`：通过生产代码生成 6 级预过滤 mip，每级最大通道误差实测为 0，容差 .025。此测试使用清黑的未完成 mip 链，能暴露隐式 LOD 误读。
- mip 1–3 实际场景读回恢复非黑内容。多面、特殊全屏合成关闭时，All 调试能显示 source、地板遮罩、材质属性。
- 孔雀月令、花吐电気各主/左/右 3 镜头已用 32 帧预热重新输出，正式图及三机位总览已更新。
- 27 场景结构审计 `errors=[]`；46 接收面身份全部有效，固定 PC Renderer 顺序正确，SSGI/RTDI/DebugTile 保留禁用。没有新建 Pipeline/Renderer 副本，也没有启用 SSGI。
- 其余 25 场景此次完成结构检查，未重新输出所有镜头。`Ready=true` 只证明源提交，不是像素验收。

## 重现

将 `Tools~/MmdStages/Unity/Editor/` 同步到工程制作工具目录。以下捕获入口均使用 `-batchmode -force-d3d12 -projectPath D:/Unity_Project/BREAK_URP`，不加 `-quit` / `-nographics`：

```text
-executeMethod HoMmdPlrValidation.Run -hoStage 孔雀月令 -plrMode off,on,debug
-executeMethod HoMmdPlrValidation.Run -hoStage 孔雀月令 -plrMode scene-off,scene
-executeMethod HoMmdPlrValidation.RunReview -hoStages 孔雀月令,花吐电気
```

`Run` 的材质/Volume 改动仅驻留捕获场景内存，不保存到资产。`strong` 是临时放大反射的诊断模式，不作为正式验收结果。`RunReview` 不重新应用打灯配方。

`HoMmdPlrRepair.RunBatch` 只补缺失接收面身份，保留已有身份和艺术参数，拒绝未保存场景；同步 `HoMmdStageAudit.RunBatch` 检查结构。旧 FeatureRepair 的排序也已修正并保留未启用条目，但此次修复不需要重建共享 Renderer。
