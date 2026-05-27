using System.Collections.Generic;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ImageProcess;
using lilToon.URP.Extensions.MetadataBuffer;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.ScreenProcess;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.SubsurfaceScattering;

namespace lilToon.URP.Extensions.Debugging
{
    public static class HoDebugViewRegistry
    {
        public static IReadOnlyList<HoDebugViewInfo> AllViews
        {
            get
            {
                List<HoDebugViewInfo> views = new List<HoDebugViewInfo>();
                AddRange(views, HoMetadataBufferDebugViewInfo.Views);
                AddRange(views, HoGeometryBufferDebugViewInfo.Views);
                AddRange(views, HoShadowCastDebugViewInfo.Views);
                AddRange(views, HoSubsurfaceScatteringDebugViewInfo.Views);
                AddRange(views, HoPlanarReflectionDebugViewInfo.Views);
                AddRange(views, ScreenProcessDebugViewInfo.Views);
                AddRange(views, ImageProcessDebugViewInfo.Views);
                return views;
            }
        }

        public static IReadOnlyList<HoDebugViewInfo> ShaderCollectionViews
        {
            get
            {
                List<HoDebugViewInfo> views = new List<HoDebugViewInfo>();
                foreach (HoDebugViewInfo view in AllViews)
                {
                    if (view.RequiresShaderCollection && view.HasShader)
                    {
                        views.Add(view);
                    }
                }

                return views;
            }
        }

        private static void AddRange(List<HoDebugViewInfo> destination, HoDebugViewInfo[] source)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination.Add(source[i]);
            }
        }
    }
}
