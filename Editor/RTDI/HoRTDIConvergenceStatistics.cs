using System;

// Shared by the package probe and the isolated upstream-project probe.
// Each component is a fixed pixel/channel; spatial texture variation is not noise.
public sealed class HoRTDIConvergenceStatistics
{
    private readonly double[] mean,m2,previous;
    private double differenceSquares,differenceAbsolute;
    public int Frames {get;private set;}
    public HoRTDIConvergenceStatistics(int components){mean=new double[components];m2=new double[components];previous=new double[components];}
    public void Add(double[] values)
    {
        if(values.Length!=mean.Length)throw new ArgumentException("Convergence component count changed");
        Frames++;
        for(int i=0;i<values.Length;i++)
        {
            double value=values[i];if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentException("Non-finite convergence signal");
            double delta=value-mean[i];mean[i]+=delta/Frames;m2[i]+=delta*(value-mean[i]);
            if(Frames>1){double difference=value-previous[i];differenceSquares+=difference*difference;differenceAbsolute+=Math.Abs(difference);}
            previous[i]=value;
        }
    }
    public Result Summarize(string signal,int firstFrame,int lastFrame)
    {
        double energy=0,variance=0,total=0;
        for(int i=0;i<mean.Length;i++){energy+=mean[i]*mean[i];variance+=m2[i];total+=mean[i];}
        return new Result{signal=signal,firstFrame=firstFrame,lastFrame=lastFrame,frames=Frames,components=mean.Length,
            mean=total/mean.Length,normalizedTemporalVariance=Frames>1?variance/(Frames-1)/Math.Max(1e-20,energy):0,
            normalizedFrameDifferenceMSE=Frames>1?differenceSquares/(Frames-1)/Math.Max(1e-20,energy):0,
            frameDifferenceL1=Frames>1?differenceAbsolute/(Frames-1)/mean.Length:0};
    }
    [Serializable] public sealed class Result
    {
        public string signal;
        public int firstFrame,lastFrame,frames,components;
        public double mean,normalizedTemporalVariance,normalizedFrameDifferenceMSE,frameDifferenceL1;
    }
}
