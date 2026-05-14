# Shoost 解包参考

这里记录 Shoost v0.16.3 解包、反编译和 shader 抓帧资料的位置，方便后续把后处理重写进 `lilToon-URP-Extensions`。

AssetRipper 解包工程：

```text
D:\Unity_Fork\Shoost_v0.16.3\unpack\ExportedProject
```

Cpp2IL 工作文件：

```text
D:\Unity_Fork\Shoost_v0.16.3\DecompileWorkFiles
```

RenderDoc shader dump：

```text
D:\Unity_Fork\Shoost_v0.16.3\RenderDocShaderDump
```

优先参考：

- `Assets/Scripts/Assembly-CSharp/**/*.cs`
- `Assets/MonoBehaviour/AMS_*.asset`
- `Assets/MonoBehaviour/AniMakeStudio_Finish_PostProcess_*.asset`
- `Assets/Texture2D`
- `Assets/Shader`：只看 shader 名和属性，不看算法源码
- `DecompileWorkFiles/Cpp2ILOutputs/ISIL`：看 C# renderer 流程和 uniform 设置
- `RenderDocShaderDump/*.dxbc.asm`：看 Shoost 自定义 shader 的 DXBC 反汇编
