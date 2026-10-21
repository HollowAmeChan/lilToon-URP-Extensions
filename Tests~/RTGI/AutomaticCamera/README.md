# 024 自动 enabled-camera 证据

2026-10-22：两个 Linear D3D12 Win64 Mono Release Player，真实相机从序列化场景加载起 enabled，连续 240 次 `endCameraRendering`；下一 Update 读取实际前一帧目标纹理。无 SubmitRenderRequest、Camera.Render、手动 Volume.Update 或请求预热。Runtime 无改动。

先复读 DI MotionProtocol/GpuTimingAudit：已有 hidden 普通启动失败，因此本步先尝试新的 serialized-enabled/serialized-target 条件。原 RG build 46000 / Player 7000：180 Update、0 callback，target 未创建；保留 `rejected-uncreated-target`。显式 Create 仅分配目标 GPU 资源后，build/Player 真实 exit 0/1，targetCreated=true 仍 0 callback；保留 `player-run.json` / `player-results`。不把分配称渲染。

用户明确授权短暂可见窗口后，同一份 RG 可执行及完整 payload 未变，仅 Hidden→Normal：Player 43576 约 8.15 秒自行 exit0；compat build 44364 / Player 45248 约 8.30 秒 exit0。两个 Player 各 241 Update、240 callback/记录、10 checks，D3D12 debug/Shader/绑定错误0，source drift0。两 build 各保留5条已逐字核对的历史 warning，不称0。RG/compat 的报告 compatibility=false/true。

各30移动帧保持历史；Volume off 的32帧实际输出恢复 raster，re-enable 第一帧 reset；红发光关闭第一帧 raw/filtered 红分量均0且 reset。0.25/1强度的最大RGB加法误差 RG 8.795e-5/6.855e-5、compat 8.798e-5/6.711e-5，原门槛 .015 未放宽。每条读取与渲染相差恰好一个应用帧，renderFrame 连续递增。最终 payload 无 HoGIGpuProbe.dll。

**边界：** 相机自动输出到64² ARGBHalf targetTexture；窗口本身512²是 Unity 最小尺寸。窗口可见启动不等于已验证 backbuffer/最终屏幕呈现，未拍摄或声称视觉QA。这里只补正常启用相机的自动调度、合成、Volume 与动态响应。具体 hidden 无回调机制未定位；不泛称 Unity/驱动缺陷。不得用本步代替完整场景、画质5%、1080/完整Player GPU预算或 F39。

`verify_validation.py` 验证原失败、报告/完整log/SHA、1163源码对象（5个新增小文本对象，其余复用既有证据/Git）、逐帧连续性、全部RGB加法、payload清单及无手动请求。`archive_evidence.py` 冻结证据并在本地最终二进制仍存在时核验完整 payload；第一份旧 binary 被后一次 build 覆盖，只有原 manifest，不伪称可复验该旧 binary。完整 log 与 source-index 支持历史重建。`run_players.ps1` 是保留的隐藏失败条件；`run_visible.ps1` 复建 compat 并限时15秒运行，须沿用本会话用户可见窗口授权。

下一步025：独立 serialized-enabled 且 targetTexture=null 的 backbuffer 呈现证据，明确截图/读回的颜色空间与位置映射，避免把捕获请求伪装成渲染请求。继续每步 review、收益/成本、失败禁试、交接与白名单提交；目标 active。
