# 去哪里调，以及怎样调

以当前手工版本为准。以下是调整方向，不是所有场景都必须满足的清单。旧 H2CU 制作参数 JSON 是初次制作配方，不自动跟踪 Inspector 修改；不要为渲图重新 Apply 配方。

| 想改变的观感 | 首先调整 | 补充判断 |
| --- | --- | --- |
| 主体暗、远处细节不清 | Light 位置/朝向、颜色、强度、range、spot angle，低位和远景补光 | 曝光会一起推高亮部，先让灯照到需要的几何 |
| 二次元强对比、斑驳阴影 | Ho lilToon 阴影/受光参数、Light shadow strength、ShadowCaster、窗框/树叶/阴影遮片 | 侧视确认投影落在真实表面，不每次重写所有 caster |
| 接触太平或 AO 太脏 | 材质 _UseRealtimeAO、_AOContrast、_AODarkStrength，以及 GTAO Volume | 逐类材质调，太重会压黑地板和角落 |
| 局部体积光柱 | Point/Spot 的 HoVolumeLightSource、Atmosphere Region 密度/颜色、Volume Light Volume | 灯提供表面亮度，体积提供空气可见性，一起摆 |
| 天空/窗外气氛 | 环境 skybox、天空球/HDR 材质、室外灯与天空曝光 | 天空壳按需从 RTGI 射线排除 |
| 地板/水面倒影 | HoPlanarReflectionSurface 平面位置/法线，材质 PLR 开关/强度、smoothness、reflectance | 地板和水面可能不同高度，先定平面再调粗糙度 |
| 间接光/环境响应 | Ho-RTGI Volume、RELAX、天空/灯光、材质金属度和光滑度 | 禁用 SSGI，GI 是否开启按效果需求决定 |
| 全图明暗、色调、辉光 | Color Adjustments/Bloom，ImageProcess 渐变/Glow/LensFlare/颗粒等图层 | 由弱到强调，观察亮部是否变大片死白 |
| 景深与主体焦点 | ScreenProcess Depth of Field，相机位置/FOV/焦点参数 | 不同镜头可有不同焦距，留意运动遮挡边缘 |
| 分区/渐变/中心调色 | IP/SP 对应层的颜色、遮罩、混合模式、强度 | 允许艺术化处理，主体基础受光尽量由世界灯光承担 |
| Logo 与电影边框 | ImageProcess LogoOverlay、CinematicBars | Hollow 默认 X=.5、Y=.1、大小=.3，黑边白 Logo，白边黑 Logo |

共享 PC_Renderer 负责 Feature 存在和底层执行。日常场景差异优先用 Volume、灯光、材质；只有任务需要底层改动时才改共享 Feature，不顺手开启用户关掉的项。

## 体积与雾

重要 Point/Spot 可加 HoVolumeLightSource HD。96–256 steps、Spot 深度 512–1024、Point 每面 256–512 可作起点，再按质量/性能调。独立 LightDepth 需要几何投影；source radius/soft edge 缓和硬边。

雾蒙蒙时一起观察太阳光束、区域密度、height fog、SkyTyndall、Glow。先降低抢眼的全局散射，再补局部灯。乐园·墟实的废墟窗雾可以保留；花吐电気应保持明亮干净。

## PLR 设置与按需排错

接收 Renderer 加 HoPlanarReflectionSurface，指定反射平面 Transform，开 Scene View 预览。材质开 _UseReflection 和 _UsePlanarReflection，调 strength/smoothness/reflectance。不透明 Ho lilToon 直接吃每物件 source，通常不用特殊全屏合成。

GB/OB/SB/AC 是特殊合成与材质调试输入；调试需要接收面 OB 身份，场景对象 tags=None。没有故障不每次检查。有 source 却没反射时再看材质开关和 mip，Ready=true 不能代替像素。

HoMmdPlrValidation 的 on/off/debug 与 HoMmdPlrGpuCheck 用于管线排错，不是日常调灯换滤镜的必经流程。

## 风格参考，不自动套用

花吐电気适合明媚窗外、浅空气和局部树影；圣诞万花镜可用低位小点光读清树、礼物、地板；百鸟溯归需要远景书墙宽补光；乐园·墟实可保留较重废墟窗雾。只用这些历史例子帮助判断，不要求用户手工版本复现它们。
