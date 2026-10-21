#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.RayTracing
{
    public static partial class HoRTSceneValidation
    {
        private static readonly List<Object> objects = new();
        private static HoRTScene scene;
        private static HoRTSceneProbeFeature probe;
        private static GraphicsBuffer raysBuffer;
        private static RTHandle capture;
        private static RenderPipelineAsset previousDefault, previousQuality;
        private static bool compatibility, previousAsync;
        private static Camera camera;
        private static RenderTexture target;
        private static Mesh mesh;
        private static Renderer multi, outside;
        private static Renderer[] sources;
        private static TestRay[] rays;
        private static int frame;
        [StructLayout(LayoutKind.Sequential)]
        private struct TestRay { public Vector4 originMin, directionMax; public uint mask, pad0, pad1, pad2; }
        public static void RunStaticBaseVertex(){forceStaticBaseVertex=true;Run();}
        public static void RunStaticBaseVertexCompatibility(){compatibility=true;RunStaticBaseVertex();}
        public static void RunCompatibility() { compatibility = true; Run(); }
        public static void RunUnsupported()
        {
            using (var unavailable = new HoRTScene())
            {
                if (HoRTScene.IsSupported || unavailable.IsAvailable) throw new Exception("Expected unsupported API in this validation run");
                unavailable.Synchronize(Array.Empty<Renderer>());
                if (unavailable.InstanceCount != 0 || unavailable.Rejections.Count == 0) throw new Exception("Unsupported scene must publish a reason without instances");
            }
            Debug.Log("[HoRTSceneValidation] PASS unsupported capability branch; API=" + SystemInfo.graphicsDeviceType);
            EditorApplication.Exit(0);
        }
        public static void Run()
        {
            try
            {
                Debug.Log("[HoRTSceneValidation] START; inline=" + SystemInfo.supportsInlineRayTracing + "; API=" + SystemInfo.graphicsDeviceType);
                Require(HoRTScene.IsSupported, "D3D12 inline ray tracing is required for the hardware validation");
                previousDefault = GraphicsSettings.defaultRenderPipeline; previousQuality = QualitySettings.renderPipeline;
                previousAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                scene = new HoRTScene();
                var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                probe = Keep(ScriptableObject.CreateInstance<HoRTSceneProbeFeature>()); probe.scene = scene;
                probe.shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RayTracing/HoRTSceneValidation.compute");
                Require(probe.shader != null, "Scene compute shader missing");
                var output = Keep(new RenderTexture(8, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = true }); output.Create();
                capture = RTHandles.Alloc(output); probe.output = capture;
                rays = new[] { Ray(.1f, -3, 1), Ray(2.1f, -3, 1), Ray(6.1f, -3, 1), Ray(.1f, -3, -1), Ray(.1f, 3, -1), Ray(.1f, -3, 1, 2), Ray(6.1f, -3, 2), Ray(.1f, -3, 0) };
                raysBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, Marshal.SizeOf<TestRay>()); raysBuffer.SetData(rays); probe.rays = raysBuffer;
                renderer.rendererFeatures.Add(probe);
                var pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer)); pipeline.msaaSampleCount = 1;
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
                #else
                Require(!compatibility, "Isolated project must define URP_COMPATIBILITY_MODE");
                #endif
                mesh = Keep(new Mesh { name = "Two submesh RT validation" });
                mesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(-.5f,.5f,0), new Vector3(.5f,.5f,0),
                    new Vector3(1.5f,-.5f,0), new Vector3(2.5f,-.5f,0), new Vector3(1.5f,.5f,0), new Vector3(2.5f,.5f,0) };
                mesh.subMeshCount = 2; mesh.SetTriangles(new[] {0,2,1,2,3,1},0); mesh.SetTriangles(new[] {4,6,5,6,7,5},1); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var multiObject = Keep(new GameObject("RT two submesh object")); multiObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                multi = multiObject.AddComponent<MeshRenderer>(); multi.sharedMaterials = new[] { Mat("Universal Render Pipeline/Unlit"), Mat("Universal Render Pipeline/Unlit") };
                if(forceStaticBaseVertex)mesh.SetTriangles(new[]{0,2,1,2,3,1},1,false,4);
                if (forceSkinned) SetupSkinned(multiObject);
                // Turning raster cast off must not remove the explicitly registered RT geometry.
                multi.shadowCastingMode = ShadowCastingMode.Off;
                outside = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube)).GetComponent<Renderer>(); outside.transform.position = new Vector3(6, 0, 0);
                outside.sharedMaterial = Mat("Universal Render Pipeline/Unlit"); sources = new[] { multi, outside };
                scene.Synchronize(sources, 1);
                camera = Keep(new GameObject("RT scene test camera")).AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = new Vector3(0,0,-3); camera.farClipPlane = 5; camera.cullingMask = 0;
                target = Keep(new RenderTexture(32, 32, 24)); target.Create();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static TestRay Ray(float x, float z, float direction, uint mask = 1) => new TestRay { originMin = new Vector4(x,.1f,z,0), directionMax = new Vector4(0,0,direction,20), mask = mask };
        private static void Tick()
        {
            try
            {
                if (++frame < 3) { Render(); return; }
                if (forceSkinned) { ValidateSkinned(); Finish(null); return; }
                var image = Render(); Require(scene.InstanceCount == 3, "one instance per submesh");
                Hit(image, 0, 3, multi, 0); Hit(image, 1, 3, multi, 1); Hit(image, 2, 2.5f, outside, 0);
                Status(image, 3, 0, "opposite ray miss"); Hit(image, 4, 3, multi, 0); Status(image, 5, 0, "instance mask");
                Hit(image, 6, 2.5f, outside, 0); Status(image, 7, 3, "zero direction rejected");
                Require(Pixel(image, 0, 1).a != Pixel(image, 4, 1).a, "two-sided queries must report opposite front-face values");
                if(forceStaticBaseVertex)ValidateStaticCopyCache(image);else if(staticControlReport)ValidateStaticAliasControl();
                uint original = (uint)Pixel(image, 0, 0).g, builds = scene.BuildCount, revision = scene.Revision;
                scene.Synchronize(new[] { outside, multi, multi }, 1); Render();
                Require(scene.BuildCount == builds && scene.Revision == revision, "unchanged/reordered/duplicate registry should not rebuild");
                multi.transform.position = new Vector3(0,0,1); scene.Synchronize(sources, 1); image = Render(); Hit(image, 0, 4, multi, 0);
                Require((uint)Pixel(image,0,0).g == original && scene.BuildCount == builds + 1, "transform should retain stable identity and rebuild once");
                var materials = multi.sharedMaterials; materials[0] = Mat("Universal Render Pipeline/Unlit"); multi.sharedMaterials = materials;
                uint oldMaterial = (uint)Pixel(image,0,0).a; builds = scene.BuildCount;
                scene.Synchronize(sources, 1); image = Render(); Hit(image, 0, 4, multi, 0);
                Require((uint)Pixel(image,0,0).g == original && (uint)Pixel(image,0,0).a != oldMaterial && scene.BuildCount == builds, "material mapping update should not rebuild geometry");
                scene.Synchronize(sources, 2); image = Render(); Status(image,0,0,"mask updated"); Hit(image,5,4,multi,0);
                scene.Synchronize(sources,1); multi.enabled = false; scene.Synchronize(sources,1); image = Render(); Status(image,0,0,"disabled renderer");
                Require(!scene.TryResolve(original, out _), "removed identity must not resolve");
                multi.enabled = true; scene.Synchronize(sources,1); image = Render(); Hit(image,0,4,multi,0);
                Require((uint)Pixel(image,0,0).g != original, "removed instance identity must not be recycled");
                materials = multi.sharedMaterials; var opaque = materials[1]; materials[1] = Mat("Hidden/lilToonCutout"); multi.sharedMaterials = materials;
                scene.Synchronize(sources,1); image = Render(); Status(image,1,0,"cutout excluded"); Require(scene.Rejections.Count > 0, "cutout exclusion must be visible");
                materials[1] = opaque; multi.sharedMaterials = materials;
                var vertices = mesh.vertices; for (int i=0;i<vertices.Length;i++) vertices[i].z = 1; mesh.vertices = vertices; mesh.RecalculateBounds();
                scene.MarkGeometryDirty(); scene.Synchronize(sources,1); image = Render(); Hit(image,0,5,multi,0);
                mesh.UploadMeshData(true); scene.MarkGeometryDirty(); scene.Synchronize(sources,1); image = Render(); Hit(image,0,5,multi,0);
                if(forceStaticBaseVertex){Require(!mesh.isReadable&&scene.StaticIndexReadbackCount>0,"non-readable static mirror uses GPU index readback");var copies=scene.StaticMeshCopyCount;var reads=scene.StaticIndexReadbackCount;scene.Synchronize(sources,1);Render();Require(copies==scene.StaticMeshCopyCount&&reads==scene.StaticIndexReadbackCount,"non-readable stable frame never repeats readback");}
                scene.Synchronize(Array.Empty<Renderer>()); image = Render(); Require(scene.InstanceCount == 0, "empty registry");if(forceStaticBaseVertex)Require(scene.StaticMeshCount==0,"empty registry releases static mirrors"); Status(image,0,0,"empty AS miss");
                scene.Synchronize(sources,1); Hit(Render(),0,5,multi,0);
                scene.MarkGeometryDirty(); scene.Synchronize(sources,1); image = Render(); Hit(image,0,5,multi,0);
                Hit(image,1,5,multi,1);
                Require(Pixel(image,1,0).g >= 16 && Pixel(image,0,3).g >= 32,"metadata capacity growth was not exercised");
                long lifetime = scene.Lifetime; scene.Dispose(); scene.Dispose(); Require(!scene.IsAvailable, "disposed service");
                scene = new HoRTScene(); Require(scene.Lifetime != lifetime, "new scene lifetime must differ"); probe.scene = scene;
                scene.Synchronize(Array.Empty<Renderer>()); Status(Render(),0,0,"new empty scene");
                scene.Synchronize(sources,1); Hit(Render(),0,5,multi,0);
                multi.transform.localScale = Vector3.zero; scene.Synchronize(sources,1); Status(Render(),0,0,"singular transform excluded");
                multi.transform.localScale = Vector3.one; scene.Synchronize(sources,1); Hit(Render(),0,5,multi,0);
                var stale = scene.ReadCompatibility(); multi.transform.position += Vector3.forward; scene.Synchronize(sources,1);
                var commands = new CommandBuffer(); bool staleRejected = false;
                try { stale.BuildAndBind(commands,probe.shader,0); }
                catch (InvalidOperationException) { staleRejected = true; }
                finally { commands.Release(); }
                Require(staleRejected,"mutation after input capture must be rejected"); Hit(Render(),0,6,multi,0);
                Object.DestroyImmediate(mesh); scene.Synchronize(sources,1); Status(Render(),0,0,"destroyed mesh removed");
                Object.DestroyImmediate(outside.gameObject); scene.Synchronize(sources,1); Status(Render(),2,0,"destroyed renderer removed");
                Require(scene.InstanceCount==0,"destroyed objects retained scene entries");
                foreach (var message in ShaderUtil.GetComputeShaderMessages(probe.shader))
                    Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error && message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning, message.message);
                Debug.Log("[HoRTSceneValidation] PASS: real hardware hits, offscreen geometry, submesh/material IDs, barycentrics, front/back, masks, transform/material updates, removal/re-add, cutout rejection, edited/non-readable meshes, empty/dispose/recreate; API="+SystemInfo.graphicsDeviceType+"; compatibility="+compatibility);
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static Color[] Render()
        {
            if(forceStaticBaseVertex||staticControlReport)Debug.Log("[HoRTStaticAS] before render instances="+scene.InstanceCount+" build="+scene.BuildCount);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
            var previous = RenderTexture.active; var image = new Texture2D(8,4,TextureFormat.RGBAFloat,false,true);
            try { RenderTexture.active=capture.rt; image.ReadPixels(new Rect(0,0,8,4),0,0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active=previous; Object.DestroyImmediate(image);if(forceStaticBaseVertex||staticControlReport)Debug.Log("[HoRTStaticAS] after render instances="+scene.InstanceCount+" build="+scene.BuildCount); }
        }
        private static Color Pixel(Color[] image,int ray,int row) => image[row*8+ray];
        private static void Hit(Color[] image,int ray,float distance,Renderer renderer,int submesh)
        {
            Status(image,ray,1,"expected hit "+ray); var data=Pixel(image,ray,0); Near(data.r,distance,"distance "+ray);
            Require(scene.TryResolve((uint)data.g,out var instance),"unknown hit instance");
            Require(instance.renderer==renderer && instance.submesh==submesh && instance.material==renderer.sharedMaterials[submesh]
                && instance.materialId==(uint)data.a && instance.submesh==(int)data.b,"wrong instance/submesh/material association");
            var triangle=Pixel(image,ray,1); Require(triangle.r>=0 && triangle.g>=0 && triangle.r+triangle.g<=1.001f && triangle.b<instance.triangleCount,"invalid barycentrics/primitive");
            if (instance.mesh.isReadable)
            {
                var indices=instance.mesh.GetTriangles(submesh); var positions=instance.mesh.vertices; int start=(int)triangle.b*3;
                var local=positions[indices[start]]*(1-triangle.r-triangle.g)+positions[indices[start+1]]*triangle.r+positions[indices[start+2]]*triangle.g;
                var expected=renderer.localToWorldMatrix.MultiplyPoint3x4(local); var hitPosition=Pixel(image,ray,2);
                Near(hitPosition.r,expected.x,"barycentric X"); Near(hitPosition.g,expected.y,"barycentric Y"); Near(hitPosition.b,expected.z,"barycentric Z");
            }
        }
        private static void Status(Color[] image,int ray,int expected,string label) => Near(Pixel(image,ray,2).a,expected,label);
        private static void Near(float value,float expected,string label) => Require(Mathf.Abs(value-expected)<.002f,label+": expected "+expected+", got "+value);
        private static void Require(bool condition,string message) { if(!condition)throw new Exception(message);if(forceSkinnedBaseVertex)baseVertexChecks.Add(message);if(forceStaticBaseVertex||staticControlReport)staticBaseChecks.Add(message); }
        private static T Keep<T>(T value) where T:Object { objects.Add(value);return value; }
        private static Material Mat(string name) { var shader=Shader.Find(name);Require(shader!=null,"Missing shader "+name);return Keep(new Material(shader)); }
        private static void Finish(Exception error)
        {
            EditorApplication.update-=Tick; GraphicsSettings.defaultRenderPipeline=previousDefault; QualitySettings.renderPipeline=previousQuality; ShaderUtil.allowAsyncCompilation=previousAsync;
            if(forceSkinnedBaseVertex)SaveBaseVertexReport(error);if(forceStaticBaseVertex||staticControlReport)SaveStaticBaseReport(error);
            scene?.Dispose(); scene=null; raysBuffer?.Dispose(); raysBuffer=null; capture?.Release(); capture=null;
            for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]); objects.Clear();
            if(error!=null)Debug.LogException(error); EditorApplication.Exit(error==null?0:1);
        }
    }

    [SupportedOnRenderer()]
    internal sealed class HoRTSceneProbeFeature : ScriptableRendererFeature
    {
        public HoRTScene scene; public ComputeShader shader; public GraphicsBuffer rays; public RTHandle output;
        private ProbePass pass;
        public override void Create() { pass=new ProbePass(); }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data) { pass.owner=this;renderer.EnqueuePass(pass); }
        private sealed class ProbePass : ScriptableRenderPass
        {
            public HoRTSceneProbeFeature owner;
            public ProbePass() { renderPassEvent=RenderPassEvent.AfterRenderingOpaques; }
            private sealed class Data { public HoRTScene.Inputs scene;public HoRTSceneProbeFeature owner;public TextureHandle output; }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                using(var builder=graph.AddUnsafePass<Data>("HoRTScene hardware validation",out var data))
                {
                    data.owner=owner;data.scene=owner.scene.Read(graph,builder);data.output=graph.ImportTexture(owner.output);
                    builder.UseBuffer(graph.ImportBuffer(owner.rays));builder.UseTexture(data.output,AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static(Data d,UnsafeGraphContext context)=>
                    {
                        var cmd=CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);d.scene.BuildAndBind(cmd,d.owner.shader,0);
                        cmd.SetComputeBufferParam(d.owner.shader,0,"_TestRays",d.owner.rays);cmd.SetComputeTextureParam(d.owner.shader,0,"_TestOutput",d.output);
                        cmd.DispatchCompute(d.owner.shader,0,1,1,1);
                    });
                }
            }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            {
                var cmd=CommandBufferPool.Get("HoRTScene hardware validation");owner.scene.ReadCompatibility().BuildAndBind(cmd,owner.shader,0);
                cmd.SetComputeBufferParam(owner.shader,0,"_TestRays",owner.rays);cmd.SetComputeTextureParam(owner.shader,0,"_TestOutput",owner.output.nameID);
                cmd.DispatchCompute(owner.shader,0,1,1,1);context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
        }
    }
}
