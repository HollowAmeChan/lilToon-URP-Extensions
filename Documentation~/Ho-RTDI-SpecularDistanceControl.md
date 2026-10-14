# Ho-RTDI：specular距离单变量控制

第96步继续第95步输入审计。核对用户fork NRD frame/helper、固定v4.17 Temporal与PrePass路径：距离用于镜面虚拟位置/追踪，后续Atrous没有直接读取它；当前生产diff/spec PrePass半径均0。合同完善与静态偏差定位是两件事，不把距离关联差当作已证明根因。

只在隔离工程复制DI compute，将specular NRD输入alpha设为1或65504；diffuse alpha、RGB、材质因子、raw输出、采样/复用/可见性、NRD设置与其他shader保持。scoped替换恢复引用，没有生产修改。两个常量只用于控制，不代表有效in-lobe距离；没有设0或声明为正式fallback。

## 混合环境与直接运动

原四组环境128²、512观察、32帧×4探针读回：near实际alpha全部1，far全部65504；原第95步报告不覆盖。两组距离下正式环境都2/4、实际exit1，mixed filtered spec能量数值完全一致：none红/蓝1.050758/1.048814，TS1.044370/1.050020。相对生产只有约1e-5变化，距离跨度没有解决5%失败。

64²遮挡运动两控制各3/3，frameL1 .110073/.343487/.347752完全相同；完整primary static/smoothZ/stepZ两控制均通过，镜面frameL1 .051244/.049972/.051900完全相同。上述六个实际进程退出为环境1/1、遮挡0/0、primary0/0，没有据运动通过宣称近距正确。

## 曲面相机运动消费检查

单平面检查不能代替更一般的追踪证据。扩展已有透视sphere相机15deg/s运动fixture，roughness .3/.7、五采样/复用模式、三独立序列共30配置，192预热/64观察，独立mesh/BRDF参考，原raw3%/filtered5%和shape .15门槛不变。

两个控制实际全部通过、exit0。30条日志中24条的过滤指标不同，6条单delta确定性旁路相同；raw spec能量/shape日志值都相同。六位日志精度下，最大filtered spec ratio差 .009400、L1差 .042712。例如rough .298/mode2/seed0，near L1 .136964、far .094252，均通过。距离真实影响NRD消费，而不是测试只改了无人读取的纹理。

该日志比较没有逐像素bitwise证明，也不据一例接受far距离。原始信号只改alpha的源合同及GPU读回、实际输出差异共同支撑本实验；不同真实场景仍需有效lobe距离与运动检验。

## 结果与后续

Roslyn errors0、GUID唯一、八个日志无新增DI shader warning/error或SourceAssetDB时间错误，diff通过。距离读回报告 `rtdi-guidedistance96-near/far-rg.json`；正式环境前缀 `rtdi-runtime78-env-baseline-guidedistance95-distance96-*`，运动/primary `rtdi-distance96-*`，curve两份日志 `distance96-RunNear/FarDistanceCurved.log`，都在隔离工程。

不接纳常量或直接选最近灯距。距离合同缺口保留，但当前静态后续滤波偏差不能靠它解释。下一项应使用独立无采样噪声的镜面参考输入作空间滤波控制，区分噪声驱动的搬移与过滤本身的形状改变；参考RGB必须保留实际material demodulation/scale，不能直接把CPU颜色当成NRD内部信号或放宽门槛。仍限DI，无通用PT/GI扩展或最终NPR合成。
