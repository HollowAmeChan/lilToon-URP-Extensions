using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceMotionProtocol;
        private static bool motionProtocolRealFrames;
        private static int motionProtocolSample;
        private static RenderTexture motionProtocolCopy;
        private static Texture2D motionProtocolPixels;
        private static bool motionProtocolRecorded;
        private static Material motionProtocolMaterial;
        public static void RunMotionProtocol(){forceMotionProtocol=true;RunCutoutQuality();}
        public static void RunMotionProtocolCompatibility(){compatibility=true;RunMotionProtocol();}
        public static void RunMotionProtocolRealFrames(){motionProtocolRealFrames=true;RunMotionProtocol();}
        public static void RunMotionProtocolRealFramesCompatibility(){compatibility=true;RunMotionProtocolRealFrames();}
        private static void TickMotionProtocol()
        {
            if(motionProtocolCopy==null)
            {
                skinBlockerBone.localPosition=Vector3.right*100;primaryReceiver.GetComponent<Renderer>().motionVectorGenerationMode=MotionVectorGenerationMode.Object;
                motionProtocolCopy=Keep(new RenderTexture(new RenderTextureDescriptor(64,64){graphicsFormat=GraphicsFormat.R16G16_SFloat,depthBufferBits=0,msaaSamples=1}));motionProtocolCopy.Create();
                motionProtocolPixels=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
                var shader=UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/RTDIPlayerProbe/HoRTDIPlayerCapture.shader");Require(shader!=null,"Prepare the isolated Player capture fixture before motion audit");
                motionProtocolMaterial=Keep(new Material(shader));
                feature.LastContext.nrd.ValidationMotionCapture=(cmd,texture)=>{if(compatibility)cmd.Blit(Texture2D.blackTexture,motionProtocolCopy,motionProtocolMaterial,4);else cmd.CopyTexture(texture,new RenderTargetIdentifier(motionProtocolCopy));motionProtocolRecorded=true;};
            }
            var positions=new[]{0f,0,0,.125f,.25f,.25f,0,0};
            if(motionProtocolSample==positions.Length)
            {feature.LastContext.nrd.ValidationMotionCapture=null;Debug.Log("[HoRTDIValidation] PASS real-frame motion source protocol; compatibility="+compatibility);Finish(null);return;}
            float position=positions[motionProtocolSample],previous=motionProtocolSample>0?positions[motionProtocolSample-1]:0;
            primaryReceiver.transform.position=Vector3.right*position;motionProtocolRecorded=false;RenderCamera();Require(motionProtocolRecorded,"real-frame motion source unavailable");
            ReadImage(motionProtocolCopy,motionProtocolPixels);var value=motionProtocolPixels.GetPixel(32,32);float expected=(position-previous)*.5f;
            Debug.Log($"[HoRTDIValidation] MOTION_REAL frame={Time.frameCount} sample={motionProtocolSample} objectX={position:F6} motionUV=({value.r:F6},{value.g:F6}) expectedX={expected:F6} compatibility={compatibility}");
            if(motionProtocolSample>=3){Near(value.r,expected,"real-frame object UV motion",.0002f);Near(value.g,0,"real-frame object vertical motion",.0002f);}
            motionProtocolSample++;
        }
        private static void ValidateMotionProtocol()
        {
            skinBlockerBone.localPosition=Vector3.right*100;primaryReceiver.GetComponent<Renderer>().motionVectorGenerationMode=MotionVectorGenerationMode.Object;
            var desc=new RenderTextureDescriptor(64,64){graphicsFormat=GraphicsFormat.R16G16_SFloat,depthBufferBits=0,msaaSamples=1};
            var copy=Keep(new RenderTexture(desc));copy.Create();var pixels=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            RenderCamera();var c=feature.LastContext;bool recorded=false;
            var shader=UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/RTDIPlayerProbe/HoRTDIPlayerCapture.shader");Require(shader!=null,"Prepare the isolated Player capture fixture before motion audit");
            var material=Keep(new Material(shader));int sourceFrame=Time.frameCount;
            c.nrd.ValidationMotionCapture=(cmd,texture)=>{if(compatibility)cmd.Blit(Texture2D.blackTexture,copy,material,4);else cmd.CopyTexture(texture,new RenderTargetIdentifier(copy));recorded=true;};
            try
            {
                foreach(float position in new[]{0,.125f,.25f,0})
                {
                    recorded=false;primaryReceiver.transform.position=Vector3.right*position;RenderCamera();Require(recorded,"motion source unavailable in current renderer path");
                    ReadImage(copy,pixels);var value=pixels.GetPixel(32,32);
                    Require(Time.frameCount==sourceFrame,"same-frame protocol advanced the engine frame");Near(value.r,position*.5f,"same-frame accumulated object UV motion",.0002f);Near(value.g,0,"same-frame object vertical motion",.0002f);
                    Debug.Log($"[HoRTDIValidation] MOTION_PROTOCOL frame={Time.frameCount} objectX={position:F6} motionUV=({value.r:F6},{value.g:F6}) compatibility={compatibility}");
                }
            }
            finally{c.nrd.ValidationMotionCapture=null;}
        }
    }
}
