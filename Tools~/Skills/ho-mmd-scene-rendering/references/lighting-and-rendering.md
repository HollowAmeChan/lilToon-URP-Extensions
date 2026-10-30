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

## 逐盏灯具与层级标记

先辨认真正的灯芯/灯纸/发光晶体，别直接在整个材质 Renderer 的 bounds 中心放一个大 Point。PMX 常把整排灯合为一个网格：可用 HoMmdPracticalLighting.Inventory 拆出焊接后的连通几何，再核对哪些部件属于同一盏灯。例如炫光的同一个 LED 有上下两片薄面，要合为一个灯位；百鸟和月昔梅大量纸灯则确实是独立灯具。

补灯计划保存在场景 Rendering/灯具补光.json，显式列出位置、颜色、强度、范围、来源、定向下照、投影与局部体积。Apply 只更新 11 实景灯具 下的灯、指定发光材质和列出的旧灯，不重新应用全局打灯/滤镜配方。实际摆好后可以直接在 Inspector 修改；只渲图不会恢复 JSON 数值。

小灯用短范围低强度实光，悬灯可配向下 Spot 在地面形成光池。少量重点灯承担 HD 体积/遮挡，先看光束中段是否在观察空间内，避免把预算全部给相机旁边、画外的灯。开 HD 独立遮挡还需要 Light 的 shadows 开启，源头要在开口外，避免灯壳把整个光束挡住。

点光在双面纸灯内部会以极近距离把灯纸烤白。当前补灯工具将这类发光 Renderer 放在 rendering layer 128；旧的主光/补光保留对它的照明，新实景灯使用 layer 1，照亮周围几何。灯体的 Emission 提供可见发光，金属框架保留反光。调整时留意这个分层，不要重新让高强度点光照回自己的灯纸。

vHierarchy 使用现有调色板：模型绿、灯光金、相机蓝、环境青、反射与场景根紫。先保存新物体以获得稳定 GlobalObjectID，再调用 vHierarchy 原生数据 API，保留用户已有颜色。无需修改插件源码或开启 Team Mode。

## 风格参考，不自动套用

花吐电気适合明媚窗外、浅空气和局部树影；圣诞万花镜可用低位小点光读清树、礼物、地板；百鸟溯归需要远景书墙宽补光；乐园·墟实可保留较重废墟窗雾。只用这些历史例子帮助判断，不要求用户手工版本复现它们。
