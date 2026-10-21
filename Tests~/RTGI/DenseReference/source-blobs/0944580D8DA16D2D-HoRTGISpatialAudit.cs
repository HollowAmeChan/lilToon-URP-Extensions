using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Full-domain CPU accounting of already-read GPU data. This never changes
    // sampling, publication or the original stride-(0,0) energy gate.
    internal sealed class HoRTGISpatialAudit
    {
        [Serializable]internal sealed class Domain
        {
            public int phaseX,phaseY,pixels;public long samples;public Vector3 raw,filtered,relativeErrorPercent;
            public double normalizedL1,rawVariance,filteredVariance;public bool energyGatePass;
        }
        [Serializable]internal sealed class Stage
        {
            public int pipeline;public string meaning;public Vector3 full,phaseZero;public long negativeComponents;
        }
        [Serializable]internal sealed class Local
        {
            public int x,y;public Vector3 raw,filtered;public double luminanceDeficit,rawVariance,filteredVariance;public LocalStage[] stages;
        }
        [Serializable]internal sealed class LocalStage{public int pipeline;public string meaning;public Vector3 rgb;}
        [Serializable]internal sealed class Frame{public int index;public Vector3 raw,filtered;}
        [Serializable]internal sealed class Report
        {
            public bool complete;public int width,height,frames;public Domain full;public Domain[] phases;public Stage[] stages;public Frame[] frameMeans;public Local[] largestLosses,largestGains;
        }
        sealed class StageData{internal double[] sums;internal int frames;internal long negative;}
        readonly int width,height,n;int frames;
        readonly double[] raw,filtered,raw2,filtered2;
        readonly Dictionary<int,StageData> stages=new();readonly List<Frame> frameMeans=new();
        internal HoRTGISpatialAudit(int width,int height)
        {
            this.width=width;this.height=height;n=width*height;
            raw=new double[n*3];filtered=new double[n*3];raw2=new double[n*3];filtered2=new double[n*3];
        }
        internal void AddPublication(Color[] a,Color[] b,int frame)
        {
            if(frame!=frames||a.Length!=n||b.Length!=n)throw new Exception("Nonsequential full-domain samples");
            var sumA=new double[3];var sumB=new double[3];
            for(int i=0;i<n;i++)
            {
                if(a[i].a<.5f||b[i].a<.5f)throw new Exception("Full-domain receiver validity changed");
                for(int c=0;c<3;c++)
                {
                    double x=a[i][c],y=b[i][c];if(!double.IsFinite(x)||!double.IsFinite(y)||x<0||y<0)throw new Exception("Invalid full-domain radiance");int at=i*3+c;
                    raw[at]+=x;filtered[at]+=y;raw2[at]+=x*x;filtered2[at]+=y*y;sumA[c]+=x;sumB[c]+=y;
                }
            }
            frameMeans.Add(new Frame{index=frame,raw=V(sumA,n),filtered=V(sumB,n)});frames++;
        }
        internal void AddStage(int pipeline,Color[] values,HoRTGICastleValidation.Primary[] guides,float scale)
        {
            if(!stages.TryGetValue(pipeline,out var stage)){stage=new StageData{sums=new double[n*3]};stages.Add(pipeline,stage);}stage.frames++;
            for(int i=0;i<n;i++)for(int c=0;c<3;c++)
            {
                double value=values[i][c]*scale*((guides[i].diffuse>>(8*c))&255)/255.0;
                if(!double.IsFinite(value))throw new Exception("Nonfinite stage RGB");if(value<0)stage.negative++;
                stage.sums[i*3+c]+=value;
            }
        }
        internal Report Finish()
        {
            if(frames<2||stages.Values.Any(x=>x.frames!=frames))throw new Exception("Incomplete spatial stage window");
            var phases=new Domain[16];for(int y=0;y<4;y++)for(int x=0;x<4;x++)phases[y*4+x]=DomainFor(x,y);
            var losses=new List<Local>();var gains=new List<Local>();
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int at=(y*width+x)*3;var a=Pixel(raw,at,frames);var b=Pixel(filtered,at,frames);double deficit=(a.x-b.x)*.2126+(a.y-b.y)*.7152+(a.z-b.z)*.0722;
                var list=deficit>0?losses:gains;double magnitude=Math.Abs(deficit);
                if(magnitude==0||(list.Count>=12&&magnitude<=Math.Abs(list[^1].luminanceDeficit)))continue;
                var local=new Local{x=x,y=y,raw=a,filtered=b,luminanceDeficit=deficit,rawVariance=Variance(raw,raw2,at),filteredVariance=Variance(filtered,filtered2,at),stages=stages.OrderBy(p=>p.Key).Select(p=>new LocalStage{pipeline=p.Key,meaning=Meaning(p.Key),rgb=Pixel(p.Value.sums,at,frames)}).ToArray()};
                list.Add(local);list.Sort((u,v)=>Math.Abs(v.luminanceDeficit).CompareTo(Math.Abs(u.luminanceDeficit)));if(list.Count>12)list.RemoveAt(12);
            }
            return new Report{complete=true,width=width,height=height,frames=frames,full=DomainFor(-1,-1),phases=phases,stages=stages.OrderBy(p=>p.Key).Select(p=>new Stage{pipeline=p.Key,meaning=Meaning(p.Key),full=StageMean(p.Value.sums,-1,-1),phaseZero=StageMean(p.Value.sums,0,0),negativeComponents=p.Value.negative}).ToArray(),frameMeans=frameMeans.ToArray(),largestLosses=losses.ToArray(),largestGains=gains.ToArray()};
        }
        Domain DomainFor(int phaseX,int phaseY)
        {
            var a=new double[3];var b=new double[3];int count=0;double l1=0,reference=0,varA=0,varB=0;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                if(phaseX>=0&&((x&3)!=phaseX||(y&3)!=phaseY))continue;int at=(y*width+x)*3;count++;
                for(int c=0;c<3;c++){double p=raw[at+c]/frames,q=filtered[at+c]/frames;a[c]+=p;b[c]+=q;l1+=Math.Abs(p-q);reference+=Math.Abs(p);}
                varA+=Variance(raw,raw2,at);varB+=Variance(filtered,filtered2,at);
            }
            bool gate=true;for(int c=0;c<3;c++){a[c]/=count;b[c]/=count;gate&=Math.Abs(b[c]-a[c])<Math.Max(.000005,Math.Abs(a[c])*.05);}
            return new Domain{phaseX=phaseX,phaseY=phaseY,pixels=count,samples=(long)count*frames,raw=V(a,1),filtered=V(b,1),relativeErrorPercent=new Vector3((float)(100*(b[0]/a[0]-1)),(float)(100*(b[1]/a[1]-1)),(float)(100*(b[2]/a[2]-1))),energyGatePass=gate,normalizedL1=l1/Math.Max(reference,1e-30),rawVariance=varA/count,filteredVariance=varB/count};
        }
        Vector3 StageMean(double[] data,int phaseX,int phaseY)
        {
            var sum=new double[3];int count=0;for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {if(phaseX>=0&&((x&3)!=phaseX||(y&3)!=phaseY))continue;int at=(y*width+x)*3;count++;for(int c=0;c<3;c++)sum[c]+=data[at+c];}
            return V(sum,count*frames);
        }
        double Variance(double[] sums,double[] squares,int at)
        {double result=0;for(int c=0;c<3;c++)result+=Math.Max(0,(squares[at+c]-sums[at+c]*sums[at+c]/frames)/(frames-1))/3;return result;}
        static Vector3 Pixel(double[] data,int at,int count)=>new((float)(data[at]/count),(float)(data[at+1]/count),(float)(data[at+2]/count));
        static Vector3 V(double[] data,int count)=>new((float)(data[0]/count),(float)(data[1]/count),(float)(data[2]/count));
        static string Meaning(int pipeline)=>pipeline switch{-1=>"NRD input remodulated",3=>"PrePass RGB",4=>"Temporal slow RGB",5=>"Conditional HistoryFix / fast RGB",6=>"Clamping slow RGB",9=>"First Atrous RGB",10=>"Last Atrous RGB",_=>"Other RGB"};
    }
}
