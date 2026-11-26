using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Copy into a disposable host Assets/Editor. No menu, persistent assets or source writes.
public static class HoRtgiLibraryChecks
{
    private const string Package = "Packages/jp.lilxyzw.liltoon.urp.extensions/";
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] KernelNames = { "ShadeSecondary", "Temporal", "Spatial", "Final", "FinalNRD", "ResolveNRD", "BuildEnvironmentPdf", "PresampleEnvironment" };
    private static readonly string[] Headers = { "HoRTGIApplicationBridge", "HoRTGIGrid", "HoRTGINativeSceneQuery", "HoRTGINrdSignal", "HoRTGIResampling", "HoRTGISecondaryData", "HoRTGISecondaryInputs", "HoRTGISecondaryMaterial", "HoRTGISecondaryNormals", "HoRTGISecondarySurface", "HoRTGISecondaryTension", "HoRTGIShadingCommon" };
    private static readonly List<Object> owned = new List<Object>();
    private static readonly List<Diagnostic> diagnostics = new List<Diagnostic>();
    private static int assertions;

    [StructLayout(LayoutKind.Sequential)] private struct Ray { public Vector4 originMin, directionMax; }
    [Serializable, StructLayout(LayoutKind.Sequential)] private struct Hit
    {
        public float distance; public uint status;
        public Vector3 normal, geometricNormal, baseColor;
        public float roughness, metallic, reflectance;
        public Vector3 emission;
    }
    [Serializable, StructLayout(LayoutKind.Sequential)] private struct Secondary
    {
        public Vector3 position; public uint normal, diffuse, specular;
        public Vector3 emission; public float pdf; public uint geometricNormal, flags;
    }
    [Serializable] private sealed class Kernel { public string name; public int index; public uint x, y, z; }
    [Serializable] private sealed class Diagnostic { public string asset, severity, message; }
    [Serializable] private sealed class Fingerprint { public string path, sha256; }
    [Serializable] private sealed class Result
    {
        public bool passed, sourceUntouched, nativeInvalidPrimaryDispatched, shadowContractValidated;
        public string project, unity, graphics, error;
        public int assertions, rayStride, hitStride, secondaryStride;
        public float environmentPdf, expectedEnvironmentPdf;
        public Kernel[] kernels;
        public Hit hit, miss, visibilityHit, visibilityMiss;
        public Secondary invalidPrimary;
        public Diagnostic[] diagnostics;
        public Fingerprint[] sources;
    }

    private static void Need(bool value, string message)
    { ++assertions; if (!value) throw new InvalidOperationException("RTGI_LIBRARY: " + message); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Near(float actual, float expected, string label, float tolerance = .005f)
    { Need(Finite(actual) && Mathf.Abs(actual - expected) <= tolerance, label + " actual=" + actual + " expected=" + expected); }
    private static void Near(Vector3 actual, Vector3 expected, string label)
    { Near(actual.x, expected.x, label + ".x"); Near(actual.y, expected.y, label + ".y"); Near(actual.z, expected.z, label + ".z"); }
    private static T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
    private static string Normal(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
    private static string Hash(string path)
    {
        using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private static string Physical(string assetPath)
    {
        if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
        Need(info != null && assetPath.StartsWith("Packages/" + info.name + "/", StringComparison.Ordinal), "resolved package source " + assetPath);
        return Path.Combine(info.resolvedPath, assetPath.Substring(("Packages/" + info.name + "/").Length));
    }
    private static Fingerprint[] CaptureSources(Shader cloth)
    {
        var paths = Headers.Select(name => Package + "ShaderLibrary/RTGI/" + name + ".hlsl").ToList();
        paths.AddRange(new[] { Package + "Runtime/RTGI/Shaders/HoRTGI.compute", Package + "Runtime/RTGI/Shaders/HoRTGI.raytrace", Package + "Runtime/RTGI/Shaders/HoRTGISecondaryMaterial.shader", Package + "Editor/RTGI/Shaders/HoRTGISecondaryValidation.raytrace", Package + "Editor/RTGI/Shaders/HoRTGIGridAudit.compute", AssetDatabase.GetAssetPath(cloth) });
        return paths.SelectMany(path => new[] { path, path + ".meta" }).Select(path => new Fingerprint { path = path, sha256 = Hash(Physical(path)) }).ToArray();
    }
    private static Type RuntimeType(string name)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name, false)).FirstOrDefault(value => value != null);
        Need(type != null, "runtime type available " + name); return type;
    }
    private static object Call(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethods(Members).Single(value => value.Name == name && value.GetParameters().Length == args.Length);
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) { throw new InvalidOperationException("Runtime call " + name + " failed", error.InnerException ?? error); }
    }
    private static void Bind(object scene, CommandBuffer cmd, RayTracingShader shader)
    {
        object inputs = Call(scene, "ReadCompatibility");
        var method = inputs.GetType().GetMethod("BuildAndBind", Members, null, new[] { typeof(CommandBuffer), typeof(RayTracingShader) }, null);
        Need(method != null, "runtime ray-scene binding method"); method.Invoke(inputs, new object[] { cmd, shader });
        cmd.SetRayTracingShaderPass(shader, "HoRTGISecondarySurface");
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning)
            diagnostics.Add(new Diagnostic { asset = "GPU execution log", severity = type.ToString(), message = message });
    }
    private static void CheckDiagnostics(ComputeShader compute, Shader hitShader)
    {
        foreach (var item in ShaderUtil.GetComputeShaderMessages(compute))
            diagnostics.Add(new Diagnostic { asset = AssetDatabase.GetAssetPath(compute), severity = item.severity.ToString(), message = item.message });
        foreach (var item in ShaderUtil.GetShaderMessages(hitShader))
            diagnostics.Add(new Diagnostic { asset = AssetDatabase.GetAssetPath(hitShader), severity = item.severity.ToString(), message = item.message });
        Need(!diagnostics.Any(item => item.severity == "Error" || item.severity == "Exception" || item.severity == "Assert"), "no compute, hit-shader, native DXR or binding errors");
    }
    private static Cubemap ConstantCube()
    {
        var cube = Keep(new Cubemap(1, TextureFormat.RGBAFloat, false));
        for (int face = 0; face < 6; ++face) cube.SetPixel((CubemapFace)face, 0, 0, Color.white);
        cube.Apply(false, false); return cube;
    }
    private static float DispatchPdf(ComputeShader compute, Cubemap cube)
    {
        int kernel = compute.FindKernel("BuildEnvironmentPdf");
        var output = Keep(new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = true, name = "Private RTGI library PDF" });
        Need(output.Create(), "1x1 PDF UAV allocated");
        using (var cmd = new CommandBuffer { name = "RTGI library real 1x1 PDF" })
        {
            cmd.SetComputeVectorParam(compute, "_HoRTGIEnvironment", new Vector4(1, 1, 1, 0));
            cmd.SetComputeVectorParam(compute, "_HoAtmosphereParameters", new Vector4(0, 1, 1, 1));
            cmd.SetComputeVectorParam(compute, "_HoAtmosphereTint", Vector4.one);
            cmd.SetComputeVectorParam(compute, "_HoAtmosphereConstantColor", new Vector4(.5f, .25f, .125f, 1));
            cmd.SetComputeTextureParam(compute, kernel, "_HoAtmosphereRadianceTexture", cube);
            cmd.SetComputeTextureParam(compute, kernel, "_HoRTGIEnvironmentPdfOutput", output);
            cmd.DispatchCompute(compute, kernel, 1, 1, 1); Graphics.ExecuteCommandBuffer(cmd);
        }
        var request = AsyncGPUReadback.Request(output, 0); request.WaitForCompletion();
        Need(!request.hasError, "PDF GPU readback completed");
        float result = request.GetData<float>()[0];
        Need(Finite(result) && result > 0, "PDF contains an actual finite positive write");
        Near(result, .5f * .2126f + .25f * .7152f + .125f * .0722f, "constant-atmosphere PDF luminance", .00001f);
        return result;
    }
    private static Hit[] Trace(object materials, object scene, Renderer renderer, RayTracingShader shader, GraphicsBuffer rays, GraphicsBuffer hits, bool visibility)
    {
        Call(materials, "BeginFrame");
        var method = scene.GetType().GetMethods(Members).Single(value => value.Name == "Synchronize" && value.GetParameters().Length == 4);
        method.Invoke(scene, new object[] { new Renderer[] { renderer }, (uint)255, null, Enum.ToObject(method.GetParameters()[3].ParameterType, 0) });
        Call(materials, "EndFrame");
        hits.SetData(new[] { new Hit { status = uint.MaxValue, distance = -9 }, new Hit { status = uint.MaxValue, distance = -9 } });
        using (var cmd = new CommandBuffer { name = "RTGI library real secondary " + (visibility ? "visibility" : "surface") })
        {
            Bind(scene, cmd, shader);
            cmd.SetRayTracingBufferParam(shader, "_HoGITestRays", rays); cmd.SetRayTracingBufferParam(shader, "_HoGITestHits", hits);
            cmd.DispatchRays(shader, visibility ? "HoGIVisibilityValidate" : "HoGIValidate", 2, 1, 1); Graphics.ExecuteCommandBuffer(cmd);
        }
        var result = new Hit[2]; hits.GetData(result); return result;
    }
    private static Secondary DispatchInvalidPrimary(object scene, RayTracingShader shader, Cubemap cube)
    {
        using (var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<Secondary>()))
        using (var cmd = new CommandBuffer { name = "RTGI library native invalid-primary entry" })
        {
            output.SetData(new[] { new Secondary { flags = uint.MaxValue, pdf = 99, emission = Vector3.one } });
            Bind(scene, cmd, shader);
            cmd.SetRayTracingBufferParam(shader, "_HoRTGISecondary", output);
            cmd.SetRayTracingVectorParam(shader, "_HoRTGIFrame", new Vector4(1, 1, 0, 0));
            cmd.SetRayTracingIntParam(shader, "_HoRTGIFrameIndex", 0);
            cmd.SetRayTracingVectorParam(shader, "_HoSurfaceAssociationState", Vector4.zero);
            cmd.SetRayTracingVectorParam(shader, "_HoRTPrimaryState", Vector4.zero);
            cmd.SetRayTracingVectorParam(shader, "_HoRTPrimarySize", Vector4.one);
            cmd.SetRayTracingVectorParam(shader, "_HoRTGIRaySettings", new Vector4(.001f, 10, 0, 100));
            cmd.SetRayTracingVectorParam(shader, "_HoAtmosphereParameters", Vector4.zero);
            cmd.SetRayTracingVectorParam(shader, "_HoAtmosphereTint", Vector4.one);
            cmd.SetRayTracingVectorParam(shader, "_HoAtmosphereConstantColor", Vector4.zero);
            cmd.SetRayTracingTextureParam(shader, "_HoAtmosphereRadianceTexture", cube);
            foreach (string name in new[] { "_HoRTPrimaryColor", "_HoRTPrimaryNormal", "_HoRTPrimaryMaterial", "_HoRTPrimaryReflection", "_HoRTPrimaryOwner", "_HoSurfaceAssociationSurfaceDepth", "_HoSurfaceAssociationGeometryDepth", "_HoSurfaceAssociationWriter", "_HoSurfaceAssociationGeometry" })
                cmd.SetRayTracingTextureParam(shader, name, Texture2D.blackTexture);
            cmd.DispatchRays(shader, "GenerateSecondary", 1, 1, 1); Graphics.ExecuteCommandBuffer(cmd);
            var data = new Secondary[1]; output.GetData(data); Secondary value = data[0];
            Need(value.flags == 0 && value.normal == 0 && value.geometricNormal == 0 && value.diffuse == 0 && value.specular == 0, "native GenerateSecondary overwrote sentinel with invalid-primary packet");
            Near(value.position, Vector3.zero, "invalid-primary position"); Near(value.emission, Vector3.zero, "invalid-primary emission"); Near(value.pdf, 0, "invalid-primary PDF");
            return value;
        }
    }
    public static void Run()
    {
        assertions = 0; diagnostics.Clear(); owned.Clear();
        string project = Normal(Path.Combine(Application.dataPath, ".."));
        Need(project.StartsWith("D:/Temp/", StringComparison.OrdinalIgnoreCase), "disposable D:/Temp host only");
        string report = Environment.GetEnvironmentVariable("HO_RTGI_LIBRARY_REPORT");
        Need(!string.IsNullOrEmpty(report) && Path.IsPathRooted(report) && !File.Exists(report), "fresh absolute external JSON report required");
        Need(Directory.Exists(Path.GetDirectoryName(report)) && !Normal(report).StartsWith(Normal(Application.dataPath) + "/", StringComparison.OrdinalIgnoreCase), "report remains outside imported Assets");
        var result = new Result { project = project, unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        bool previousAsync = ShaderUtil.allowAsyncCompilation; object materials = null, scene = null; Exception failure = null;
        GraphicsBuffer rays = null, hits = null;
        Application.logMessageReceived += OnLog; ShaderUtil.allowAsyncCompilation = false;
        try
        {
            Need(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 && SystemInfo.supportsRayTracing && SystemInfo.supportsInlineRayTracing && SystemInfo.supportsComputeShaders, "real D3D12 native and inline ray-tracing hardware required");
            Type validator = RuntimeType("lilToon.URP.Extensions.Editor.ShadowCast.HoShadowCastShaderContractValidator");
            var validate = validator.GetMethod("Validate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
            Need(validate != null && validate.Invoke(null, new object[] { false }) is bool contractValid && contractValid, "UPM ShadowCast validator resolves the relocated receiver library and validates it");
            result.shadowContractValidated = true;
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(Package + "Runtime/RTGI/Shaders/HoRTGI.compute");
            var validation = AssetDatabase.LoadAssetAtPath<RayTracingShader>(Package + "Editor/RTGI/Shaders/HoRTGISecondaryValidation.raytrace");
            var native = AssetDatabase.LoadAssetAtPath<RayTracingShader>(Package + "Runtime/RTGI/Shaders/HoRTGI.raytrace");
            Shader hitShader = Shader.Find("Hidden/lilToon/URP/RTGI/SecondaryMaterial"), cloth = Shader.Find("Ho/Cloth/URP/Cloth");
            Need(compute && validation && native && hitShader && cloth, "production compute/native/validation entries and Cloth shader loaded");
            result.sources = CaptureSources(cloth);
            result.kernels = KernelNames.Select(name =>
            {
                Need(compute.HasKernel(name), "compute kernel present " + name); int index = compute.FindKernel(name);
                compute.GetKernelThreadGroupSizes(index, out uint x, out uint y, out uint z);
                Need(index >= 0 && x > 0 && y > 0 && z > 0, "compiled compute kernel " + name);
                return new Kernel { name = name, index = index, x = x, y = y, z = z };
            }).ToArray();
            Cubemap cube = ConstantCube(); result.environmentPdf = DispatchPdf(compute, cube); result.expectedEnvironmentPdf = .294125f;
            var source = Keep(new Material(cloth) { name = "Private Cloth DXR source", hideFlags = HideFlags.HideAndDontSave, renderQueue = 2000 });
            source.SetOverrideTag("RenderType", "Opaque"); source.SetFloat("_SurfaceMode", 0); source.SetTexture("_BaseMap", Texture2D.whiteTexture);
            source.SetColor("_BaseColor", new Color(.2f, .4f, .6f, 1)); source.SetVector("_ColorAdjustment", new Vector4(0, 1, 1, 1));
            source.SetFloat("_PBRInputMode", 0); source.SetVector("_PBRMapFlags", Vector4.zero);
            source.SetFloat("_Smoothness", .7f); source.SetFloat("_Metallic", .3f); source.SetFloat("_Reflectance", .08f);
            source.SetFloat("_BumpScale", 0); source.SetFloat("_FlipNormal", 1); source.SetFloat("_UseEmission", 0); source.SetFloat("_HoTesionEnabled", 0);
            string sourceSnapshot = EditorJsonUtility.ToJson(source);
            var go = Keep(new GameObject("Private Cloth DXR quad") { hideFlags = HideFlags.HideAndDontSave }); go.transform.position = new Vector3(2, 0, 0);
            var mesh = Keep(new Mesh { name = "Private DXR quad", hideFlags = HideFlags.HideAndDontSave });
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0) }; mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray(); mesh.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, -1), 4).ToArray();
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }; mesh.uv2 = mesh.uv3 = mesh.uv4 = mesh.uv; mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = source;
            Type materialType = RuntimeType("lilToon.URP.Extensions.RTGI.HoRTGISecondaryMaterials"), sceneType = RuntimeType("lilToon.URP.Extensions.RT.HoRTScene");
            materials = Activator.CreateInstance(materialType, Members, null, new object[] { hitShader }, null);
            var resolve = (Func<Material, Material>)Delegate.CreateDelegate(typeof(Func<Material, Material>), materials, materialType.GetMethod("Resolve", Members));
            var constructor = sceneType.GetConstructors(Members).Single(value => value.GetParameters().Length == 4);
            scene = constructor.Invoke(new object[] { resolve, null, null, null });
            result.rayStride = Marshal.SizeOf<Ray>(); result.hitStride = Marshal.SizeOf<Hit>(); result.secondaryStride = Marshal.SizeOf<Secondary>();
            Need(result.rayStride == 32 && result.hitStride == 68 && result.secondaryStride == 48, "CPU/HLSL ray, hit and secondary ABI strides");
            rays = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, result.rayStride); hits = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, result.hitStride);
            rays.SetData(new[] { new Ray { originMin = new Vector4(2.2f, .3f, -1, .001f), directionMax = new Vector4(0, 0, 1, 10) }, new Ray { originMin = new Vector4(-2, 0, -1, .001f), directionMax = new Vector4(0, 0, 1, 10) } });
            Hit[] first = Trace(materials, scene, renderer, validation, rays, hits, false); result.hit = first[0]; result.miss = first[1];
            Need(first[0].status == 1 && first[1].status == 0, "Cloth evaluated hit and world-space miss statuses"); Near(first[0].distance, 1, "surface distance"); Near(first[1].distance, 0, "miss distance");
            Near(first[0].normal, Vector3.back, "Cloth shading normal"); Near(first[0].geometricNormal, Vector3.back, "Cloth geometric normal");
            Near(first[0].baseColor, new Vector3(.2f, .4f, .6f), "HDR-marked Cloth linear base"); Near(first[0].roughness, .3f, "Cloth roughness"); Near(first[0].metallic, .3f, "Cloth metallic"); Near(first[0].reflectance, .08f, "Cloth F0"); Near(first[0].emission, Vector3.zero, "disabled Cloth emission");
            Hit[] visibility = Trace(materials, scene, renderer, validation, rays, hits, true); result.visibilityHit = visibility[0]; result.visibilityMiss = visibility[1];
            Need(visibility[0].status == 2 && visibility[1].status == 0, "visibility hit remains an occluder and miss remains a miss"); Near(visibility[0].distance, 1, "visibility distance");
            result.invalidPrimary = DispatchInvalidPrimary(scene, native, cube); result.nativeInvalidPrimaryDispatched = true;
            Need(renderer.sharedMaterial == source && source.shader == cloth && EditorJsonUtility.ToJson(source) == sourceSnapshot, "runtime adapter and both native traces leave source material untouched");
            foreach (var item in result.sources) Need(Hash(Physical(item.path)) == item.sha256, "source bytes unchanged " + item.path);
            result.sourceUntouched = true; CheckDiagnostics(compute, hitShader); result.passed = true;
        }
        catch (Exception error) { failure = error; result.error = error.ToString(); }
        finally
        {
            try { if (scene != null) Call(scene, "Dispose"); if (materials != null) Call(materials, "Dispose"); }
            catch (Exception error) { result.passed = false; result.error = (result.error ?? "") + "\nCleanup: " + error; if (failure == null) failure = error; }
            rays?.Dispose(); hits?.Dispose();
            for (int i = owned.Count - 1; i >= 0; --i) if (owned[i]) { if (owned[i] is RenderTexture target) target.Release(); Object.DestroyImmediate(owned[i]); }
            owned.Clear(); ShaderUtil.allowAsyncCompilation = previousAsync; Application.logMessageReceived -= OnLog;
            result.assertions = assertions; result.diagnostics = diagnostics.ToArray(); File.WriteAllText(report, JsonUtility.ToJson(result, true));
        }
        if (failure != null) throw new InvalidOperationException("RTGI_LIBRARY_FAILED report=" + report, failure);
        Debug.Log("RTGI_LIBRARY_OK kernels=" + result.kernels.Length + " assertions=" + assertions + " PDF=" + result.environmentPdf + " native=GenerateSecondary hit+visibility report=" + report);
    }
}
