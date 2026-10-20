# RTGI步骤013：实际primary网格合同

最终两个实际Editor进程：RG PID37868、compat PID50528，均exit0、编译/Shader诊断0、无fence错误。每条12个受控RenderRequest，读取18,190个真实GB/SB receiver、5,961个前次请求设备深度和1,443个前景receiver；没有启用GI生产链、AS、计时DLL或实际NRD过滤器。不是正常enabled-camera或真实应用帧motion验收。

验证`Runtime/RTGI/HoRTGIGrid.cs`与`Shaders/HoRTGIGrid.hlsl`：偶数尺寸固定2×2代表点`fullPixel=2g+1`；full、奇数尺寸或单行/列为scale1。C#按逻辑projection的row3补偿row0/row1，让NRD规则grid中心与实际代表点相符；HLSL统一primaryUV与gridUV转换。输入尺寸/scale不同则历史不兼容。helper尚未接入生产GI，没有半GI产品开关或重建交付。

真实透视/正交、偏心投影、相机平移/旋转、resize/full-half/odd/single row-column共12case。CPU用独立Camera.ViewportPointToRay与两个变换quad的平面/边界求交；GPU从真实GB/SB加载位置、法线及device depth。当前补偿投影反投影、实际上次depth的物理/NRD反投影、当前world投前次grid均评分。

| 最大误差（两路径相同） | 实测 | 原门槛 |
| --- | ---: | ---: |
| world / NRD world | .001916 | .002 |
| geometric normal向量距离 | .006913 | .012 |
| view depth | .001686 | .002 |
| primary UV | 8.43e-8 | 1e-6 |
| NRD grid UV | 3.21e-7 | 2e-5 |
| previous物理 / NRD world | 7.15e-7 / 7.54e-7 | .002 |
| current→previous grid UV | 1.48e-5 | 2e-5 |

world最大值来自8×1极端宽视野的full回退；半GI各case误差低于1e-4。负对照刻意漏掉NRD投影补偿，半GI最大world误差每case .052～.108；漏掉primary pixel映射则为3.75～4.09，均被原.002门槛明确检出。full时两负对照与原full等价，确认零偏移不改变full合同。

首轮和诊断轮各actual exit1，仅最后full移动case一个像素最大world误差1.99435。精确CPU中心位于foreground边缘外local x约−.5000122，距投影边缘约.000192像素，GPU raster仍覆盖foreground；数值见`edge-analysis.json`及保留DIAG日志。最终夹具只在投影轮廓1/256像素带内，允许两种独立ray/plane候选；仍按原world/depth/normal门槛比较，不跳过像素、不按输出反造位置或扩大范围。每条仅两个像素落入该带，其中另一个本来就无错误；其余18,188个像素维持唯一解析候选。该约定针对raster覆盖，不证明薄边GI重建质量。

`verify_validation.py`复核退出/log/report SHA、每case范围/门槛/负对照/历史拒绝、当前源码与exact source-at-run（仅允许checkout换行等价），并验证两份失败及所有失败源逐字节归档。局部.gitattributes保留JSON和源归档SHA。执行`run_validation.ps1`会写新的run/results；现有证据应先归档，不能覆盖保留失败。

事后review：接纳可复用坐标合同；没有生产质量/性能收益宣称。下一步把同helper接GI私有RAB、Generate、history/Temporal和NRD，再做full输出边缘重建/原能量及细节门槛。010能量失败与012HD失败继续，不能把本夹具的数学准确当NRD过滤或半GI渲染验收。
