namespace lilToon.URP.Extensions.MetadataBuffer
{
    /// <summary>
    /// 主 pass 的颜色附件布局（4 个：D3D 要求从 0 连续，所以本轮把两个 surface 槽摘掉后必须重编号）。
    /// 表面数值（thickness / curvature / materialClass / reflectance…）现在归 `Ho-SurfaceBuffer`。
    /// </summary>
    internal static class HoMetadataBufferAttachmentLayout
    {
        public const int ColorTargetCount = 4;

        public const int MaskId = 0;
        public const int Custom0 = 1;
        public const int ObjectCustom0 = 2;
        public const int ObjectCustom1 = 3;
    }
}
