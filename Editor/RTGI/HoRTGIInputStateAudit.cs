using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.RTGI;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal static class HoRTGIInputStateAudit
    {
        [Serializable]internal sealed class State
        {
            public int shaderFrame,applicationFrame,lightingSignature,lightCount,instances,cutouts;public uint sceneRevision,skyRevision;
            public Vector4 skyParameters,skyTint,skyConstant;public bool temporal,environment,alphaVisibility;
            public string lightsSha256,risSha256,secondarySha256,primarySha256;
            public Vector4[] lights;public Vector2Int[] ris;
            public HoRTGISeedAudit.Secondary[] selectedSecondary;public uint[] selectedPrimary;
            public Vector4[] selectedPrimaryProbe;
        }
        internal static State Capture(HoRTGIContext context,HoRTGIDenseReferenceFeature feature)
        {
            if(Marshal.SizeOf<HoRTDIRendererFeature.AnalyticLight>()!=64||Marshal.SizeOf<HoRTGISeedAudit.Secondary>()!=48||Marshal.SizeOf<HoRTGICastleValidation.Primary>()!=20)throw new Exception("Input-state buffer ABI drift");
            var lights=new HoRTDIRendererFeature.AnalyticLight[feature.lastLightCount];context.lights.GetData(lights,0,0,lights.Length);
            var ris=new Vector2Int[context.environmentRis.count];context.environmentRis.GetData(ris);
            var secondary=new HoRTGISeedAudit.Secondary[context.secondary.count];context.secondary.GetData(secondary);
            var primary=new HoRTGICastleValidation.Primary[context.history.count];context.history.GetData(primary);
            var probes=new HoRTGIDenseReferenceFeature.PrimaryProbe[feature.pointCount];feature.probe.GetData(probes);
            var at=HoRTGIAtrousTrace.Pixels.Select(p=>p.y*context.width+p.x).ToArray();
            return new State{shaderFrame=context.frame-1,applicationFrame=Time.frameCount,lightingSignature=feature.lastLightingSignature,lightCount=feature.lastLightCount,instances=feature.lastInstances,cutouts=feature.lastCutouts,sceneRevision=feature.lastSceneRevision,skyRevision=feature.lastSkyRevision,
                skyParameters=feature.lastSkyParameters,skyTint=feature.lastSkyTint,skyConstant=feature.lastSkyConstant,temporal=context.lastTemporal,environment=context.environmentSampling,alphaVisibility=context.alphaVisibility,
                lights=lights.SelectMany(v=>new[]{v.positionRange,v.radianceType,v.directionOuter,v.innerShadow}).ToArray(),ris=ris,lightsSha256=Hash(lights),risSha256=Hash(ris),secondarySha256=Hash(secondary),primarySha256=Hash(primary),
                selectedSecondary=at.Select(i=>secondary[i]).ToArray(),selectedPrimary=at.SelectMany(i=>new[]{primary[i].depth,primary[i].normal,primary[i].geometric,primary[i].diffuse,primary[i].specular}).ToArray(),selectedPrimaryProbe=probes.SelectMany(p=>new[]{p.originalWorld,p.rabWorld,p.normalDepth,p.albedo,p.errors0,p.errors1}).ToArray()};
        }
        static string Hash<T>(T[] values)where T:struct
        {
            int stride=Marshal.SizeOf<T>();byte[] bytes=new byte[checked(stride*values.Length)];IntPtr pointer=Marshal.AllocHGlobal(stride);
            try{for(int i=0;i<values.Length;i++){Marshal.StructureToPtr(values[i],pointer,false);Marshal.Copy(pointer,bytes,i*stride,stride);}using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","");}
            finally{Marshal.FreeHGlobal(pointer);}
        }
        public static void RunSerializationQualification()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../input-state-serialization.json"));bool pass=false;string error=null;string payload=null;
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated project required");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var state=new State{lights=Enumerable.Range(0,72).Select(i=>new Vector4(i*.125f,-i,i+123.5f,1)).ToArray(),selectedPrimary=Enumerable.Range(0,135).Select(i=>unchecked((uint)(0xF0000000u+i))).ToArray(),ris=Enumerable.Range(0,16384).Select(i=>new Vector2Int(i,i^0x12345678)).ToArray()};
                payload=JsonUtility.ToJson(state);var roundtrip=JsonUtility.FromJson<State>(payload);
                if(roundtrip.lights.Length!=72||roundtrip.selectedPrimary.Length!=135||roundtrip.ris.Length!=16384||!state.selectedPrimary.SequenceEqual(roundtrip.selectedPrimary)||!state.lights.SequenceEqual(roundtrip.lights)||!state.ris.SequenceEqual(roundtrip.ris))throw new Exception("Input snapshot roundtrip lost numeric fields");
                pass=true;
            }
            catch(Exception e){error=e.ToString();Debug.LogException(e);}
            File.WriteAllText(output,JsonUtility.ToJson(new SerializationResult{pass=pass,error=error,lightVectors=72,primaryWords=135,risRecords=16384,payloadSha256=payload==null?null:HashBytes(System.Text.Encoding.UTF8.GetBytes(payload))},true));
            Debug.Log("[HoGIInputSerialization] END pass="+pass);EditorApplication.Exit(pass?0:1);
        }
        [Serializable]sealed class SerializationResult{public bool pass;public string error,payloadSha256;public int lightVectors,primaryWords,risRecords;}
        static string HashBytes(byte[] data){using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(data)).Replace("-","");}
    }
}
