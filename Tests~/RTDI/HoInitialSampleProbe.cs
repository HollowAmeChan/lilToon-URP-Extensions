using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
public static class HoInitialSampleProbe
{
    [StructLayout(LayoutKind.Sequential)]private struct LightData{public Vector4 positionRange,radianceType,directionOuter,innerShadow;}
    public static void Run()
    {
        try
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new InvalidOperationException("Use the isolated research project");
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Editor/HoInitialSampleProbe.compute");int kernel=shader.FindKernel("Compare");
            using(var lights=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,64))using(var output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,64*64*2,16))
            {
                lights.SetData(new[]{new LightData{positionRange=new Vector4(.401924f,.15f,-1.5f,20),radianceType=new Vector4(.7f*4,.4f*4,.2f*4,0)},new LightData{positionRange=new Vector4(-30,0,-50,100),radianceType=new Vector4(.7f*25000,.4f*25000,.2f*25000,0)}});
                shader.SetBuffer(kernel,"_HoRTDILights",lights);shader.SetVector("_HoRTDIFrame",new Vector4(64,64,2,2));shader.SetBuffer(kernel,"_Results",output);
                foreach(int samples in new[]{1,8})
                {
                shader.SetInt("_SampleCount",samples);shader.Dispatch(kernel,8,8,1);
                var values=new Vector4[64*64*2];output.GetData(values);int mismatches=0,far=0,returnedNear=0;
                for(int i=0;i<values.Length;i+=2){if((new Vector3(values[i].x,values[i].y,values[i].z)-new Vector3(values[i+1].x,values[i+1].y,values[i+1].z)).sqrMagnitude>1e-6)mismatches++;if(values[i].w==1)far++;if(Mathf.Abs(values[i].z+1.5f)<.001f)returnedNear++;}
                for(int i=1;i<values.Length;i+=2)if(values[i].w!=1)throw new InvalidOperationException("Probe reservoir invalid; cannot interpret sample mismatch");
                Debug.Log($"[HoInitialSampleProbe] candidates={samples} mismatches={mismatches}/4096 reservoirFar={far} returnedNear={returnedNear} first={values[0]} canonical={values[1]}; diagnostic only, not acceptance");
                }
            }
            EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
