// Adapter around the preserved step001 reference probe, which remains unchanged.
using System;
using System.IO;
using System.Reflection;
using PathTracing;
using UnityEditor;
using UnityEngine;

public static class HoReferenceGIDiffuseProbe
{
    const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    public static void Run(){Setup(false);}
    public static void RunNoReuse(){Setup(true);}
    static object Probe(string name)=>typeof(HoReferenceGIProbe).GetField(name,Static).GetValue(null);
    static void ProbeSet(string name,object value)=>typeof(HoReferenceGIProbe).GetField(name,Static).SetValue(null,value);
    static void Setup(bool noReuse)
    {
        try
        {
            if(noReuse)HoReferenceGIProbe.RunUnityNoReuse();else HoReferenceGIProbe.RunUnityRaw();
            var feature=(UnityRtxdiFeature)Probe("feature");
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Editor/HoReferenceGIDiffuseFinal.compute");
            if(shader==null)throw new Exception("Common diffuse diagnostic compute missing");
            var errors=ShaderUtil.GetComputeShaderMessages(shader);
            if(errors.Length>0)throw new Exception("Diagnostic shader messages: "+string.Join("; ",Array.ConvertAll(errors,m=>m.message)));
            feature.giFinalShadingComputeCs=shader;feature.setting.useComputeForGIFinalShading=true;
            // Create() uses ??= for passes, so explicitly replace only this final
            // evaluator on the private in-memory feature before the first request.
            typeof(UnityRtxdiFeature).GetField("_giFinalShadingPass",Instance).SetValue(feature,
                new GIFinalShadingPass(feature.giFinalShadingShader,shader){renderPassEvent=feature.renderPassEvent});
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-reference-gi-common-diffuse-"+(noReuse?"no-reuse":"restir")));
            Directory.CreateDirectory(output);ProbeSet("output",output);
            var report=Probe("report");report.GetType().GetField("mode").SetValue(report,"common-diffuse-"+(noReuse?"no-reuse":"restir"));
            report.GetType().GetField("protocol").SetValue(report,
                "Step002: preserved reference sampler and secondary radiance; diagnostic final evaluator selects receiver diffuse only with original visibility/BRDF, final MIS off. Raw=combined minus proven deterministic single-light direct baseline; no RR. Secondary F0 remains the measured reference dielectric value near .04. 128x128, 256 GI requests; not performance/motion/Player acceptance.");
            Debug.Log("[HoReferenceGIDiffuse] FINAL_DIFFUSE_ONLY native sampling unchanged, noReuse="+noReuse);
        }
        catch(Exception error)
        {
            typeof(HoReferenceGIProbe).GetMethod("Finish",Static).Invoke(null,new object[]{error});
        }
    }
}
