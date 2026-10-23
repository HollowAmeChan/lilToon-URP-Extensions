# A01：DI/GI公共NRD执行器与通用契约

2026-10-24。生产代码数值/布局不改，目标是消除GI依赖DI内部实现和NRD通用执行与DI策略混杂。设计、已测边界和后续内存候选见Documentation~/架构/Ho-RT-公共层-设计与交接.md。

冻结原Context/Native/Timing/Manifest后独立编译，33帧GPU全部540672个RGBA分量逐位同；独立reset对照及恢复过；64组DI native dispatch/资源/上传常量/AppliedSettings逐字节同。DI调度对照不冒称独立DI像素参考。GI真实RG/compat各243请求41check、同输入小矩阵64帧和固定3实例DI+GI RG/compat各42请求10check全部actual0/diagnostics0/drift0；公开开关与resize/释放通过。

完整记录在runs.json，各run保留实际exit/完整日志/报告/源SHA。4个backend运行中前2失败（URP未初始化、冻结基线误改Shader常量名），之后2次有效（含最终当前源）；另保留初编译52336、组合fixture语法50752和旧合成33060 actual0但ASdebug2拒绝。初始化/生成器/语法修正前源均在source-index的无损gzip对象；失败不标合格。baseline原字节与移动DLL/资产GUID均校验，源码层禁GI→DI C#及公共NRD→Feature引用。

NRD池64²实际DI327696B、GI307216B，新旧一样。匹配typed transient4112B，1080布局算2081760B≈1.985MiB，尚未共享且不是1080实测。池数不含常量/外部信号/AS/pose/驱动，本步显存收益0。

```powershell
python Tests~/RenderingArchitecture/verify_validation.py
```

重跑：先generate_baseline.py生成私有工程旧实现/asmref/测试，再run_validation.ps1 -Mode backend（或small/gi/giCompat/combined/combinedCompat）；独占私有工程、hidden窗口、实际D3D12 debug，失败先archive_sources.py保留源再修。旧composite模式会增加AS实例而触发F39，只保留其拒绝证据，不用它反复压AS或宣称通过。

verify会检查当前源/源对象/所有日志报告、GPU/调度计数、真实双模块资源释放、旧DLL和GUID、分配账本及未接受失败；不把代码去重冒充显存优化，不改旧RTGI/RTDI研究归档。原GI047预审/PlanarReference未纳入本步。

补充Release验证：13960构建actual0/PASS；原始通用runner因3条Shader warning拒绝，逐行比对既有Backbuffer/build-warning-review的历史白名单后确认全为原有SolarHD与lilToon重复keyword，无新增诊断，原run保留diagnostics3。42752隐藏D3D12/Linear Mono Player actual0、304 distinct-Update camera请求/16check全过，运行时Shader/AS/fence/动态链接错误0，NRD运行正常；打包后的HoRTDINRD.dll字节与公共目录原库一致。该项证明公共插件迁移后的Release打包/加载，不扩称自动enabled-camera或复杂Player矩阵完成。

可复现run_player.ps1已实际重跑47316，第二次隐藏Release同样actual0/304请求/16check、运行时diagnostics0；两次均未使用可见窗口授权。
