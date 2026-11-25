using System;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class HoCapsuleAOGpuTests
    {
        private const int Size = 97;
        private GameObject cameraObject;
        private Camera camera;
        private Material capsule, fixture;
        private Mesh floor;
        private RenderTexture depth, normal, mask, ao;
        private Texture2D read;
        private bool asyncCompilation;
        private RenderTexture previous;
        private Matrix4x4 vp;

        [SetUp]
        public void SetUp()
        {
            asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            previous = RenderTexture.active;
            cameraObject = new GameObject("Capsule AO GPU fixture");
            camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 2, 0);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            camera.orthographic = true; camera.orthographicSize = 1; camera.aspect = 1;
            camera.nearClipPlane = .01f; camera.farClipPlane = 10;
            vp = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
            capsule = new Material(Resources.Load<Shader>("HoCapsuleAO"));
            fixture = new Material(Shader.Find("Hidden/HoCapsuleAO/Fixture"));
            floor = new Mesh { vertices = new[] { new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1) },
                triangles = new[] { 0,2,1,0,3,2 } };
            floor.RecalculateNormals(); floor.RecalculateBounds();
            depth = Target(RenderTextureFormat.RFloat);
            normal = Target(RenderTextureFormat.ARGBFloat);
            mask = Target(RenderTextureFormat.R8);
            ao = Target(RenderTextureFormat.ARGBFloat);
            read = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            var cmd = new CommandBuffer();
            cmd.SetGlobalMatrix("_FixtureVP", vp);
            cmd.SetGlobalMatrix("_FixtureView", camera.worldToCameraMatrix);
            cmd.SetRenderTarget(depth); cmd.ClearRenderTarget(false,true,Color.black);
            cmd.DrawMesh(floor,Matrix4x4.identity,fixture,0,0);
            cmd.SetRenderTarget(normal); cmd.ClearRenderTarget(false,true,Color.clear);
            cmd.DrawMesh(floor,Matrix4x4.identity,fixture,0,1);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
        }

        private static RenderTexture Target(RenderTextureFormat format)
        {
            var rt = new RenderTexture(Size,Size,0,format,RenderTextureReadWrite.Linear) { filterMode=FilterMode.Point };
            rt.Create(); return rt;
        }

        private void DrawMask(bool foreground=false, int repeats=1)
        {
            var cmd = new CommandBuffer();
            cmd.SetRenderTarget(depth);
            cmd.DrawMesh(floor,Matrix4x4.identity,fixture,0,0);
            if(foreground)
            {
                cmd.SetRenderTarget(depth);
                cmd.DrawMesh(floor,Matrix4x4.TRS(new Vector3(0,.1f,0),Quaternion.identity,new Vector3(.2f,1,.2f)),fixture,0,0);
            }
            cmd.SetRenderTarget(mask);cmd.ClearRenderTarget(false,true,Color.black);
            cmd.SetGlobalTexture("_HoCapsuleGeometryDepth",depth);
            cmd.SetGlobalVector("_HoCapsuleScreenSize",new Vector4(Size,Size,1f/Size,1f/Size));
            cmd.SetGlobalMatrix("_HoCapsuleViewProjection",vp);
            for(int i=0;i<repeats;i++)cmd.DrawMesh(floor,Matrix4x4.identity,capsule,0,0);
            Graphics.ExecuteCommandBuffer(cmd);cmd.Release();
        }

        private void Append(HoCapsuleAOMode mode, HoCapsuleAOBlendMode blend,float height=.06f,float strength=.4f,float initial=.7f)
        {
            var a=new Vector4[64];var b=new Vector4[64];
            a[0]=new Vector4(0,height,0,.06f);b[0]=new Vector4(0,height,0,0);
            var cmd=new CommandBuffer();
            cmd.SetRenderTarget(ao);cmd.ClearRenderTarget(false,true,new Color(initial,initial,initial,1));
            cmd.SetGlobalTexture("_HoCapsuleReceiverMask",mask);
            cmd.SetGlobalTexture("_HoCapsuleGeometryDepth",depth);
            cmd.SetGlobalTexture("_HoCapsuleGeometryNormal",normal);
            cmd.SetGlobalMatrix("_HoCapsuleInverseViewProjection",vp.inverse);
            cmd.SetGlobalVector("_HoCapsuleScreenSize",new Vector4(Size,Size,1f/Size,1f/Size));
            cmd.SetGlobalInt("_HoCapsuleCount",1);
            cmd.SetGlobalVectorArray("_HoCapsuleA",a);cmd.SetGlobalVectorArray("_HoCapsuleB",b);
            cmd.SetGlobalVector("_HoCapsuleParams",new Vector4(strength,.5f,(float)mode,0));
            cmd.SetGlobalVector("_HoCapsuleCross",new Vector4(.25f,.06f,Mathf.PI*.5f,.01f));
            cmd.SetGlobalFloat("_HoCapsuleRotation",0);
            cmd.DrawProcedural(Matrix4x4.identity,capsule,blend==HoCapsuleAOBlendMode.Min?1:2,MeshTopology.Triangles,3);
            cmd.SetGlobalInt("_HoCapsuleCount",0);
            Graphics.ExecuteCommandBuffer(cmd);cmd.Release();
            Assert.That(ShaderUtil.ShaderHasError(capsule.shader),Is.False);
            Assert.That(ShaderUtil.ShaderHasError(fixture.shader),Is.False);
            RenderTexture.active=ao;read.ReadPixels(new Rect(0,0,Size,Size),0,0);read.Apply();
        }

        private float At(Vector3 world)
        {
            Vector4 clip=vp*new Vector4(world.x,world.y,world.z,1);
            Vector2 uv=new Vector2(clip.x,clip.y)/clip.w*.5f+Vector2.one*.5f;
            // Texture2D ReadPixels has bottom-left coordinates, matching clip-space raster.
            int x=Mathf.Clamp(Mathf.FloorToInt(uv.x*Size),0,Size-1),y=Mathf.Clamp(Mathf.FloorToInt(uv.y*Size),0,Size-1);
            return read.GetPixel(x,y).r;
        }

        [TestCase(HoCapsuleAOMode.Capsule,HoCapsuleAOBlendMode.Min,.6f)]
        [TestCase(HoCapsuleAOMode.Capsule,HoCapsuleAOBlendMode.Multiply,.42f)]
        [TestCase(HoCapsuleAOMode.Cross,HoCapsuleAOBlendMode.Min,.6f)]
        [TestCase(HoCapsuleAOMode.Cross,HoCapsuleAOBlendMode.Multiply,.42f)]
        public void ContactCombinesVisibilityOnce(HoCapsuleAOMode mode,HoCapsuleAOBlendMode blend,float expected)
        {
            DrawMask(repeats:3);Append(mode,blend);
            Assert.That(At(Vector3.zero),Is.EqualTo(expected).Within(.002f));
            Assert.That(At(new Vector3(.8f,0,.8f)),Is.EqualTo(.7f).Within(.0001f));
        }

        [TestCase(HoCapsuleAOMode.Capsule)]
        [TestCase(HoCapsuleAOMode.Cross)]
        public void ForegroundAndRaisedFootDoNotDarkenGround(HoCapsuleAOMode mode)
        {
            DrawMask(true);Append(mode,HoCapsuleAOBlendMode.Multiply);
            Assert.That(At(Vector3.zero),Is.EqualTo(.7f).Within(.0001f));
            DrawMask();Append(mode,HoCapsuleAOBlendMode.Multiply,height:.8f);
            Assert.That(At(Vector3.zero),Is.EqualTo(.7f).Within(.0001f));
        }

        [Test]
        public void CrossProducesTwoFiniteDiagonalBands()
        {
            DrawMask();Append(HoCapsuleAOMode.Cross,HoCapsuleAOBlendMode.Min);
            Assert.That(At(new Vector3(.13f,0,.13f)),Is.LessThan(.65f));
            Assert.That(At(new Vector3(.13f,0,-.13f)),Is.LessThan(.65f));
            Assert.That(At(new Vector3(.16f,0,0)),Is.EqualTo(.7f).Within(.001f));
            Assert.That(At(new Vector3(.4f,0,.4f)),Is.EqualTo(.7f).Within(.001f));
        }

        [Test]
        public void EmitterGeometryFollowsBoneAndConservativeScaleWithoutPhysicsCollider()
        {
            var go=new GameObject("Emitter geometry");
            try
            {
                var emitter=go.AddComponent<HoCapsuleAOEmitter>();
                emitter.radius=.1f;emitter.height=.6f;emitter.direction=2;
                go.transform.SetPositionAndRotation(new Vector3(1,2,3),Quaternion.Euler(20,45,10));
                go.transform.localScale=new Vector3(-2,3,4);
                Assert.That(emitter.TryGetWorldCapsule(out Vector3 a,out Vector3 b,out float radius),Is.True);
                Assert.That(radius,Is.EqualTo(.3f).Within(1e-6));
                Assert.That((a+b)*.5f,Is.EqualTo(go.transform.position));
                Assert.That(Vector3.Distance(a,b)+2*radius,Is.EqualTo(2.4f).Within(1e-5));
                Assert.That(go.GetComponent<Collider>(),Is.Null);
                emitter.enabled=false;
                Assert.That(emitter.TryGetWorldCapsule(out _,out _,out _),Is.False);
            }
            finally {Object.DestroyImmediate(go);}
        }

        [TearDown]
        public void TearDown()
        {
            RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=asyncCompilation;
            foreach(Object o in new Object[]{depth,normal,mask,ao,read,floor,capsule,fixture,cameraObject})
                if(o!=null)Object.DestroyImmediate(o);
        }
    }
}
