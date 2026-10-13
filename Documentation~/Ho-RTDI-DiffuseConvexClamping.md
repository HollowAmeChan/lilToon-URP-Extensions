# Ho-RTDI：diffuse历史颜色凸裁剪

第84步将第83步已验证的线段裁剪接入DI private HistoryClamping，并同步固定源派生generator。原vendor文件/hash、specular、SH分支、native/GI不改；Sigma/预算、box构造/扩展、加速/reset和moment修正保持。

## 问题与改动

第82步actual stage证明，关闭红/蓝灯后raw与PrePass为0，重置首帧全0，第2帧Clamping的YCoCg三轴独立截取先生成假通道，第3帧Temporal才反馈。不是旧灯身份或缺少reset。已有nonnegative限制修掉负值，却会保留生成的正值。

Fast center由原box扩展保证在box内。现在RADIANCE diffuse按各轴求fast→normal线段的box交点，再用同一个标量做颜色插值；不会离开两端共享的RGB零通道平面。它保留真实历史端点之间的颜色变化，也不根据当前随机raw为0硬清输出。Specular仍使用原clip，SH仍用vendor逻辑。

## 实际生产验证

未使用Editor shader替换的关灯stage两路径中，raw/PrePass/Temporal/Clamping/Atrous/published关闭通道峰值全部为0；原blue .00114059/red .00158405消失。动态九阶段由opaque6/9提升8/9，100:1短窗能量失败保留。

两路径各16组默认single彩灯noise与完整primary motion通过，256/512动态diffuse六组通过，Runtime三样本Cutout Spot/step/32灯/HDR四组通过。第83步mixed天空spec仍2/4，未宣称本步解决镜面局部滤波。

四份Win64 Mono release（Single/Multi × RG/compatibility）构建errors0，实际D3D12 Player均pass/exit0。默认single raw/filtered MSE保持.050400/.000510；multi RG约.019103/.000206693、compatibility约.019103/.000219972。Mixed、HDR/环境、material exclusion、Cutout、primary origin等既有门槛均通过。实际D3D11 inactive分支也pass/exit0。

构建最初在质量进程尚未退出时启动，被隔离工程占用挡住，未算验证；确认原进程终止后顺序重新构建并执行。没有用失败/无报告的构建作部署证据。

Roslyn errors0、original SHA/派生check、diff whitespace通过，无新增DI shader诊断；外部SolarHD/重复keyword警告保留。native DLL SHA保持 `5EFF9D42D56DA8B38D305BC483D84263AEE3C31A6C6C3A0A637370DA68220EDB`。Generator重写的其它派生文件没有逻辑diff，不纳入本步改动。

## 边界与下一项

这项修复阻止clip主动创造端点没有的颜色，并非任意输入/历史下的完美保色，也没有GPU耗时证明。包含真实非零旧历史的recolor仍需confidence/历史机制；旧混合镜面与强弱灯短窗质量问题继续记录。下一项应回到真实DI使用/目标场景支持或这些具体有效失败，不反复改变已过门槛的参数。

报告`rtdi-colourstage82-*`的生产重跑、`rtdi-convex84-player-*`和`convex84-*`日志在隔离工程，独立候选的第83步报告继续保留。DI输出与最终lilToon合成边界保持。
