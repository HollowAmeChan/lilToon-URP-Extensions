# MMD 动画导入与适配工作流

动作源目录：`E:/BaiduSyncdisk/大师/_完整动作`。旁路整理目录：`E:/BaiduSyncdisk/大师/_完整动作_整理`。`prepare-motion-library.py` 会保留原始包，按作者与动作名解压，记录 SHA256、README、VMD 类型、镜头数量、形态键段和 show/IK 段。

首次整理结果：7 个作者目录、128 个压缩包、18 个直接 VMD、666 个 VMD、1 组重复包、0 个解压失败。部分甜茶旧 ZIP 的目录含空文件名，bsdtar 会给 warning，但仍能提取可读 VMD/README；warning 写入 index，不把包标成完全无警告。

参考模型：`E:/BaiduSyncdisk/大师/_完整角色/Sour暄/Sour式初音ミクVer.1.02_by_Sour暄_194b7aa2b8fd2f9d6f0221d8767ad951.zip`，已解压到动作整理库的 `_参考模型/Sour暄`。它只作为卜逻desu/Taitohimea 动作的骨骼、IK 和 morph 依赖参考，不要求作为 Unity 角色最终导入格式。真正播放目标可以是 FBX/GLTF 角色，动作包若指定别的模型，必须按 README 建立动作实例依赖记录。骨骼和 morph 报告在 `_参考模型/Sour暄/Sour_IK_REPORT.md`，其中 morph 名称使用工程的 `HoPmxReader` 生成。

`HoUnityTools` 现在提供独立 `.vmd` ScriptedImporter，资产类型为 `HoVmdAnimation`，以及第一版 `HoVmdMotionPlayer` / `HoVmdCameraPlayer`。Importer 先保留轨道数据和作者模型名；播放器按骨骼/morph 名称绑定，show/IK 段映射到 `HoIkConstraint.IkName`，镜头使用 target + Camera 两节点。它不会与现有静态 `.pmx` importer 争夺扩展名。

FBX/GLTF 角色的适配通过 `HoVmdBindingProfile` 保存：骨骼用目标 Transform path，形态键用 Renderer path + BlendShape name，IK 用 constraint path。动作包与角色 Profile 分开保存，同一动作可以换不同角色 Profile。

`HoVmdBindingProfileBuilder` 可从 FBX/GLTF 角色层级生成初始映射，按精确名称、规范化名称和 Humanoid 骨骼顺序匹配；生成后仍需手工检查 WIK、toe、眼睛和作者专用 morph。

现有 `HoMmdSceneWorkshop` review 支持 `--motion Assets/.../clip.vmd --motion-frame 240`，会在主、左、右三个镜头应用同一个时间点；这条路径用于动作适配审查，不会保存场景中的临时播放器状态。

review 同时对每个 `HoVmdMotionPlayer` 的 `ModelRoot` 执行 morph 依赖审查：优先验证其 `HoVmdBindingProfile.morphs`，再检查精确名称和规范化名称，并把具体命中、缺失与冲突写到 `Library/HoMmdStages/MorphAudit/<scene>-<motion>.json`。日志会报告 `missing` / `conflicts`，但不会因为缺少某个角色专用表情而修改角色资产或中断静帧渲染；适配阶段应先看这份 JSON，再决定补 Profile 映射、换角色或按 README 关闭该动作实例的表情轨道。

定格 review 在编辑器模式下会立即执行 Ho IK，避免没有 LateUpdate 时只看到 FK 姿态；播放模式则继续让 Ho IK 约束按 LateUpdate 顺序执行。

## 作者规约中已经确认的情况

- 卜逻desu 的包明确包含 Sour 动作、Sour 镜头，以及若干 PV 背景＋BGM 组合包。不能只导入一个运动 VMD 后宣称动作完整。
- 兰若的包大量配套表情、镜头和道具；至少有动作 README 要求关闭 `左足IK`、`左つま先IK`、`右足IK`、`右つま先IK` 四个 IK。关闭 IK 是动作实例选项，不应永久改 PMX 模型。
- 甜茶的包大量包含 `camera.vmd`、表情 VMD、多人动作、音频或 PVkit，并且 README 会写模型来源和加载顺序。遇到无腰骨等要求时，需要在适配报告中标记缺骨，而不是静默修正。
- 少数动作作者明确说明脚部或脚踝没有制作，或者动作只针对 Sour 式模型。此类动作需要按作者说明选择裁切镜头、关闭 IK 或单独修脚。

## 运行时职责

MMD 动画的正确顺序是：

```text
VMD FK/位置曲线 → morph 曲线 → append/inherit → MMD IK → physics → HoAux 辅助骨 → 相机/渲染
```

HoAux 的 twist、fan、体积保持属于辅助形变层，不能替代 MMD IK。`HoIkConstraint` 已加入 `HoUnityTools`：它支持任意 CCD 链、Pole、局部 Euler 限制、轴遮罩、最大步进角、权重和手动求解。典型足链为 `脚踝 → 膝盖 → 大腿`；toe IK 单独建一个约束，按模型实际 toe 父骨配置。

`HoIkConstraint` 默认 LateUpdate，适合 Animator/VMD FK 曲线之后求解；动作播放器需要严格控制 transform order 时设为 Manual。VMD show/IK 开关应映射到对应约束的 `SolveEnabled` 或 `Weight`，不能把 IK toggle 当成一次性导入设置。

## 现有参考实现

`HoUnityTools/research~/PmxImportReferences/UnityMMDTools` 是 Candidum Games 的 UMT 研究副本，MIT。它已经实现 PMX 骨骼、蒙皮、形态键、MMD transform order、追加/继承、CCD IK、VMD bone/morph/camera/light/self-shadow/showIK 段、runtime-solved sparse curves、IK/physics bake 和两节点 VMD camera rig。

它不能直接与 HoPmxImporter 同时注册 `.pmx` ScriptedImporter。后续正式接入时应采用独立的动画路径：保留 HoPmxImporter 的静态场景职责，把 UMT 的解析/solver/clip builder 接进 Ho 动画 importer，或使用明确的动画 PMX 入口，不能让两个 PMX importer 抢同一扩展名。

## 动作适配报告至少记录

```text
source archive / sha256
author / action name / README path
model dependency and required bone names
bone frame count / last frame / morph frame count
camera VMD files / audio / PV files
showIK entries and required IK defaults
missing bones, missing morphs, duplicate names
chosen mode: runtime IK or baked FK
chosen camera mode: authored VMD camera or scene camera
```

不要用“动作能导入”作为验收。至少播放一个动作时间点，检查脚底、膝盖、toe、手腕、脸部形态键和镜头裁切；再用侧面机位看 IK 是否二次扭曲。

## 当前验证

HoIkConstraint 在 Unity 6000.3.15f1 / D3D12 的临时 smoke scene 中通过：三节链、32 次 CCD、目标距离误差 `0.002010015`。测试结果在 `D:/Unity_Project/BREAK_URP/Library/HoMmdStages/ho-ik-smoke.json`。这证明约束求解本身可工作，尚未证明所有作者动作都能直接适配 Sour 模型。
