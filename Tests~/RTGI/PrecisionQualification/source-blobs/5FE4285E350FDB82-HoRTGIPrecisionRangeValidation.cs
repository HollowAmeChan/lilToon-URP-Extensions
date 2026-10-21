using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.RTDI.NRD;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.RTGI.NRD;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGINrdValidation
    {
        [Serializable]sealed class PrecisionRange{public float amplitude;public Color raw,filtered;public float slowMomentMax,slowMomentMean;public bool firstFrameReset;}
        [Serializable]sealed class PoolState{public string label;public int width,height,rgba32Count,rgba16Count;public long colorPoolBytes;}
        static readonly List<PrecisionRange> precisionRanges=new();static readonly List<PoolState> precisionPoolStates=new();
        const BindingFlags PrecisionFlags=BindingFlags.Instance|BindingFlags.NonPublic;
        public static void RunPrecision(){precisionAudit=true;Run();}
        public static void RunPrecisionCompatibility(){precisionAudit=true;compatibility=true;Run();}
        static RTHandle[] Pools(HoRTDINrdContext core,string name)=>(RTHandle[])typeof(HoRTDINrdContext).GetField(name,PrecisionFlags).GetValue(core);
        static void CheckPrecisionPools(HoRTGINrdContext context,string label)
        {
            var state=DescribePools(context.core,label);
            Require(state.rgba32Count==4&&state.rgba16Count==0,"GI-only four color pools are actually RGBA32F: "+label);
            Require(context.AppliedSignalScale==64&&context.signal.rt.graphicsFormat==GraphicsFormat.R16G16B16A16_SFloat&&context.output.rt.graphicsFormat==GraphicsFormat.R16G16B16A16_SFloat,"Precision policy retains units and external formats: "+label);
            precisionPoolStates.Add(state);
        }
        static PoolState DescribePools(HoRTDINrdContext core,string label)
        {
            var state=new PoolState{label=label};
            foreach(var texture in Pools(core,"permanent").Concat(Pools(core,"transient")))
            {
                if(texture.rt.graphicsFormat==GraphicsFormat.R32G32B32A32_SFloat){state.rgba32Count++;state.colorPoolBytes+=(long)texture.rt.width*texture.rt.height*16;}
                if(texture.rt.graphicsFormat==GraphicsFormat.R16G16B16A16_SFloat){state.rgba16Count++;state.colorPoolBytes+=(long)texture.rt.width*texture.rt.height*8;}
                if(texture.rt.graphicsFormat==GraphicsFormat.R32G32B32A32_SFloat||texture.rt.graphicsFormat==GraphicsFormat.R16G16B16A16_SFloat){state.width=texture.rt.width;state.height=texture.rt.height;}
            }
            return state;
        }
        static void ValidatePrecisionRangeAndLifecycle()
        {
            var c=gi.LastContext;CheckPrecisionPools(c.nrd,"initial production");
            var library=(ComputeShader[])typeof(HoRTGIRendererFeature).GetField("nrdShaders",PrecisionFlags).GetValue(gi);
            using(var defaultCore=new HoRTDINrdContext(library,HoRTGINrdManifest.DenoiserType,HoRTGINrdManifest.Identifiers,HoRTGINrdManifest.Resources))
            {
                defaultCore.Prepare(Size,Size);var control=DescribePools(defaultCore,"default constructor control");
                Require(control.rgba16Count==4&&control.rgba32Count==0,"Shared default constructor retains four half color pools");precisionPoolStates.Add(control);
            }
            gi.Settings.useEnvironment=false;gi.Settings.temporal=true;gi.Settings.spatial=false;
            bounceMaterial.SetColor("_Color",Color.black);bounceMaterial.SetFloat("_UseEmission",1);bounceMaterial.SetFloat("_EmissionBlend",1);bounceMaterial.SetFloat("_EmissionMainStrength",0);
            bounceMaterial.SetTexture("_EmissionMap",Texture2D.whiteTexture);bounceMaterial.SetTexture("_EmissionBlendMask",Texture2D.whiteTexture);
            foreach(float amplitude in new[]{.0001f,1f,20000f})
            {
                bounceMaterial.SetColor("_EmissionColor",new Color(amplitude,amplitude,amplitude,1));Render();bool reset=c.nrd.lastReset;
                Require(reset,"Precision range transition resets the first affected frame: "+amplitude);
                Sample(48,32,out var raw,out var filtered,out _,out _);var expected=new Color(amplitude,amplitude,amplitude,1);var range=new PrecisionRange{amplitude=amplitude,raw=raw,filtered=filtered,firstFrameReset=reset};precisionRanges.Add(range);
                Near(raw,expected,"Precision raw emissive integral: "+amplitude,.05f,amplitude*.0001f);
                Near(filtered,raw,"Precision filtered HDR/low-signal mean: "+amplitude,.05f,amplitude*.0001f);
                var texture=Pools(c.nrd.core,"permanent")[0].rt;var request=AsyncGPUReadback.Request(texture);request.WaitForCompletion();if(request.hasError)throw new Exception("Slow HDR moment readback failed");
                var values=request.GetData<Vector4>();float maximum=0,mean=0;
                for(int y=16;y<48;y++)for(int x=16;x<48;x++){float value=values[y*Size+x].w;if(!float.IsFinite(value)||value<0)throw new Exception("Non-finite/negative high-precision slow moment");maximum=Mathf.Max(maximum,value);mean+=value/1024;}
                Require(mean>0,"Low/HDR slow second moment is retained: "+amplitude);
                if(amplitude==20000)Require(maximum>65504,"Actual slow HDR second moment exceeds half range without overflow");
                range.slowMomentMax=maximum;range.slowMomentMean=mean;
            }
            bounceMaterial.SetFloat("_UseEmission",0);bounceMaterial.SetColor("_Color",Color.white);gi.Settings.useEnvironment=true;Render();
            var oldTarget=target;
            foreach(int size in new[]{96,64})
            {
                target=Keep(new RenderTexture(size,size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();Render();
                Require(c.nrd.lastReset&&c.nrd.signal.rt.width==size,"Precision resize resets history: "+size);CheckPrecisionPools(c.nrd,"resize"+size);
            }
            target=oldTarget;Render();
            var old=c.nrd;var owned=Pools(old.core,"permanent").Concat(Pools(old.core,"transient")).ToArray();
            gi.Settings.denoise=false;Render();Require(c.nrd==null&&owned.All(h=>h.rt==null),"Disabling releases every high-precision pool handle");
            gi.Settings.denoise=true;Render();Require(c.nrd!=null&&!ReferenceEquals(old,c.nrd)&&c.nrd.lastReset,"Re-enable creates a fresh precision instance");CheckPrecisionPools(c.nrd,"re-enabled");
            gi.Settings.temporal=gi.Settings.spatial=false;Render();
        }
    }
}
