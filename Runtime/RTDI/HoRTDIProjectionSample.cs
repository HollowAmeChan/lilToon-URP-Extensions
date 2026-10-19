using UnityEngine;

namespace lilToon.URP.Extensions.RTDI
{
    // URP Base/Mono starts with camera.projectionMatrix and applies TAA as a
    // clip-space translation. Keep real lens changes; separate only a verified
    // subpixel translation. DI/primary reconstruction still use the actual P.
    internal readonly struct HoRTDIProjectionSample
    {
        internal readonly Matrix4x4 nonJittered;
        internal readonly Vector2 jitterPixels;
        internal HoRTDIProjectionSample(Matrix4x4 actual,Matrix4x4 cameraProjection,int width,int height)
        {
            nonJittered=actual;jitterPixels=Vector2.zero;
            if(width<=0||height<=0)return;
            var delta=actual*cameraProjection.inverse;
            var shift=Matrix4x4.Translate(new Vector3(delta.m03,delta.m13,0));
            for(int i=0;i<16;i++)if(!float.IsFinite(delta[i])||Mathf.Abs(delta[i]-shift[i])>1e-5f)return;
            var pixels=new Vector2(-delta.m03*width*.5f,-delta.m13*height*.5f);
            // The pinned NRD cameraJitter contract is [-.5,.5] pixels.
            if(Mathf.Abs(pixels.x)>.501f||Mathf.Abs(pixels.y)>.501f)return;
            nonJittered=cameraProjection;jitterPixels=new Vector2(Mathf.Clamp(pixels.x,-.5f,.5f),Mathf.Clamp(pixels.y,-.5f,.5f));
        }
    }
}
