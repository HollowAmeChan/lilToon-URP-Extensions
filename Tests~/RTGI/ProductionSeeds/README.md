# 026 当前生产默认的独立 seed 全域与局部测量

本步提供下一动作的依据，**没有修改生产渲染，也没有修复画质**。早期019发生在pre-S history和GI32F之前，不能直接代替当前默认的误差/波动证据。本轮只当前TS模式，不重复四模式矩阵；固定原8seed，各GI/NRD reset、64warm/256观察、256²古堡、18灯/15实例/12Cutout/次级天空及原primary合同。

Editor38940短资格actual0，Editor39616正式actual0；后述编码修正后Editor44052短资格actual0。runtime D3D12/Shader/Property/UAV/fence/source drift均0。请求数98/2594/98；正式8×256=2048观察帧、134217728 receiver samples，全部16phase每段各1048576 samples。进程exit0与report.pass/seedFunctional只代表测量和旧功能合同完成，**不代表原5%质量通过**；逐段full/phases与analysis保留真实门槛。

| 当前8seed全域汇总 | R | G | B |
| --- | --- | --- | --- |
| raw均值 | .00132385418 | .000139750312 | .000127679479 |
| filtered均值 | .00126142420 | .000132541894 | .000121521042 |
| filtered/raw差值 | −4.71577% | −5.15807% | −4.82336% |
| 配对差的seed经验SE / raw均值 | .05379% | .11936% | .08255% |

每个seed作为一个统计单位，不把相关帧/像素当iid，不提供正式置信保证。3/8 full通过，逐seed16phase失败数7/11/11/8/8/9/11/7；合并后全图仍不通过、9/16phase不通过，固定8×8网格全部64块中32不通过。绿色距5%仅.158个百分点，相对经验SE约1.32，不能把边界当高度确定，也不放宽或挑seed。filtered/raw约5%的均值差存在，并不等于相对独立物理真值损失5%。平均逐像素时间方差raw4.54467e−6、filtered5.80513e−9，不宣称跨不同代码/负载GPU成本改善。

每像素raw/filtered均值由CPU double累计后存float32，HGI1 little-endian header+width/height/frames+逐像素6float；不改变GPU格式。每个map从SHA开始验证，可独立重建全图与所有phase。raw JSON和maps仅gzip，原字节/SHA均保留；12原maps中两对短资格逐字节相同，共用归档对象。旧同模型15/16参考点仍有选择偏差和未收敛暗点，不作全图真值。

局部协议在测量前固定：所有64个32²网格都报告；前4seed选择top12亮度差点，后4seed评估同坐标。首位(221,184)探索差.01454，holdout仅.000127±.000172经验SE；不能继续围绕该单窗极值调整算法。6个x=86位置（y=206/227/255/252/204/226）holdout亮度差仍.00159～.00252，均大于其经验SE约4.5倍，RGB差约−35%～−66%。这支持优先调查该结构，**没有证明raw就是真实答案或NRD造成错误**。全部12点含反向/缩小结果在analysis，不能只挑这6点接纳完整质量。

编码review发现Windows Python默认locale将Castle helper从UTF8写成GBK；原实际运行字节保留。修正为显式UTF8后Unicode文本严格相同，重新编译/场景资格44052 actual0，两份短资格全图publication map逐字节相同。encoding-repair绑定前后源SHA/格式/图SHA；不是改门槛或重写历史。后续文件读写应显式UTF8，不能提交乱码/用日志成功掩盖源码差异。

最终静态review还修正一行进度JSON的testedModes标签：Snapshot(false)显式传同一modes数组，避免TS-only运行中仍显示旧四模式标签。最终Snapshot(true,modes)和所有GPU调用/报告数据均未改；reporting-review绑定精确源字节替换，verifier区分真实运行源与此纯报告修正，不伪称原run使用新字节。该调用复用已编译的签名和现有局部变量，无新渲染测试或画质声明。

239精确source hash只有5新文本对象，其余复用既有证据/Git；3完整logs、3原JSON+12原maps、分析与verifier约12.6MiB。源码仅Castle入口传flag、SeedAudit显式TS/全部phase/map元数据、SpatialAudit CPU map导出，无Runtime/SDK/NRD参数改动。`run_validation.ps1 -Mode qualification|matrix`在自有项目隐藏运行，15分钟/fence只停自有进程。`analyze.py`重算全部统计，`verify_validation.py`检查源/日志/计数/域/门槛/map重建/编码修正/holdout账目。

下一027先针对6个保留位置及相邻控制点取得**同支持域无复用高样本一跳参考和primary/guide证据**，区分真实薄结构与默认TS有限样本/NRD滤波差异。已有017参考不覆盖这些新坐标，禁止靠本轮raw/filtered直接定性错误、补gain/放宽sigma或重启纯精度扫描。全图原5%与全部phase、static/F39、复杂动态和1080/完整Player预算继续，目标active。
