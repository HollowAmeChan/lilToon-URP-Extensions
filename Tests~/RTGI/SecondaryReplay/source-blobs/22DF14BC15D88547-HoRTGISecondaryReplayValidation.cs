namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGICastleValidation
    {
        public static void RunSecondaryReplayQualification()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:4,secondaryReplay:true,validationTag:"secondary-replay-qualification");
        public static void RunSecondaryReplay()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:16,secondaryReplay:true,validationTag:"secondary-replay");
        public static void RunSecondaryReplayCompatibility()=>Run(true,64,denseScheduled:true,lineageAudit:true,lineageFrames:16,secondaryReplay:true,validationTag:"secondary-replay");
    }
}
