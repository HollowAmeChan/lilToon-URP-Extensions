using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool environmentDirectionAudit;
        private static readonly List<EnvironmentDirectionCase> environmentDirectionCases=new();
        public static void RunEnvironmentDirections(){environmentDirectionAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunEnvironmentDirectionsCompatibility(){compatibility=true;RunEnvironmentDirections();}
        [Serializable] private sealed class EnvironmentDirectionReport{public bool diagnosticOnly=true;public string path;public int bins=10;public EnvironmentDirectionCase[] cases;}
        [Serializable] private sealed class EnvironmentDirectionCase
        {
            public bool mixed,reuse;public int frames,probes,environmentSamples,analyticSamples;public double maxGreenReconstructionError,maxUniformPdfError;
            public int[] counts=new int[10];public double[] greenSums=new double[10],greenSquares=new double[10],brdfPdfSums=new double[10];
        }
        private sealed class EnvironmentDirectionCapture
        {
            private readonly HoRTDIContext context;private readonly PairedPackedReservoir[] block=new PairedPackedReservoir[512];private readonly float[] pdf;
            private readonly EnvironmentDirectionCase result;
            internal EnvironmentDirectionCapture(HoRTDIContext context,bool mixed,bool reuse)
            {
                this.context=context;result=new EnvironmentDirectionCase{mixed=mixed,reuse=reuse};Require(context.width==128&&context.height==128&&camera.orthographic,"Direction audit requires 128-square orthographic fixture");
                int size=context.environmentPdf.rt.width;Require(size>0,"Direction audit lacks environment PDF");var image=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));ReadImage(context.environmentPdf.rt,image);
                pdf=new float[size*size];double sum=0;for(int y=0;y<size;y++)for(int x=0;x<size;x++){float value=image.GetPixel(x,y).r;Require(float.IsFinite(value)&&value>=0,"Environment PDF invalid");pdf[y*size+x]=value;sum+=value;}
                Require(sum>0,"Environment PDF empty");for(int i=0;i<pdf.Length;i++){double directional=pdf[i]/sum*size*size/(4*Math.PI);result.maxUniformPdfError=Math.Max(result.maxUniformPdfError,Math.Abs(directional*4*Math.PI-1));}
            }
            internal void Observe(Texture2D raw)
            {
                result.frames++;int address=context.arrayPitch+4*context.rowPitch+3*256;context.reservoirs.GetData(block,0,address,block.Length);
                foreach(int x in new[]{52,60,68,76})
                {
                    result.probes++;var sample=block[(x/16-3)*256+x%16];Require((sample.lightData&0x80000000)!=0,"Direction audit sample invalid");
                    if((sample.lightData&0x7fffffff)!=0x7ffffffe){result.analyticSamples++;continue;}
                    result.environmentSamples++;float u=(sample.uvData&65535)/65535f,v=(sample.uvData>>16)/65535f;
                    double worldY=1-2*v,phi=2*Math.PI*u,r=Math.Sqrt(Math.Max(0,1-worldY*worldY));var direction=new Vector3((float)(r*Math.Cos(phi)),(float)worldY,(float)(r*Math.Sin(phi)));
                    double mu=Math.Max(0,Math.Min(1,-direction.z));int bin=Math.Min(9,(int)(mu*10));result.counts[bin]++;
                    float response=ReferenceGGX(Vector3.back,-camera.transform.forward,direction,102f/255,new Color(10f/255,10f/255,10f/255,1)).r;
                    double green=response*sample.weight/(context.environmentPdf.rt.width*context.environmentPdf.rt.width/(4*Math.PI))*(((sample.mVisibility&63)!=0)?1:0),actual=raw.GetPixel(x,64).g;
                    double error=Math.Abs(green-actual)/(Math.Max(Math.Abs(green),Math.Abs(actual))+.001);Require(double.IsFinite(error)&&error<.003,"Environment direction/weight specular reconstruction mismatch");result.maxGreenReconstructionError=Math.Max(result.maxGreenReconstructionError,error);
                    result.greenSums[bin]+=actual;result.greenSquares[bin]+=actual*actual;
                    double a2=Math.Pow(102.0/255,4),nh2=(1+mu)*.5,den=nh2*a2+1-nh2,D=a2/(Math.PI*den*den);result.brdfPdfSums[bin]+=vndfProposalAudit?D/4:.5*(mu/Math.PI+D/4);
                }
            }
            internal void Finish(){Require(result.frames==512&&result.probes==2048,"Direction audit count mismatch");environmentDirectionCases.Add(result);}
        }
        private static void WriteEnvironmentDirections()=>File.WriteAllText(Path.Combine(Application.dataPath,"../"+(vndfProposalAudit?"rtdi-envdirection103-vndf-":"rtdi-envdirection102-")+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new EnvironmentDirectionReport{path=compatibility?"compatibility":"RenderGraph",cases=environmentDirectionCases.ToArray()},true));
    }
}
