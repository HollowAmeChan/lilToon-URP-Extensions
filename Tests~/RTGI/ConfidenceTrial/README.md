# 步骤046：受控同采样confidence与独立NRD历史候选

结果：接线/输入资格通过，产品接纳拒绝。两个独立NRD分支读取真正相同的生产FinalNRD输入，比较盲保留历史与同旧样本confidence；原版+lineage双GI的全部原GI32/raw/NRD输出仍逐位同。候选没有已证明的画质收益，生产Runtime/DI/NRD/reset未改。

## 实际运行

| 运行 | PID | 帧 | 实际结果 |
| --- | --- | --- | --- |
| 初短资格 | 46404 | 16 | actual1：保存仍打开的stream时sharing violation，完整保留 |
| 修保存后短资格 | 41400 | 16 | actual0，全部输入/oracle门槛通过 |
| 正式RG | 50092 | 128 | actual0，全部输入/oracle门槛通过 |
| 正式compatibility | 53052 | 128 | actual0，结果与RG一致 |

有效运行compile/Shader warning/AS debug/fence/source drift均0，hook/资源恢复且activeProcess=null。每正式核520192个previous actualShade包关系；原3poolGI32/raw/NRD所有RGBA逐位同，第一静态phase的两实验NRD分支也逐位同原NRD（phase0不消费候选confidence）。独立源/日志/原报告/stream均有SHA及无损归档，失败数据未改成通过。

## 输入和支持边界

沿045四固定AS平面/两点灯、环境关、静止主表面/常量材质八phase；每phase正式16帧，属于阶跃后保持，尚非连续运动。当前primary位置与上一帧同像素记录逐位比较；首帧/失配、端点失效或unsupported kind保守confidence0。这个夹具中的静止对应保护不证明移动相机/primary、变形或normal/material改变的通用对应。

保留旧DI choice/UV/invPdf/sourceview，当前TLAS验证次级端点并重求当前材质/同灯shadow。比较旧cached RGB×旧primary visibility，与同选择当前RGB经原LogLuv encode/decode×当前primary visibility；staticprimary下共同Lambert/GIweight相消。g=abs(newLum−oldLum)/max(newLum,oldLum,1e−10)，confidence=(1−g)^8，不模糊/不调指数，旧零也比较。首次及alpha/材质phase变化硬reset；仅受控transform试验放开分支NRD历史，原GI reservoir和原版/lineage NRD保守reset不动。

两正式后端：17140失效/unsupported/首帧无primary历史→confidence0；162零→亮、8700亮→零→confidence0。CPU独立有限plane/alpha/point-light BRDF继续通过；CPU另完整实现float64 LogLuv及gamut clamp，confidence maxabs0.0134465968，小于预设0.03（FP32编码bin边界经八次方传播的受控资格门槛，不是普适误差证明）。所有失效reject和极端变化confidence严格为0。

## 失败、收益和取舍

初46404全部16帧已写，但Save在独占stream未关时ReadAllBytes失败。原源码/日志/partial正式报告和二进制完整保留；先关stream再hash后重跑，不修改渲染算法。

初CPU confidence模型遗漏GPU当前RGB的LogLuv往返；frame1有845超界、maxabs0.0383959。光谱色gamut clamp/chroma离散不能仅用logY步长估计。保留initial-unquantized-confidence-oracle.py.txt/SHA/失败计数，补独立完整codec后通过，原GPU数据和0.03阈值不变。不能扩大门槛掩盖此模型错误。

收益仅在实际confidence→NRD接线及实验隔离。短窗没有相对原策略的噪声改善：挡片恢复phase4末8帧relative frameL1，baseline约0.01595、candidate约0.02273；phase1 baseline0.01153、candidate0.01313。不能把短窗自均值当真值/能量，也不能据此宣称残影通过。NRD原信号单位为physical/64；analysis物理均值已将NRD RGB乘64，raw不变。

本步拒绝生产接纳，不重开局部phi/归一化/迭代试验。下一步必须补独立收敛reference与连续/阶跃运动的变化像素L1/能量/噪声，再决定候选去留；没有收益就搁置，禁止不断扫描confidence指数/模糊/阈值。

额外external纹理278528B=0.265625MiB（64²两组NRD输出/validation、confidence和previousPrimary）；两额外NRD core池、既有4MiB endpoint诊断、双GI AS/NRD、rays与读回另计。没有总VRAM/GPU耗时/1080预算资格。

## 复核

```powershell
python Tests~/RTGI/ConfidenceTrial/verify_validation.py
```

verifier核精确源/日志/捕获SHA、初保存失败、原输出对照、上一帧真实包、独立几何/alpha/BRDF/codec/confidence、独立NRD第一phase逐位同与每图SHA、硬reset记录及当前源码漂移。输入资格pass不等于产品quality pass。生成器只写私有工程Shader，保留历史生成manifest；每次run独占私有Unity工程。

## 末8帧变化诊断（每phase保持16帧，不能作真值）

| phase | 原reset | 盲保留 | confidence候选 |
| --- | --- | --- | --- |
| 0 | 0.015571 | 0.015571 | 0.015571 |
| 1 | 0.011531 | 0.013614 | 0.013127 |
| 2 | 0.016503 | 0.021546 | 0.018600 |
| 3 | 0.020079 | 0.023916 | 0.020285 |
| 4 | 0.015949 | 0.022638 | 0.022735 |
| 5 | 0.037940 | 0.037940 | 0.037957 |
| 6 | 0.014623 | 0.014623 | 0.014657 |
| 7 | 0.091984 | 0.091984 | 0.092578 |
