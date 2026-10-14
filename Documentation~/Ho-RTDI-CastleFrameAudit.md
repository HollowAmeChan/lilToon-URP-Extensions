# Ho-RTDI：隔离古堡的实际帧支持审计

第86步将已登记的实际复制场景推进到GB/SB/DI的真实相机渲染。此前registry支持15个submesh不等于其实际主表面输入/光照链路已运行；本步读取完整输出，仍不拿复杂合并场景作解析质量或漏光基准。

隔离工程打开已复制的血月古堡，临时创建GB/SB/Ho-Atmosphere/Ho-RTDI与URP asset；克隆Main Camera的transform、FOV/near/far/culling，使用512²审计目标并固定aspect=1，不宣称与源窗口aspect下的画面逐像素相同。采用三样本/Power/NRD、原场景天空盒，按已知fixture材质“天空”配置DI排除列表。使用temporary pipeline和camera，不保存源场景、不修改其材质数组；结束恢复原pipeline、compatibility与async shader设置并销毁自有对象。

## 实际结果

RenderGraph/compatibility两入口实际exit0，字段及导出PNG逐项一致：

- 18个实际已接受灯：17个local、1个directional，无Cookie/cullingMask/baked-only排除。
- 15个accepted RT submesh，唯一排除为已知sky submesh0；12个基础Cutout按既有registry支持。
- Ho-Atmosphere SceneSkybox输入有效，源材质名UnearthlyRed。
- 512²像素全部具有有效receiver alpha，244,575像素非零；HDR峰值.570801，valid峰值平均.0247292。无负值/非有限数据，NRD有真实dispatch。
- native第二辅助buffer为2,097,152 bytes（2 MiB），不是全部DI/scene/NRD内存或帧耗时。

`pass=true`仅要求实际DI/NRD执行、有valid/lit输出和有限非负数据。全部receiver有效也包含raster可见的sky shell材质；排除列表只控制ray registry，不把它变成接收面过滤或自动天空合成。没有独立解析光照期望，因此这些平均值不能用于宣称无偏/无漏光/画质达标。

预览仅16帧warmup，仍有明显随机噪声；可见阶梯/栏杆与黑色测试球。RGB按Reinhard后gamma2.2映射显示，invalid像素用magenta，不是最终曝光/NPR处理。此预览不进入用户Assets、也不作为产品效果图。

报告与预览为`rtdi-castleframe86-rg/compat.json/png`，PNG SHA256两路径都为`28CE7DD01C3EE3C6E9877893B7490E7D84DC7C88F74F2F6F1152C481921C23B2`。Roslyn errors0、GUID唯一/diff whitespace通过，无新增DI shader诊断。只Editor/文档，Runtime/native/GI/Player未改，不重建未变Player。

执行入口为`HoRTDICastleFrameValidation.Run/RunCompatibility`，仅接受.codex-research隔离工程。下阶段按这份可复现实际帧查看使用问题，保留独立fixture的物理/动态门槛、GPU预算未证明和最终NPR合成边界。
