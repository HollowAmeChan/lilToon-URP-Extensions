using System;

namespace lilToon.URP.Extensions.RTDI.NRD
{
    // Camera sample time, in unscaled seconds. Extra requests in the same
    // Unity frame add samples without pretending the application FPS increased.
    internal sealed class HoRTDINrdTiming
    {
        private bool valid;
        private double previous;
        internal float Seconds {get;private set;}=1f/60;
        internal float FilteredSeconds {get;private set;}=1f/60;
        internal bool Advance(double time)
        {
            if(double.IsNaN(time)||double.IsInfinity(time))
            {valid=false;Seconds=FilteredSeconds=1f/60;return true;}
            if(!valid){previous=time;valid=true;return false;}
            double delta=time-previous;
            if(delta==0)return false;
            previous=time;
            if(delta<0||delta>1){Seconds=FilteredSeconds=1f/60;return true;}
            Seconds=(float)delta;
            float alpha=1-(float)Math.Exp(-delta/.1);
            FilteredSeconds+=alpha*(Seconds-FilteredSeconds);return false;
        }
    }
}
