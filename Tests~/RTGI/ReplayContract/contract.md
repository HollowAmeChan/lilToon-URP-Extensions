# 同历史 GI 采样重求：实现前契约 v0

本轮是信息/ABI必要性证明，不是生产GI重求实现或confidence接纳。toy ledger为独立opaque字段复制测试，真实Shade关联仍须下一阶段验证。

## 必须对应的样本

NRD输入来自Final使用的 **Spatial1实际选样**（或S关时对应copy），包含最终可见性和demodulated主表面响应。生产temporal历史0从pre-S2保存，不能拿它替代NRD实际Shade历史。上一帧primary guide在当前Final前仍可读取；旧NRD signal/visibility的读取时机必须在当前覆盖前明确。

当前48B secondary缓存是本帧初始pixel的材质/位置/法线/发光/PDF，不跟随GI temporal/spatial选样，不是最终历史采样的材质缓存。GI32只保留secondary位置/normal/LogLuv辐射、GI weight、M/age。secondary DI reservoir的lightData/uvData/invPdf没有保存。age不提供来源pixel，几何/颜色相同也可能有不同旧选择，无法由最终GI包唯一恢复。

## 建议先保存选中选择，而非重新生成随机候选

HoGIShadeSecondary先用8本地点灯/4环境候选得到一个DI reservoir，再重建其light/UV并求可见性，贡献为BRDF×light radiance×**DI invPdf**/light solidAnglePdf，加emission。DI invPdf不同于外层GI weight。保存选中lightData、UV、该浮点权重，重求无需重新运行RIS或恢复旧environment RIS表/RNG序列。

secondary BRDF有specular项且viewDir取其**原source primary**方向，不能换成GI重采样后receiver方向；还需原方向或等效信息。候选16B记录为uint lightData/uvData、float DI invPdf、uint source-viewDir。无DI有效选择时可利用未用UV域区分emission-only/sky/black/invalid，但这种union和量化误差尚未验证，不能称最小充分ABI。需要时增加字段并重算成本，不能静默丢sample-kind或secondary验证信息。

## 来源继承与捕获

初始记录与初始GI包一起生成；每次temporal/spatial选中谁，就继承谁的记录。SDK低级Combine返回select bool，但当前高层T/S没有对应用导出该来源；misc16bits和unused32bits不会被普通Unpack/Combine/Store自动保留。必须有明确的GI私有来源链/sidecar传播，并验证碰撞样本和真实SDK所有选样分支。禁止按float位置/normal/radiance反向匹配来源。

可先评审应用自有、许可原样保留的GI reservoir helper扩展，在寄存器保留source-pointer，Load记录源pool地址，Combine随选样继承，Store复制sidecar；GI32外存格式保持原样，原SDK文件/DI不改。初始Store、T/S不同pool、S-off、temporal fallback/无效样本、history0的raw packed copy都必须更新sidecar，不能只改Combine。Final另捕获实际Spatial1包和sidecar供下一帧NRD重求。

## 当前世界重求不止primary到旧位置的一条开区间射线

旧secondary点可能已离开几何；TMax=distance-bias的最终visibility不证明端点仍是当前有效表面。需验证/重建当前secondary及原kind，处理miss↔hit、black、alpha、normal/material与range，再从匹配的当前primary求旧样本传输。secondary light/environment shadow也会因另一块几何运动而变化，要重求**原选择**的可见性与当前光照，不能只复用旧cached radiance。

保持原weight，按原quantization/SignalScale64与demod单位构造新旧可比信号；旧零信号也检查。灯身份/域、材质、环境版本、camera cut/time、resize、Volume等硬失效不放开。GI reservoir仍按原scene.Revision保守拒绝，NRD保历史必须有已验证confidence/primary对应；缺数据时保守失效。

## 预算和下一阶段边界

16B×3个GI pool +48B×pixel的last-shade候选，256²约6MiB，1920×1080约190.55MiB/相机；未含confidence/gradient、对齐、原生分配，不是已实现资源或VRAM测量。环形保存多帧所有48B secondary/随机表更昂贵，不在本步引入。必须先低分辨率资格、生命周期/静态跳过/新增射线成本评审，不能在当前host直接开1080驻留。

下一步先做 **真实T/S/Final来源继承观察资格**，必须主输出不变、原字段/原shader行为对照，手工Combine toy ledger不能替代它。随后才实际当前TLAS同采样重求与独立oracle，包含静态/移动caster/primary/secondary、阶跃变化像素、零→亮、旧色/灯和Volume硬重置；正确性、响应/噪声/能量和成本都证明后才考虑生产生命周期策略。归一化/phi/迭代方向继续搁置。
