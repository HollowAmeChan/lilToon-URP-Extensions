# 主体轮廓场 GPU 回归

在隔离的 Unity 6000.3 / URP 17.3 工程中引用 lilToon 和本包，将此目录的
`OutlineFieldValidation.cs`、`OutlineValidationUV.shader` 复制到 `Assets/Editor`。
保留图形设备，运行：

```powershell
Unity.exe -batchmode -projectPath <isolated-project> -executeMethod OutlineFieldValidation.Run -logFile <log-path>
```

成功时输出 `[OutlineFieldValidation] PASS` 并返回 0，断言失败返回 1。
脚本直接执行生产用的 source、blur、composite shader，不修改场景或管线资产。

覆盖透视/正交、0°/25° 俯仰、15° 相机滚转、关闭/两个方向的渐隐、低覆盖率、空场，
以及 8/40/80/200 像素半径下的重叠轮廓与两轮真实模糊。通过独立的相机空间公式检查
渐隐位置，注入交错深度和无关 G/B 数据，验证它们无法扰动渐隐。检查角色参考距离的
选择及无参考点回退；方向着色另用非正方形目标与微小梯度验证。

高度渐隐使用每相机统一的朝向相机的平面：CPU 按投影和角色参考距离算出 UV 到世界 Y
的线性系数，GPU 只计算点积和渐隐曲线。距离取画面内有效 GD 角色参考点的平均相机深度，
没有参考点时使用 5 个世界单位；“渐隐参考距离（共用）”设为正数可手动指定，0 为自动。
左右眼分别计算系数。它是视觉近似，多角色前后相距很远时
仍共用一张渐隐平面；不追求逐物体贴地。不会模糊深度/位置，也不使用轮廓方向求高度。

轮廓源和两轮模糊只保存单通道覆盖率，优先 R32F（4 字节/像素），不支持线性过滤时回退
R16F 或原支持格式。方向场只服务彩色流光和方向调试；纯色/雾气的高度渐隐不计算方向。
