# 新 PMX 从零搭建

先看作者预览与说明：主体、树/花枝、灯、地面追加可能需要组合；改色版、单独道具、重复合并版按需选择。不要把包内所有 PMX 叠在一起。

## 文件和导入

推荐 `Assets/mmd场景测试/<作者>/<场景>/`，下设 Source、Materials、Rendering、Renders，场景文件放在根目录。作者/名称不依赖 H2CU 清单。

复制完整原包到 Source，保留 PMX 到贴图的相对目录，不扁平化同名纹理，不复制其他 Unity 工程的 .meta。E 盘原包不需要改写。普通制作不要求反复比较全库哈希。

PMX Inspector 使用 Ho PMX importer：本工程常用缩放 0.08，静态舞台通常关闭自动描边，按需读取 Ray 材质。检查缺图与 alpha 分类，再抽出可编辑材质到 Materials。程序入口为 HoPmxMaterialExtraction.Extract。外置材质承担后续编辑，PMX 不需要回写。

PMX/MME 预览中的灯光、粒子、镜头和天空不会自动变成完整 Unity 效果，按用途重建。导入信息只在出现缺失/转换疑问时查。

## 通用创建入口

工程打开、队列脚本编译完成后：

```powershell
python Tools~/MmdStages/stage-cli.py create --folder "Assets/mmd场景测试/作者/新场景" --pmx "Assets/mmd场景测试/作者/新场景/Source/原始包/主体.pmx"
```

重复 --pmx 可组合部件。可选 --logo "Assets/.../Hollow Logo.png" 会复制 Logo 到 Rendering，创建黑边/白 Logo，Y=.1、大小=.3。先复制完整包到 Source；创建入口不推断外部纹理依赖，也不覆盖已有 .unity。

Starter 创建 PMX 实例、外置材质、一个初始主方向光、主/左/右相机、已有 PC 管线引用、独立天空与 Volume。Bloom 轻量起步，SP/IP 堆栈用于后续加效果。不重写统一金属度，不自动开整套艺术效果。

层级为 01 场景模型 / 02 灯光 / 03 相机 / 04 环境与后处理。打开新场景后重新构图：地下结构、远处散点、大墙面可能干扰自动包围盒。也可直接手工拖 PMX、放灯和相机、加 Global Volume；HoMmdStageEnvironment 可承载当前场景天空/环境。通用渲图也接受没有该组件的 URP 场景。

## 开始调整

先处理玻璃、窗帘、植被的透明/裁切，再处理灯芯、发光球的 Emission 与对应真实灯。金属、地板、水面的 smoothness/metallic/reflectance 按材质用途调，不统一覆盖所有材质。

月昔梅的历史经验：完整庭院有主体、树、花枝、灯四个 PMX，旧团扇包只是道具。先辨认资源组成，再考虑灯光配方。
