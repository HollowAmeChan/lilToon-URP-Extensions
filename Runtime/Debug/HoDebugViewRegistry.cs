using System.Collections.Generic;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.GeometryData;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.GTAO;
using lilToon.URP.Extensions.SSGI;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ImageProcess;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.ScreenProcess;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.SubsurfaceScattering;
using lilToon.URP.Extensions.SurfaceBuffer;

namespace lilToon.URP.Extensions.Debugging
{
    public static class HoDebugViewRegistry
    {
        public static HoDebugViewInfo[] SystemViews => HoAdaptiveProbeVolumeDebugViewInfo.Views;

        public static IReadOnlyList<HoDebugViewInfo> AllViews
        {
            get
            {
                List<HoDebugViewInfo> views = new List<HoDebugViewInfo>();
                AddRange(views, HoAdaptiveProbeVolumeDebugViewInfo.Views);
                AddRange(views, HoObjectBufferDebugViewInfo.Views);
                AddRange(views, HoGeometryBufferDebugViewInfo.Views);
                AddRange(views, HoShadowCastDebugViewInfo.Views);
                AddRange(views, HoSubsurfaceScatteringDebugViewInfo.Views);
                AddRange(views, HoPlanarReflectionDebugViewInfo.Views);
                AddRange(views, HoSurfaceBufferDebugViewInfo.Views);
                AddRange(views, HoSSGIDebugViewInfo.Views);
                AddRange(views, HoGTAODebugViewInfo.Views);
                AddRange(views, HoGeometryDataDebugViewInfo.Views);
                AddRange(views, HoCharacterShadowDebugViewInfo.Views);
                AddRange(views, HoCharacterSpecializationDebugViewInfo.Views);
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

                views.Add(new HoDebugViewInfo("DebugGrid", "debug-grid.overlay", "Labels", 0,
                    "Hidden/lilToon/URP/Debug/GridOverlay",
                    "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Debug/Shaders/HoDebugGridOverlay.shader", true, "Grid labels unavailable."));
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
