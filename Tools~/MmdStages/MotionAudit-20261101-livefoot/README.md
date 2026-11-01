# EX-HAPPY 暂停现场：脚部跳变定位

调查日期：2026-11-01。工程 `D:/Unity_Project/BREAK_URP`，Unity 6000.3.15f1。

## 本次具体现场

- 场景：`Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/EX-HAPPY.unity`。
- 动作：`Assets/00动画/VMD动画/甜茶/EX_HAPPY/EX_HAPPY/EX_HAPPY/motion.vmd`。
- 主骨架：WTD `Armature-Swimsuit`，运行时已包含第二套服装合骨架结果。
- Unity 帧 949，`Time.time=11.73602`；Sequence 时间 `10.75447 s`，VMD 帧约 `322.6341`。
- 用户关心的是相邻帧跳变，单帧的大幅转折、姿势角度或四元数符号变化本身均不算故障。
- 不把本现场当作先前的 NEMONEMO 8–10 秒或 Sour 截图。

## 两处已复现原因

1. **绝对膝盖点不适合直接当异比例目标骨架的 Pole。**
   当前重定向器按骨盆和全身位移比例映射源膝盖的绝对坐标。WTD 与参考 PMX 的上下腿长度、脚踝静止高度不同；源膝点映射到目标后，会靠近并穿过目标髋—踝轴。投影方向翻转使目标大腿/小腿选择另一个弯曲平面。源参考腿本身在这个窗口没有对应的大跳变。
2. **同一帧存在第二次、使用不同 Pole 的自动腿 IK。**
   现场目标 Foot IK 的真实 Mode 为 `LateUpdate`，即使 Sequence Builder 的代码会设置 `Manual`，也不能据此假定运行时仍然是 Manual。
   Sequence 先重定向并设置源膝 Pole；后续 `HoParentConstraint` 把 Pole 重写成相对 `Armature-Swimsuit` 的固定偏移（Z 约 0.79223）。IK 的 32100 顺序随后再解一次，旋转目标腿及其子级脚掌。这次求解不再恢复重定向器保存的脚掌朝向。

在用户暂停的实际帧中，左右脚掌与应复制的参考世界旋转偏差约 15° / 12.59°；这只是同帧覆盖的证据，不凭它单独声称存在时间跳变。

## 隔离 A/B

用现场骨架的纯 Transform 副本、原始缓存绑定姿态、IK 参数、重定向映射、HoAux 操作复现；不克隆可见渲染器，不采样原始角色，不推进游戏时间。

从源帧零按半个 VMD 帧推进，记录 **9–12.5 秒，60 Hz**。分别记录重定向后、父级约束和 HoAux 后、额外 LateUpdate IK 后。

| 变体 | 右脚掌最大相邻旋转 | 右小腿最大相邻旋转 | 左小腿最大相邻旋转 |
| --- | ---: | ---: | ---: |
| 现场顺序 | 约 30.08° | 约 168.84° | 约 48.03° |
| 只禁止额外自动 IK | 约 0.82° | 约 161.16° | 约 48.03° |
| 同时改为传递源弯曲方向 | 约 0.82° | 约 1.84° | 约 2.15° |

源参考右脚掌最大步进约 0.82°，源参考右小腿约 2.70°。不能只修第二次求解后就宣布腿链连续性已修复。

每个实验前后 Unity 帧均为 949，原角色所有 1511 个捕获 Transform 的最大世界位置与旋转变化均为零，Play/Pause 状态不变。

## 修复状态

**用户确认后，已应用到正式 HoUnityTools 包并在 BREAK_URP 编译加载。** 修改位于 `Runtime/MmdMotion/HoVmdHumanoidRetargeter.cs` 与 `Runtime/RigConstraints/HoIK/HoIkConstraint.cs`。运行时反射确认 `TransferPoleBend` 与 `SolveNow(bool)` 均已存在；加载的 Runtime DLL 编译时间为北京时间 2026-11-01 11:32:22。应用时用户已经退出 Play。

邻接的 `HoUnityTools-live-foot-jump.candidate.patch` 保留为本轮修复历史；不包含此前另一个未提交的 no-pole 膝盖求解改动，不应再次直接应用。

候选方案：

