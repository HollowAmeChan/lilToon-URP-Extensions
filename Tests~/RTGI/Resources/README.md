# RTGI步骤012：实际资源与大尺寸边界

`verify_validation.py`核对两个实际完成进程、逐资源payload/格式/尺寸/mip/stride、别名去重、NRD池、GPU tick/fence和一个中止的1080p进程。它通过不代表1080p或稳定性能预算通过：见`evidence.json`的独立布尔值。

| 项目 | 256²实际 | 512²实际 | 1920×1080推算，未验收实跑 |
| --- | ---: | ---: | ---: |
| GI自有+NRD+实例映射payload | 16.588 MiB | 65.151 MiB | 513.997 MiB |
| AS Unity GetSize | 16.523 MiB | 16.523 MiB | 未取得 |
| 自有pose Mesh的Unity运行时统计 | 28.539 MiB | 28.539 MiB | 未取得 |
| GI.Total GPU median / p95 | 24.969 / 138.807 ms | 117.351 / 125.692 ms | 未取得合格样本 |

前两行不能不加说明相加称物理VRAM；Mesh运行时统计更不能按GPU驻留相加。payload排除GB/SB、源mesh/纹理/材质、合成临时附件、driver heap、AS scratch等。AS为Unity API报告值，未独立解析其底层分配归属。

28个去重资源：GI七个GraphicsBuffer与两纹理；NRD五个外部信号/guide、六permanent、四transient、三常量；Scene一个实例映射。PublishedDiffuse是filtered别名，只计一次。已创建RTHandle按实际graphicsFormat/block、尺寸、mip、volumeDepth和samples计算；缓冲按实际count×stride。

这两个场景有32灯容量、16实例容量、Cutout full visibility、128²全mip PDF和16384 RIS，payload公式为：

`259×w×h + 96×(reservoirArrayPitch−w×h) + ceil(w/16)×ceil(h/16) + 419412` 字节。

其中`reservoirArrayPitch=ceil(w/16)×256×ceil(h/16)`；NRD为`79×w×h + tileCount + 196608`。1080p reservoir含padding为191.25 MiB，NRD156.421 MiB。这是按已测布局投影，1080p失败不能改称分配/渲染成功。

实际进程：256 PID52360、512 PID56712均exit0，40样本/marker、编译诊断0、没有Unity fence错误；1080 RG PID44476连续`D3D12Fence::Wait`，核对专用projectPath和已记录PID后人工中止，actual exit−1，无fresh report，compat未执行。初版审计器误读setter-only GraphicsBuffer.name造成的编译exit1也保留。日志与SHA在`run.json`、`rejected-compilation.json`，没有用旧文件冒充HD报告。

计时远慢于011；当时整卡约7.3～7.6/8 GiB占用，host瞬时状态保存在`host-observations.json`。该观测不能证明唯一原因，也不是本任务专属显存。Runtime/Shader/NRD相对011完全未变，不接受据此判断算法退化、分辨率比例或稳定预算。没有操作其它应用/Unity进程，没有重置驱动或调TDR。F31规定新条件前不原样重试HD。

`source-at-run/run_validation.ps1`是实际执行脚本，SHA匹配run。最终runner后来加15分钟上限、fence错误自动停止自身进程，默认只跑已完成两档；HD需`-IncludeHd -HostConditionNote '具体新条件'`。新增控制仅通过PowerShell语法复核，未声称实跑新控制或重新取得HD证据。运行会生成新的run.json；已有证据先归档，不能覆盖历史失败。

本步只有Editor取证/文档，没有Runtime算法、native DLL或NRD参数变更，因此不重复相同Player构建。010 filtered质量门槛仍失败；半GI设计见[输入域与重建设计](../../../Documentation~/RTGI/Ho-RTGI-半分辨率设计.md)，尚无实现或画质验收。
