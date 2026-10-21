# 025 默认屏幕呈现

2026-10-22，两条 Linear D3D12 Win64 Mono Release 路径均实跑：场景序列化 Camera.enabled=true、targetTexture=null，自动输出512²屏幕，GI publication 同512²。每次沿用用户短暂可见窗口授权，进程限15秒。没有 Camera.Render / SubmitRenderRequest / Volume.Update / 手动预热。

独立 HoRTGIBackbufferProbe 在 WaitForEndOfFrame 后 CaptureScreenshotAsTexture，下一 Update 读取对应 GI publication，验证 captureFrame 与原生 endCameraRendering 帧号一致；只读取已完成屏幕，不请求渲染。固定逐像素 sRGB→linear 解码；先验证 raster .215823859 与024独立线性targetRT基线 .215820313 相符，再检验合成，不按加法结果选择色域。

首次 build39064 actual1：隔离工程缺 com.unity.modules.screencapture，CS0103三条；没有运行 Player，原源码/manifest/log保留 rejected-missing-module。仅隔离项目启用本机内置1.0.0模块，参考本机6000.3.15f1 XML/API，package与XML原文/SHA保留 environment；产品包依赖无改动。

最终 RG build44940 / Player13180、compat build38344 / Player44248 全 actual0。各241 Update、240连续callback/前帧记录、11checks；测量帧跨度4.514/4.576秒。全部RGB强度.25/1加法误差RG .00471446/.00641033、compat .00471386/.00643197，原.015不变；Volume-off全部32帧恢复raster，重新启用首帧reset，30移动帧全保留history，红发光关闭首帧raw/filtered红0+reset。screen最终量化路径不等于HDR能量或5%全图资格。

runtime D3D12/Shader/Property/UAV/fence错误0、source drift0；build各五条精确历史warning。两个原有GeometryBuffer fallback/MSAA shader缺省提示与024相同，完整log保留；本夹具未用这些fallback/MSAA路径，不宣称其支持。两套完整最终payload SHA验证，无HoGIGpuProbe.dll。

10张真实屏幕PNG按backend保存：raster/quarter/one/volume-off/emission-off。已查看RG raster/one及compat emission-off，灰色基线、红蓝GI渐变、关闭红发光后蓝GI保留符合夹具预期；没有用这些纯接收平面图声称复杂场景视觉质量。`image-artifacts.json`保存尺寸/原字节SHA。

`verify_validation.py`检查1164精确source对象（6新小文本对象，其余既有证据/Git）、5完整logs、原编译失败、固定decode/帧对应/连续性/全部RGB/Volume/动态/PNG/payload。`archive_evidence.py`在最终本地binary仍存在时复核payload；verify可离线核验归档。`run_players.ps1 -Mode rg|compat`复建各自独立资产/输出，Editor隐藏、Player短暂可见；Runtime无改动。

下一026回到当前生产默认的独立seed全域/局部质量证据，保留fullgreen−5.05869%/7phase原失败，不能为了.05869个百分点放宽门槛。F39/static非零base、参考偏差与1080/完整Player预算继续，目标active。