- 传递源腿的弯曲平面方向，经源/目标空间变换和轴方向运输，构造离目标髋—踝轴有足够距离的 Pole；不再直接映射源膝盖绝对点。
- 给外部显式求解添加“本帧禁止再次自动 LateUpdate”的运行时标记，不永久改动用户的 serialized Mode。
- 保留脚掌旋转恢复、目标 toe IK 禁用及现有膝盖限位机制。

初轮隔离实现使用副本上的 `Manual` 模式模拟禁止机制。第二轮将候选补丁的完整 IK / Retargeter 源码改名编译进外部诊断 DLL，在相同隔离骨架上直接调用候选 `ApplyPose` 和真实 `LateUpdate`，验证本帧禁止机制；不再依赖 Manual 模拟。该轮仍保留目标组件的 `LateUpdate` 设置。

正式包编译后执行既有 `IkPoleValidation`：24 项、零失败；`VmdProductionValidation`：10 项通过，包括运镜、Humanoid 自动适配、回到同一帧不重捕获绑定姿态等。当前场景与 dirty 状态保持不变。用户仍需正常播放做视觉复核；这些检查不代表所有动作已经验收。

候选 patch 已通过 `git -C D:/Unity_Fork/HoUnityTools apply --ignore-space-change --check <patch路径>`；当前仓库自动换行转换配置要求该检查的忽略空白选项。

本轮未覆盖所有动作、所有角色、APV/RTShadow、录制采样收敛或脚底接触优化。

## 随后发现的 Console 报错

用户粘贴的 89 个日志块中，86 个是同类 `SerializedObjectNotCreatableException`；另有一次 GameObject Inspector 的失效目标、一次 Persistent 8 allocations 提示和一次 lilToon 普通 D3D 日志。

- 实际扫描发现 87 个目标已销毁的 Editor 对象：84 个旧 Volume 子编辑器，以及 GameObject / Transform / SkinnedMesh 编辑器各一个。已清理；复查失效 Editor 为零。
- 当前加载的三份 VolumeProfile 均没有 missing component，舞台本地 Profile 保留 10 个组件。
- `HoMmdRenderDiagnostics.LiveVolumeCopy` 等工具路径销毁临时组件时，没有先释放持有这些组件的 Editor。已在临时 Profile Dispose、SetMode 移除组件、RemoveBuiltinFilters 三处补上按目标定点释放，并同步项目已安装脚本。
- 七种真实 URP Volume 编辑器生命周期回归通过：释放匹配 Editor，组件和 Profile 仍保留，无关 Profile / Editor 不受影响。随后一次正常域重载没有再产生面板空引用。
- **Persistent 8 allocations 仍未定位或修复。** 已将当前 Editor 会话的 Native Leak Detection 从 Enabled 切为 EnabledWithStackTrace。最新域重载仍提示 8 allocations，但旧分配没有补录调用栈；不能据此猜测是哪个插件或渲染 Feature。保留 Console 历史，没有通过关闭检测或 ForgiveLeaks 隐藏提示。

## 本机证据

`D:/Unity_Project/BREAK_URP/Temp~/MmdMotionAudit/`：

- `live-exhappy-paused-full.json`：实际暂停现场，含全部 Transform、组件和蒙皮骨骼路径。
- `live-exhappy-window-before.json`：原始执行顺序的隔离采样。
- `live-exhappy-window-owned.json`：只禁用额外自动 IK 的对照。
- `live-exhappy-window-fixed.json`：弯曲方向 + 单一求解所有权的对照。
- `live-exhappy-window-candidate.json`：使用完整候选源码和真实 LateUpdate 方法的独立编译回归。
- `live-exhappy-window-summary.json`：归一化四元数角差统计及原现场未变化的证明。
- `editor-health-before.json` / `editor-health-repair.json` / `editor-health-after.json`：真实 Editor 目标与 Profile 检查。
- `editor-lifecycle-regression.json`：七种 Volume 编辑器的资源释放回归。

临时外部诊断源在本仓库 `Temp~/MmdMotionLiveAudit`。最初尝试 Mono 调试附加发现 Editor 关闭 Debug Mode；已取消所有开启调试/重编译选项。最终通过现有 Mono 的临时诊断程序集，把读取委托排入 Editor update，不使用鼠标/键盘，不切换 Play/Pause。诊断产生的临时骨架在 finally 中销毁。旧临时 `HoVmdMotionSecondPass.Tick` 诊断回调已从本次域的 Editor update 摘除，下一次正常域重载会按原代码重新注册；生产序列和约束未被禁用。
