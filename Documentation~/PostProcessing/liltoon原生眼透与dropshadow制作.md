快速制作头发伪阴影+眼半透	设置速览												
需要修改stencil与队列	面部材质body设置：comp=always, pass=replace,ref={自定值1}												
	前发材质hair设置: comp=greaterequal, pass=zero,ref={自定值1}												
	前发阴影材质hair fakeshadow设置：comp=equal, pass=keep,ref={自定值1},渲染设置Srcblend=Zero，Dstblend=SrcColor												
	前发半透补偿材质hair faketransparent设置：渲染模式=透明，comp=always, 主色-alpha蒙版=Replace												
	眉毛眼睛材质eyebrow设置：comp=always, pass=decrementwrap												
	没有要求的一律认为，ref=0，comp=always，pass=keep，队列不动												
	流程												
	修改脸的材质（如果跟身体共用材质，复制一份给脸物体专用，如果分不开就用遮罩吧，下面都一样）												
	comp=always, pass=replace,ref=一个你自己定义的值												
	修改眉毛的材质，如果没有单独的，跟脸一样的处理												
	comp=always, pass=decrementwrap,ref=跟上面脸材质一样的值												
	复制一份头发材质为前发假投射阴影专用，修改shader为_lil/fakeshadow， comp: equal，给需要投射的前发加一个材质槽，放入这个，ref值修改为眼透里面一致的												
	此时阴影为简单上叠颜色，可以修改渲染的混合模式为正片叠底（右图）。还可以在主色使用专门的阴影投射贴图，省事直接不要贴图手拉一下												
	再复制一份前发材质为前发眼透专用，保持shader不动，给需要眼睛透过的前发再加一个材质槽，放入这个，ref值为0												
	修改liltoon渲染模式为透明，修改comp，修改主色-alpha蒙版为replace，此时可以通过调低Transparency来控制眉毛的透明程度												
	注意												
	保证以[眉毛眼睛]-[脸]-[前发]-[前发半透补偿]-[前发阴影]，这个队列顺序绘制。												
	默认眉毛眼睛可能在脸之后，需要手动改到1999保险，前发给2001，半透补偿默认透明队列2460（如果头发也半透需要注意他需要靠后），最后liltoonfakeshadow默认队列为2505												
	同时注意有描边的模板测试(stencil)里面两个都要改												
	如果不想要半透的效果，只要眼睛完全显示在眉毛前面，可以完全不需要前发半透补偿材质												
	需要注意阴影跟眼睛半透的材质主体都在头发上（这可能会比较反直觉）最后全加上是前发有三个材质（如右图）												
	阴影的范围可能跟眼睛半透的范围不同，侧发是不需要投射阴影的一般，所以侧发只用俩材质												
	如果以后还需要修改头发材质的效果，如果发现显示比较奇怪，需要同时修改前发材质+前发半透补偿材质（因为本质上这种半透是吧头发渲了两遍）												