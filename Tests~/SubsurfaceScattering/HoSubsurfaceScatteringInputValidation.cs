// Copy into an isolated Unity project's Assets/Editor and run with -batchmode
// -force-d3d11 -executeMethod HoSubsurfaceScatteringInputValidation.Run.
// Records the shipping Source pass with real RenderGraph handles. Graph commands are not executed;
// a graphics device is needed for RenderGraph's default textures even in contextless tests.
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.SubsurfaceScattering;

public static class HoSubsurfaceScatteringInputValidation
{
    static readonly string[] SurfaceInputs =
    {
        "classificationTexture", "colorTexture", "materialTexture", "reflectionTexture", "ownerTexture"
    };

    public static void Run()
    {
        try
        {
            if (!Application.dataPath.Replace('\\', '/').Contains("/.codex-research/"))
                throw new InvalidOperationException("Use an isolated validation project.");
            // Missing SB, each individually missing input, and then the fully populated case.
            Check("all", false);
            foreach (string input in SurfaceInputs) Check(input, false);
            Check(null, true);
            Debug.Log("[SSSInputValidation] PASS: missing SB + 5 partial inputs skip safely; complete inputs record Source.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    static void Check(string missingInput, bool shouldRecord)
    {
        var graph = new RenderGraph("SSS input validation");
        var command = new CommandBuffer { name = "SSS input validation" };
        var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        var cameraObject = new GameObject("SSS input validation camera");
        var frame = new ContextContainer();
        bool recording = false;
        try
        {
            var parameters = new RenderGraphParameters { commandBuffer = command };
            object boxed = parameters;
            typeof(RenderGraphParameters).GetField("invalidContextForTesting", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(boxed, true);
            graph.BeginRecording((RenderGraphParameters)boxed);
            recording = true;
            TextureHandle input = graph.CreateTexture(new TextureDesc(16, 16)
            {
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm, name = "SSS validation input"
            });
            var resources = frame.GetOrCreate<UniversalResourceData>();
            typeof(UniversalResourceDataBase).GetMethod("InitFrame", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(resources, null);
            typeof(UniversalResourceData).GetField("_cameraColor", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(resources, input);
            var camera = frame.GetOrCreate<UniversalCameraData>();
            camera.camera = cameraObject.AddComponent<Camera>();
            camera.cameraTargetDescriptor = new RenderTextureDescriptor(16, 16);

            Assembly runtime = typeof(HoSubsurfaceScatteringRendererFeature).Assembly;
            object ac = GetFrame(frame, runtime, "AttributeComposite.HoAttributeCompositeRenderGraphResources");
            foreach (string field in new[] { "identityId0Texture", "identityId1Texture", "identityCoverageTexture" })
                Set(ac, field, input);
            object geometry = GetFrame(frame, runtime, "GeometryBuffer.HoGeometryBufferRenderGraphResources");
            Set(geometry, "normalDepthTexture", input);
            object surface = GetFrame(frame, runtime, "SurfaceBuffer.HoSurfaceBufferRenderGraphResources");
            foreach (string field in SurfaceInputs)
                if (missingInput != "all" && missingInput != field) Set(surface, field, input);

            Type passType = runtime.GetType("lilToon.URP.Extensions.SubsurfaceScattering.HoSubsurfaceScatteringSourcePass", true);
            var pass = (ScriptableRenderPass)Activator.CreateInstance(passType, true);
            passType.GetMethod("SetupRenderGraph").Invoke(pass,
                new object[] { new HoSubsurfaceScatteringSettings(), null, material });
            pass.RecordRenderGraph(graph, frame);
            object sss = GetFrame(frame, runtime, "SubsurfaceScattering.HoSubsurfaceScatteringRenderGraphResources");
            bool recorded = ((TextureHandle)sss.GetType().GetField("sourceTexture").GetValue(sss)).IsValid();
            if (recorded != shouldRecord)
                throw new InvalidOperationException("Unexpected SSS Source state for missing=" + (missingInput ?? "none"));
            if (!shouldRecord && !HoSubsurfaceScatteringRuntimeDiagnostics.CurrentSnapshot.Reason.Contains("SurfaceBuffer"))
                throw new InvalidOperationException("Missing SB should be visible in runtime diagnostics.");
            Debug.Log("[SSSInputValidation] PASS missing=" + (missingInput ?? "none") + " recorded=" + recorded);
        }
        finally
        {
            // Contextless test cancellation resets recording without executing an artificial graph.
            if (recording) graph.ResetGraphAndLogException(new OperationCanceledException("Recording-only validation"));
            graph.Cleanup();
            frame.Dispose();
            command.Release();
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    static object GetFrame(ContextContainer frame, Assembly runtime, string suffix)
    {
        Type type = runtime.GetType("lilToon.URP.Extensions." + suffix, true);
        return typeof(ContextContainer).GetMethod("GetOrCreate").MakeGenericMethod(type).Invoke(frame, null);
    }

    static void Set(object target, string field, TextureHandle value) => target.GetType().GetField(field).SetValue(target, value);
}
