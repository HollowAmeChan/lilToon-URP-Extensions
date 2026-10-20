using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Only slow gOut_Diff of original RELAX stages 4/6 carries E[luma^2].
    // HistoryFix conditionally writes Temporal's fast pong and can early-out;
    // its alpha, PrePass hit distance and A-trous variance are excluded.
    internal sealed class HoRTGIMomentAudit
    {
        const double HalfMin=5.9604644775390625e-8;
        [Serializable]internal sealed class Bucket
        {
            public string label;
            public long samples,positiveMean,zeroMomentWithPositiveMean,momentBelowSquared,severeMomentBelowSquared,momentSaturated;
            public double luminanceSum,squaredMeanSum,secondMomentSum,inputLuminanceSum,maxLuminance,maxSecondMoment,minPositiveRatio=double.MaxValue;
        }
        [Serializable]internal sealed class Local
        {
            public int frame,x,y;public Vector3 inputPhysical,stagePhysical,rawPhysical,filteredPhysical;
            public double scaledLuminance,scaledMoment,physicalSquaredMean,physicalSecondMoment,physicalDeficit;
        }
        [Serializable]internal sealed class Stage
        {
            public int pipeline;public string binding="gOut_Diff slow RGB / second moment";public float signalScale;
            public Bucket total=new(){label="all"};
            public Bucket[] bins={new(){label="physical luma < 1e-4"},new(){label="1e-4 <= physical luma < 1e-3"},new(){label="1e-3 <= physical luma < 1e-2"},new(){label="1e-2 <= physical luma < .1"},new(){label="physical luma >= .1"}};
            public Local[] largestDeficits;internal readonly List<Local> locals=new();
        }
        readonly Dictionary<int,Stage> stages=new();
        internal void Add(int pipeline,Color[] values,Color[] input,Color[] raw,Color[] filtered,int width,int height,int frame,float scale)
        {
            if(pipeline!=4&&pipeline!=6)return;
            if(!stages.TryGetValue(pipeline,out var stage)){stage=new Stage{pipeline=pipeline,signalScale=scale};stages.Add(pipeline,stage);}
            if(stage.signalScale!=scale)throw new Exception("Mixed signal units in one moment audit");
            for(int y=0;y<height;y+=4)for(int x=0;x<width;x+=4)
            {
                int at=y*width+x;var v=values[at];double l=V(v),m=v.a,source=V(input[at]);
                if(!double.IsFinite(l)||!double.IsFinite(m)||l<0||m<0)throw new Exception("Nonfinite/negative slow diffuse moment");
                double physical=l*scale;int bin=physical<1e-4?0:physical<1e-3?1:physical<1e-2?2:physical<.1?3:4;
                Accumulate(stage.total);Accumulate(stage.bins[bin]);
                double deficit=Math.Max(0,l*l-m)*scale*scale;
                if(deficit>0&&(stage.locals.Count<12||deficit>stage.locals[^1].physicalDeficit))
                {
                    stage.locals.Add(new Local{frame=frame,x=x,y=y,inputPhysical=(Vector3)(Vector4)input[at]*scale,stagePhysical=(Vector3)(Vector4)v*scale,rawPhysical=(Vector3)(Vector4)raw[at],filteredPhysical=(Vector3)(Vector4)filtered[at],scaledLuminance=l,scaledMoment=m,physicalSquaredMean=l*l*scale*scale,physicalSecondMoment=m*scale*scale,physicalDeficit=deficit});
                    stage.locals.Sort((a,b)=>b.physicalDeficit.CompareTo(a.physicalDeficit));if(stage.locals.Count>12)stage.locals.RemoveAt(12);
                }
                void Accumulate(Bucket bucket)
                {
                    bucket.samples++;bucket.luminanceSum+=physical;bucket.squaredMeanSum+=l*l*scale*scale;bucket.secondMomentSum+=m*scale*scale;bucket.inputLuminanceSum+=source*scale;
                    bucket.maxLuminance=Math.Max(bucket.maxLuminance,physical);bucket.maxSecondMoment=Math.Max(bucket.maxSecondMoment,m*scale*scale);
                    if(m>=65504)bucket.momentSaturated++;
                    if(l>0)
                    {
                        bucket.positiveMean++;if(m==0)bucket.zeroMomentWithPositiveMean++;
                        if(m<l*l)bucket.momentBelowSquared++;
                        if(m+HalfMin*.5<.99*l*l)bucket.severeMomentBelowSquared++;
                        bucket.minPositiveRatio=Math.Min(bucket.minPositiveRatio,m/(l*l));
                    }
                }
            }
        }
        internal Stage[] Finish()
        {
            foreach(var stage in stages.Values){stage.largestDeficits=stage.locals.ToArray();foreach(var b in stage.bins.Append(stage.total))if(b.positiveMean==0)b.minPositiveRatio=-1;}
            return stages.Values.OrderBy(x=>x.pipeline).ToArray();
        }
        static double V(Color v)=>v.r*.2126+v.g*.7152+v.b*.0722;
    }
}
