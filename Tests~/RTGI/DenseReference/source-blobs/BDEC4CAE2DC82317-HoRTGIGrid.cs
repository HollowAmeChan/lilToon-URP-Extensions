using System;
using UnityEngine;

namespace lilToon.URP.Extensions.RTGI
{
    // One fixed full-resolution representative per regular GI pixel. Input,
    // history and denoiser projection must all describe this same sample lattice.
    internal readonly struct HoRTGIGrid
    {
        internal readonly int inputWidth,inputHeight,width,height,scale;
        internal Vector4 Parameters=>new(inputWidth,inputHeight,scale,(scale-1)*.5f);
        internal HoRTGIGrid(int inputWidth,int inputHeight,bool halfRequested)
        {
            if(inputWidth<1||inputHeight<1)throw new ArgumentOutOfRangeException(nameof(inputWidth));
            this.inputWidth=inputWidth;this.inputHeight=inputHeight;
            // An odd last block has a different sample spacing. Keep the regular
            // full lattice until a separately verified irregular edge exists.
            scale=halfRequested&&inputWidth>=2&&inputHeight>=2&&(inputWidth&1)==0&&(inputHeight&1)==0?2:1;
            width=inputWidth/scale;height=inputHeight/scale;
        }
        internal bool CanReuseHistory(HoRTGIGrid previous)=>inputWidth==previous.inputWidth&&inputHeight==previous.inputHeight&&scale==previous.scale;
        internal Matrix4x4 ProjectionForGrid(Matrix4x4 projection)
        {
            // Unity's logical projection uses upward Y. GPU RT Y conversion
            // and ComputeWorldSpacePosition's UV convention cancel together.
            var row=projection.GetRow(3);float offset=(scale-1)*.5f;
            projection.SetRow(0,projection.GetRow(0)-row*(2*offset/inputWidth));
            projection.SetRow(1,projection.GetRow(1)-row*(2*offset/inputHeight));
            return projection;
        }
    }
}
