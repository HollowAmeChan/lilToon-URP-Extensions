using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace lilToon.URP.Extensions.RTDI.NRD
{
    // Pinned NRD 4.17.3 CPU scheduling ABI. No Unity native graphics plugin,
    // descriptor-heap hook, NRI device or DLSS integration is loaded here.
    internal static class HoRTDINrdNative
    {
        private const string Library="HoRTDINRD";
        [StructLayout(LayoutKind.Sequential)] internal struct LibraryDesc
        {
            internal uint samplerOffset,textureOffset,constantOffset,storageOffset;
            internal IntPtr denoisers;internal uint count;
            internal byte major,minor,build,normalEncoding,roughnessEncoding;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct DenoiserDesc {internal uint identifier,type;}
        [StructLayout(LayoutKind.Sequential)] private struct CreationDesc
        {internal IntPtr allocate,reallocate,free,user,denoisers;internal uint count;}
        [StructLayout(LayoutKind.Sequential)] internal struct TextureDesc {internal uint format;internal ushort downsample;}
        [StructLayout(LayoutKind.Sequential)] internal struct ResourceDesc {internal uint descriptorType,type;internal ushort poolIndex;}
        [StructLayout(LayoutKind.Sequential)] internal struct Bytecode {internal IntPtr pointer;internal ulong size;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)] internal struct PipelineDesc
        {
            internal Bytecode dxbc,dxil,spirv;internal IntPtr ranges;internal uint rangeCount;internal byte hasConstants;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] internal string identifier;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct InstanceDesc
        {
            internal uint constantSpace,resourceSpace,constantRegister,samplerRegister,resourceRegister,constantMax;
            internal IntPtr samplers;internal uint samplerCount;internal IntPtr entry,pipelines;internal uint pipelineCount;
            internal IntPtr permanent;internal uint permanentCount;internal IntPtr transient;internal uint transientCount;
            internal uint texturesMax,storageMax,totalTextures,totalStorage,setsMax;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct DispatchDesc
        {
            internal IntPtr name;internal uint identifier;internal IntPtr resources;internal uint resourceCount;
            internal IntPtr constants;internal uint constantSize;internal byte constantsMatchPrevious;
            internal ushort pipeline,gridWidth,gridHeight;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct CommonSettings
        {
            internal Matrix4x4 projection,previousProjection,view,previousView,worldPreviousToWorld;
            internal Vector3 motionScale;internal Vector2 jitter,previousJitter;
            internal uint resourceSize,previousResourceSize,rectSize,previousRectSize;
            internal float viewZScale,timeDelta,range,disocclusion,alternateDisocclusion,cameraMaterial,strandMaterial,historyFixMaterial,strandThickness,splitScreen;
            internal uint printfAt;internal float debug;internal uint rectOriginX,rectOriginY,frameIndex;
            internal byte accumulationMode,worldMotion,confidence,disocclusionMix,validation;
            internal static CommonSettings Create(int width,int height,Matrix4x4 view,Matrix4x4 projection,Matrix4x4 previousView,Matrix4x4 previousProjection,int frame,bool reset)
            {
                uint size=checked((uint)(ushort)width|((uint)(ushort)height<<16));
                return new CommonSettings
                {
                    projection=GL.GetGPUProjectionMatrix(projection,true),previousProjection=GL.GetGPUProjectionMatrix(previousProjection,true),
                    view=view,previousView=previousView,worldPreviousToWorld=Matrix4x4.identity,motionScale=Vector3.one,
                    resourceSize=size,previousResourceSize=size,rectSize=size,previousRectSize=size,viewZScale=1,timeDelta=0,
                    range=500000,disocclusion=.01f,alternateDisocclusion=.05f,cameraMaterial=999,strandMaterial=999,historyFixMaterial=999,
                    strandThickness=.00008f,printfAt=9999u|(9999u<<16),frameIndex=(uint)frame,accumulationMode=(byte)(reset?2:0),worldMotion=1
                };
            }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct RelaxSettings
        {
            internal float acceleration,spatialSigma,temporalSigma,reset;
            internal uint diffFrames,specFrames,diffFast,specFast,historyFixFrames,baseStride,alternateStride;
            internal float historyNormalPower,fastSigma,diffPrepass,specPrepass,minHitWeight;
            internal uint varianceHistory;
            internal float diffPhi,specPhi,lobeFraction,roughFraction,specVariance,specSlack;
            internal uint atrous;
            internal float diffMinLum,specMinLum,depthThreshold,confidenceMultiplier,confidenceLum,confidenceNormal,lumRelax,normalRelax,roughRelax;
            internal byte checkerboard,reconstruction;
            internal float minDiffMaterial,minSpecMaterial;internal byte antiFirefly,roughEdge;
            // Keep the established responsive diffuse profile during changes.
            // A settled scene can retain the reference-length diffuse history;
            // fast history, specular history and spatial settings stay unchanged.
            internal const float StableDiffusePeriod=.5f;
            internal static RelaxSettings DenseDI(float seconds=1f/60,bool stableDiffuse=false)
            {
                var result=new RelaxSettings
                {
                acceleration=.6f,spatialSigma=4.5f,temporalSigma=.5f,reset=1,
                historyFixFrames=0,baseStride=14,alternateStride=14,historyNormalPower=8,fastSigma=2,diffPrepass=0,specPrepass=0,
                minHitWeight=.1f,varianceHistory=0,diffPhi=2,specPhi=1,lobeFraction=.5f,roughFraction=.15f,specSlack=.15f,atrous=5,
                depthThreshold=.003f,lumRelax=.5f,normalRelax=.3f,roughRelax=1,minDiffMaterial=0,minSpecMaterial=0,roughEdge=1
                };
                if(!float.IsFinite(seconds)||seconds<=0)seconds=1f/60;
                // Pinned NRD GetMaxAccumulatedFrameNum: round(period * FPS).
                // Two temporal observations are the minimum for a variance
                // estimate. Dense DI avoids the SDK cold spatial color bootstrap,
                // which averages through depth and visibility discontinuities.
                result.diffFrames=Math.Max(1u,Frames(stableDiffuse?StableDiffusePeriod:.05f,seconds,255));result.specFrames=Frames(.5f,seconds,255);
                result.diffFast=Math.Min(Frames(1f/60,seconds,255),result.diffFrames>0?result.diffFrames-1:0);
                result.specFast=Math.Min(Frames(.1f,seconds,255),result.specFrames>0?result.specFrames-1:0);
                // Dense DI supplies both lobes every pixel. The sparse-history
                // reconstruction averages colors before luminance edge stopping.
                result.historyFixFrames=0;return result;
            }
            private static uint Frames(float period,float seconds,uint maximum)=>(uint)Math.Min(maximum,Math.Max(0,Math.Floor(period/seconds+.5)));
        }
        [DllImport(Library,CallingConvention=CallingConvention.StdCall)] private static extern IntPtr GetLibraryDesc();
        [DllImport(Library,CallingConvention=CallingConvention.StdCall)] private static extern uint CreateInstance(ref CreationDesc desc,out IntPtr instance);
        [DllImport(Library,CallingConvention=CallingConvention.StdCall)] internal static extern void DestroyInstance(IntPtr instance);
        [DllImport(Library,CallingConvention=CallingConvention.StdCall)] internal static extern IntPtr GetInstanceDesc(IntPtr instance);
        [DllImport(Library,CallingConvention=CallingConvention.StdCall)] internal static extern uint SetCommonSettings(IntPtr instance,ref CommonSettings settings);
        [DllImport(Library,CallingConvention=CallingConvention.StdCall)] internal static extern uint SetDenoiserSettings(IntPtr instance,uint identifier,ref RelaxSettings settings);
        [DllImport(Library,CallingConvention=CallingConvention.StdCall)] internal static extern uint GetComputeDispatches(IntPtr instance,[In]uint[] identifiers,uint count,out IntPtr dispatches,out uint dispatchCount);
        // This reference fork's exported string helper retains an older enum
        // order. Bind by the pinned header's authoritative numeric enum order.
        internal static string ResourceName(uint type)=>type<HoRTDINrdManifest.ResourceTypes.Length?HoRTDINrdManifest.ResourceTypes[type]:throw new InvalidOperationException("NRD resource enum out of range");
        internal static T Read<T>(IntPtr pointer,int index=0)=>Marshal.PtrToStructure<T>(IntPtr.Add(pointer,index*Marshal.SizeOf<T>()));
        internal static void Check(uint result,string operation){if(result!=0)throw new InvalidOperationException($"NRD {operation}: result {result}");}
        internal static IntPtr Create()
        {
            if(Marshal.SizeOf<LibraryDesc>()!=40||Marshal.SizeOf<CreationDesc>()!=48||Marshal.SizeOf<PipelineDesc>()!=320||Marshal.SizeOf<InstanceDesc>()!=112||Marshal.SizeOf<CommonSettings>()!=432)
                throw new InvalidOperationException("NRD 4.17.3 managed ABI size mismatch");
            var lib=Read<LibraryDesc>(GetLibraryDesc());
            if(lib.major!=4||lib.minor!=17||lib.build!=3||lib.normalEncoding!=2||lib.roughnessEncoding!=1)
                throw new NotSupportedException("Expected pinned NRD 4.17.3, normal encoding 2 / roughness encoding 1");
            IntPtr method=Marshal.AllocHGlobal(8);
            try
            {
                Marshal.StructureToPtr(new DenoiserDesc{identifier=0,type=14},method,false);
                var creation=new CreationDesc{denoisers=method,count=1};Check(CreateInstance(ref creation,out var instance),"CreateInstance");
                try
                {
                    if(Marshal.SizeOf<RelaxSettings>()!=148)throw new InvalidOperationException("NRD RELAX settings ABI size mismatch");
                    // Dense DI supplies both lobes with a valid light distance.
                    // Avoid prepass blurring deterministic DI visibility and clean
                    // analytic highlights; retain SDK temporal and A-trous passes.
                    var settings=RelaxSettings.DenseDI();Check(SetDenoiserSettings(instance,0,ref settings),"SetDenoiserSettings");return instance;
                }
                catch{DestroyInstance(instance);throw;}
            }
            finally{Marshal.FreeHGlobal(method);}
        }
    }
}
