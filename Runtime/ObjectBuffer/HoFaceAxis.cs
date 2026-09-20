using UnityEngine;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// 角色面部朝向参考的局部轴选择：三个轴向分别在物体组的组件上配置，
    /// 常见约定：+Z 脸前、+X 角色右侧、+Y 角色上方。
    /// <para>
    /// 这个枚举原本住在 MetadataBuffer 的命名空间里（那边是最早的使用者）；MB 删掉后搬到这里，
    /// **按 int 序列化，数值一个都没动**（`Ho-ObjectBuffer Group` 上的旧场景配置照旧生效）。
    /// </para>
    /// </summary>
    public enum HoFaceAxis
    {
        [InspectorName("+Y (Up)")]
        Up = 0,
        [InspectorName("-Y (Down)")]
        Down = 1,
        [InspectorName("+X (Right)")]
        Right = 2,
        [InspectorName("-X (Left)")]
        Left = 3,
        [InspectorName("+Z (Forward)")]
        Forward = 4,
        [InspectorName("-Z (Backward)")]
        Backward = 5
    }
}
