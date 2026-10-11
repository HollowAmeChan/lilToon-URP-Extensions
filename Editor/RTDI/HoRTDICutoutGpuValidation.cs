using UnityEngine;
using lilToon.URP.Extensions.RTDI;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCutoutGpuTiming;
        private static int gpuTimingFrame;
        private static double gpuTimingSum;
        private static int gpuTimingCount;
        private static int gpuTimingResolution;
        public static void RunCutoutGpuTiming(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use isolated project");forceCutoutGpuTiming=true;RunCutout();}
        private static void SetupCutoutGpuTiming()
        {
            target.Release();Object.DestroyImmediate(target);target=Keep(new RenderTexture(1280,720,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
            feature.Settings.temporal=feature.Settings.spatial=false;feature.Settings.denoise=false;feature.Settings.initialSamples=1;
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU,true);
            HoRTDIRendererFeature.FinalVisibilityProfiling.enableRecording=true;
        }
        private static void TickCutoutGpuTiming()
        {
            var sampler=HoRTDIRendererFeature.FinalVisibilityProfiling;
            if(gpuTimingFrame>=16&&sampler.gpuSampleCount>0&&sampler.gpuElapsedTime>0)
            {gpuTimingSum+=sampler.gpuElapsedTime/sampler.gpuSampleCount;gpuTimingCount++;}
            RenderCamera();
            if(++gpuTimingFrame<1200&&gpuTimingCount<32)return;
            Debug.Log($"[HoRTDIValidation] CUTOUT_GPU size={target.width}x{target.height} frames={gpuTimingFrame} receivedFrames={gpuTimingCount} sufficient={gpuTimingCount>=32} finalVisibilityMs={(gpuTimingCount>=32?gpuTimingSum/gpuTimingCount:-1):F6}; insufficient=-1, simple one-caster fixture");
            if(gpuTimingResolution++==0)
            {
                target.Release();Object.DestroyImmediate(target);target=Keep(new RenderTexture(1920,1080,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
                gpuTimingFrame=gpuTimingCount=0;gpuTimingSum=0;return;
            }
            sampler.enableRecording=false;Finish(null);
        }
    }
}
