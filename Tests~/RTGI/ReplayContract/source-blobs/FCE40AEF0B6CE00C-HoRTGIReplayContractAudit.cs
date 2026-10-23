using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static class HoRTGIReplayContractAudit
    {
        [StructLayout(LayoutKind.Sequential)]struct U4{public uint x,y,z,w;}
        [Serializable]sealed class Layout{public string type;public int size,position,misc,radiance,weight,normal,unused;}
        [Serializable]sealed class Report{public bool pass;public string error,api,unity,shader,stream,streamSha256;public int cases=64,wordsPerCase=10,cpuEqualWordChecks,collisions,selectedB,missingMisc,missingUnused;public Layout editorLayout,playerProbeLayout;}
        public static void Run()
        {
            var r=new Report();GraphicsBuffer reservoirs=null,results=null;string output=null;
            try{
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated project required");EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-replay-contract"));Directory.CreateDirectory(output);r.api=SystemInfo.graphicsDeviceType.ToString();r.unity=Application.unityVersion;
                if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D12)throw new Exception("D3D12 required");
                r.editorLayout=Check(typeof(HoRTGIHistoryFeedbackAudit.Packet));var player=Type.GetType("HoRTGIRuntimeProbe, Assembly-CSharp");var packet=player?.GetNestedType("Packet",BindingFlags.NonPublic);if(packet==null)throw new Exception("Copy updated actual runtime probe into isolated Assets first");r.playerProbeLayout=Check(packet);
                r.shader="Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGIReplayContract.compute";var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(r.shader);if(shader==null)throw new Exception("Missing contract shader");int kernel=shader.FindKernel("Probe");
                foreach(var m in ShaderUtil.GetComputeShaderMessages(shader))if(m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(m.message);
                reservoirs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2048,32);results=new GraphicsBuffer(GraphicsBuffer.Target.Structured,64*10,16);
                using(var cmd=new CommandBuffer{name="Original RTXDI GI ABI and same-choice information contract"}){cmd.SetComputeBufferParam(shader,kernel,"_Reservoirs",reservoirs);cmd.SetComputeBufferParam(shader,kernel,"_Results",results);cmd.DispatchCompute(shader,kernel,1,1,1);Graphics.ExecuteCommandBuffer(cmd);}
                var words=new U4[640];results.GetData(words);var packets=new HoRTGIHistoryFeedbackAudit.Packet[2048];reservoirs.GetData(packets);
                r.stream="contract.bin";using(var w=new BinaryWriter(File.Create(Path.Combine(output,r.stream))))foreach(var v in words){w.Write(v.x);w.Write(v.y);w.Write(v.z);w.Write(v.w);}
                using(var h=System.Security.Cryptography.SHA256.Create())r.streamSha256=BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(Path.Combine(output,r.stream)))).Replace("-","");
                for(int i=0;i<64;i++){
                    int at=i*10,address=(i/16)*256+i%16;var p=packets[address];var fields=words[at+6];
                    if(p.position!=new Vector3(1.25f,-2.5f,3.75f)||p.misc!=fields.x||p.radiance!=fields.y||p.normal!=fields.z||BitConverter.SingleToInt32Bits(p.weight)!=unchecked((int)fields.w)||p.unused!=0x3f000001)throw new Exception("Actual GPU packet field interpretation differs");
                    if(words[at+1].x!=0xcafe0b07||words[at+1].y!=0x00000b07||words[at+1].z!=0||words[at+1].w!=32)throw new Exception("SDK metadata drop/stride contract differs");r.missingMisc++;r.missingUnused++;
                    r.collisions+=(int)words[at].z;r.selectedB+=(int)words[at].w;
                    byte[] bytes=Bytes(p);var from=Marshal.AllocHGlobal(32);try{Marshal.Copy(bytes,0,from,32);object original=Marshal.PtrToStructure(from,packet);var same=player.GetMethod("Same",BindingFlags.Static|BindingFlags.NonPublic);if(same==null)throw new Exception("Runtime packet equality helper missing");for(int word=0;word<8;word++){var changed=(byte[])bytes.Clone();changed[word*4]^=1;Marshal.Copy(changed,0,from,32);var actual=(HoRTGIHistoryFeedbackAudit.Packet)Marshal.PtrToStructure(from,typeof(HoRTGIHistoryFeedbackAudit.Packet));object runtime=Marshal.PtrToStructure(from,packet);if(HoRTGIHistoryFeedbackAudit.Equal(p,actual)||(bool)same.Invoke(null,new[]{original,runtime}))throw new Exception("Packet equality omits a word");r.cpuEqualWordChecks+=2;}}finally{Marshal.FreeHGlobal(from);}
                }
                if(r.collisions!=32||r.selectedB!=48||r.cpuEqualWordChecks!=1024)throw new Exception("SDK selection/collision witnesses incomplete");r.pass=true;
            }catch(Exception e){r.error=e.ToString();Debug.LogException(e);}finally{reservoirs?.Dispose();results?.Dispose();if(output!=null)File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(r,true));}
            Debug.Log("[HoGIReplayContract] END pass="+r.pass);EditorApplication.Exit(r.pass?0:1);
        }
        static byte[] Bytes(HoRTGIHistoryFeedbackAudit.Packet p){var ptr=Marshal.AllocHGlobal(32);try{Marshal.StructureToPtr(p,ptr,false);var b=new byte[32];Marshal.Copy(ptr,b,0,32);return b;}finally{Marshal.FreeHGlobal(ptr);}}
        static Layout Check(Type type){int Offset(string n)=>(int)Marshal.OffsetOf(type,n);var r=new Layout{type=type.FullName,size=Marshal.SizeOf(type),position=Offset("position"),misc=Offset("misc"),radiance=Offset("radiance"),weight=Offset("weight"),normal=Offset("normal"),unused=Offset("unused")};if(r.size!=32||r.position!=0||r.misc!=12||r.radiance!=16||r.weight!=20||r.normal!=24||r.unused!=28)throw new Exception("CPU GI ABI semantic offsets drift");return r;}
    }
}
