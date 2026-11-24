using System;
using System.Collections;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    public static class HoCharacterHairShadowValidation
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type PassType = typeof(HoCharacterSpecializationSettings).Assembly.GetType(
            "lilToon.URP.Extensions.CharacterSpecialization.HoCharacterSpecializationPass", true);

        [MenuItem("HoLil/Validation/Validate Character Hair Shadow")]
        public static void ValidateGpu()
        {
            Require(Application.isBatchMode, "Run in an isolated graphics batch editor.");
            ValidateCaptureDemand();
            ValidateCompatibilityResources();
            foreach (var mode in new[] { HoCharacterSpecializationDebugMode.Off, HoCharacterSpecializationDebugMode.HairShadowDirection,
                HoCharacterSpecializationDebugMode.EyeAngleFactor, HoCharacterSpecializationDebugMode.ReferenceFrameView })
                ValidateGraph(mode);
            var material = new Material(Shader.Find("Hidden/Ho-CharacterSpecialization/HairShadowProbe"));
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var cameraObject = new GameObject("Hair shadow validation camera");
            bool async = ShaderUtil.allowAsyncCompilation;
            RenderTexture previous = RenderTexture.active;
            int cases = 0; float maxError = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                target.Create();
                var camera = cameraObject.AddComponent<Camera>();
                foreach (bool orthographic in new[] { false, true })
                foreach (bool flipped in new[] { false, true })
                foreach (float aspect in new[] { 16f / 9f, 9f / 16f })
                foreach (float roll in new[] { 0f, 47f })
                foreach (float yaw in new[] { -65f, 0f, 65f })
                {
                    camera.orthographic = orthographic; camera.orthographicSize = 2; camera.aspect = aspect;
                    camera.transform.position = new Vector3(.4f, .3f, -3);
                    camera.transform.rotation = Quaternion.LookRotation(-camera.transform.position) * Quaternion.AngleAxis(roll, Vector3.forward);
                    Matrix4x4 worldToClip = GL.GetGPUProjectionMatrix(camera.projectionMatrix, flipped) * camera.worldToCameraMatrix;
                    Vector3 normal = Quaternion.Euler(0, yaw, 0) * Vector3.back;
                    foreach (Vector3 light in new[] { new Vector3(.7f, .5f, -1), new Vector3(-.7f, .3f, -1), Vector3.back,
                        new Vector3(.001f, 0, -1), Vector3.forward, Vector3.right, Vector3.zero })
                    foreach (float influence in new[] { 0f, .5f, 1f })
                    {
                        Vector4 origin = new Vector4(.2f, -.1f, 0, 1);
                        Vector4 lightData = new Vector4(light.x, light.y, light.z, light == Vector3.zero ? 0 : 1);
                        var size = new Vector2(1080 * aspect, 1080);
                        Check(material, target, read, origin, normal, lightData, worldToClip, size, 240 * Mathf.Deg2Rad, influence,
                            ref cases, ref maxError);
                    }
                }
                // Missing frame / main light / degenerate normal must preserve fixed settings exactly.
                Check(material, target, read, Vector4.zero, Vector3.back, new Vector4(1, 1, -1, 1), Matrix4x4.identity,
                    new Vector2(100,100), 1.2f, 1, ref cases, ref maxError);
                Check(material, target, read, Vector4.one, Vector3.zero, new Vector4(1, 1, -1, 1), Matrix4x4.identity,
                    new Vector2(100,100), 1.2f, 1, ref cases, ref maxError);
                var production = new Material(Shader.Find("Hidden/lilToon-HoCharacterSpecialization/URP/Composite"));
                try
                {
                    ShaderUtil.CompilePass(production, 0, true);
                    Require(!ShaderUtil.ShaderHasError(production.shader), "Production composite shader failed compilation.");
                    ValidateProductionMask(production,camera);
                }
                finally { Object.DestroyImmediate(production); }
                Debug.Log($"HO_CHARACTER_HAIR_SHADOW_GPU_PASSED api={SystemInfo.graphicsDeviceType} cases={cases} maxError={maxError:R} perspective/ortho/aspect/roll/head/light/fallback=passed");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async; RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(read);
                Object.DestroyImmediate(material); Object.DestroyImmediate(cameraObject);
            }
        }

        private static void Check(Material material, RenderTexture target, Texture2D read, Vector4 origin, Vector3 normal,
            Vector4 light, Matrix4x4 worldToClip, Vector2 size, float angle, float influence, ref int cases, ref float maxError)
        {
            material.SetVector("_TestOrigin", origin); material.SetVector("_TestNormal", normal);
            material.SetVector("_TestLight", light); material.SetMatrix("_TestWorldToClip", worldToClip);
            material.SetVector("_TestParams", new Vector4(size.x, size.y, angle, influence));
            Graphics.Blit(null, target, material); RenderTexture.active = target;
            read.ReadPixels(new Rect(0,0,1,1),0,0); read.Apply(); Color actual = read.GetPixel(0,0);
            Vector3 expected = Reference(origin, normal, light, worldToClip, size, angle, influence);
            float error = Mathf.Max(Mathf.Abs(actual.r - expected.x), Mathf.Abs(actual.g - expected.y), Mathf.Abs(actual.b - expected.z));
            Require(!float.IsNaN(error) && error < .004f, "GPU direction differs from projected finite-difference reference: " + error);
            Require(new Vector2(actual.r,actual.g).sqrMagnitude <= 1.001f, "Mixed offset exceeded authored distance.");
            maxError = Mathf.Max(maxError,error); cases++;
        }

        private static Vector3 Reference(Vector4 origin, Vector3 normal, Vector4 lightData, Matrix4x4 matrix, Vector2 size, float angle, float influence)
        {
            Vector2 manual = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            Vector3 fallback = new Vector3(manual.x,manual.y,0);
            Vector3 light = lightData;
            if (origin.w < .5f || lightData.w < .5f || influence < .0001f || normal.magnitude < .0001f || light.magnitude < .0001f)
                return fallback;
            normal.Normalize(); light.Normalize(); float front = Vector3.Dot(normal,light);
            Vector3 tangent = Vector3.ProjectOnPlane(-light,normal);
            Vector3 point = origin; const float epsilon = .001f;
            Vector4 a = matrix * new Vector4(point.x-tangent.x*epsilon,point.y-tangent.y*epsilon,point.z-tangent.z*epsilon,1);
            Vector4 b = matrix * new Vector4(point.x+tangent.x*epsilon,point.y+tangent.y*epsilon,point.z+tangent.z*epsilon,1);
            if (a.w < .0001f || b.w < .0001f) return fallback;
            Vector2 projected = new Vector2(b.x/b.w-a.x/a.w,b.y/b.w-a.y/a.w)/(2*epsilon);
            projected *= (a.w+b.w)*.5f;
            if (SystemInfo.graphicsUVStartsAtTop) projected.y = -projected.y;
            projected = Vector2.Scale(projected,size)/Mathf.Max(size.x,size.y);
            float length = projected.magnitude;
            if (length < .0001f) return fallback;
            float applied = Mathf.Clamp01(influence)*Smooth(.02f,.25f,front)*Smooth(.001f,.03f,length);
            Vector2 offset = Vector2.Lerp(manual,projected/length,applied);
            return new Vector3(offset.x,offset.y,applied);
        }

        private static float Smooth(float a,float b,float value) { float t=Mathf.Clamp01((value-a)/(b-a)); return t*t*(3-2*t); }

        private static void ValidateProductionMask(Material material, Camera camera)
        {
            const int width=64, height=32;
            var semantic=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            var identity=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var target=new RenderTexture(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var read=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            var frames=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,64);
            var identities=new GraphicsBuffer(GraphicsBuffer.Target.Structured,65536,4);
            var cmd=new CommandBuffer();
            RenderTexture previous=RenderTexture.active;
            try
            {
                var pixels=new Color[width*height];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                    pixels[y*width+x]=new Color(0,1,x>=24&&x<40&&y>=12&&y<24?1:0,0);
                semantic.SetPixels(pixels);semantic.Apply();semantic.filterMode=FilterMode.Point;
                identity.SetPixel(0,0,new Color(1f/255,1f/255,0,0));identity.Apply();
                frames.SetData(new[] {default(lilToon.URP.Extensions.GeometryData.HoGeometryFrameData),
                    new lilToon.URP.Extensions.GeometryData.HoGeometryFrameData {originValid=new Vector4(0,0,0,1),forward=Vector3.back,right=Vector3.right,up=Vector3.up}});
                var map=new uint[65536];map[257]=1;identities.SetData(map);
                material.SetBuffer("_HoGDObjectFrames",frames);material.SetBuffer("_HoGDIdentityFrameSlots",identities);
                material.SetInt("_HoGDObjectFrameCount",2);material.SetFloat("_HoACActive",1);
                material.SetTexture("_HoObjectBufferId0Texture",identity);
                material.SetTexture("_lilHoCharacterObjectSemantic0_3Texture",semantic);
                material.SetTexture("_lilHoCharacterObjectSemantic4_7Texture",Texture2D.blackTexture);
                material.SetTexture("_BlitTexture",Texture2D.whiteTexture);
                material.SetVector("_BlitScaleBias",new Vector4(1,1,0,0));
                material.SetVector("_lilHoCharacterScreenTexelSize",new Vector4(1f/width,1f/height,width,height));
                material.SetVector("_HoCharacterHairShadowParams",new Vector4(1,8,0,0));
                material.SetVector("_HoCharacterHairShadowParams2",Vector4.zero);
                material.SetVector("_HoCharacterOptions",new Vector4(0,1,1,4));
                camera.transform.position=new Vector3(0,0,-3);camera.transform.rotation=Quaternion.identity;
                camera.aspect=2;camera.orthographic=true;camera.orthographicSize=2;
                target.Create();
                Vector2 RenderCentroid(float influence,Vector4 light)
                {
                    material.SetVector("_HoCharacterHairShadowParams1",new Vector4(0,0,0,influence));
                    material.SetVector("_HoCharacterHairShadowLightDirection",light);
                    cmd.Clear();cmd.SetRenderTarget(target);
                    cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,camera.projectionMatrix);
                    cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3,1);
                    Graphics.ExecuteCommandBuffer(cmd);RenderTexture.active=target;
                    read.ReadPixels(new Rect(0,0,width,height),0,0);read.Apply();
                    Color[] colors=read.GetPixels();double weight=0,sum=0,sumY=0;
                    for(int y=0;y<height;y++)for(int x=0;x<width;x++) { float value=colors[y*width+x].r;weight+=value;sum+=value*x;sumY+=value*y; }
                    Require(weight>100,"Production shadow mask was empty.");
                    return new Vector2((float)(sum/weight),(float)(sumY/weight));
                }
                float manual=RenderCentroid(0,new Vector4(1,0,-1,1)).x;
                float left=RenderCentroid(1,new Vector4(1,0,-1,1)).x;
                float right=RenderCentroid(1,new Vector4(-1,0,-1,1)).x;
                Require(left<right-10 && Mathf.Abs(right-manual)<.1f,"Main light did not move actual production mask in the expected direction.");
                float below=RenderCentroid(1,new Vector4(0,1,-1,1)).y;
                float above=RenderCentroid(1,new Vector4(0,-1,-1,1)).y;
                Require(below<above-10,"Vertical main-light shadow offset was reversed.");
                float missing=RenderCentroid(1,Vector4.zero).x;
                Require(Mathf.Abs(missing-manual)<.1f,"Missing main light changed production manual mask.");
                Debug.Log($"HO_CHARACTER_HAIR_SHADOW_PRODUCTION_PASSED manualX={manual:R} lightRightX={left:R} lightLeftX={right:R} lightAboveY={below:R} lightBelowY={above:R} missingLightX={missing:R}");
            }
            finally
            {
                RenderTexture.active=previous;cmd.Release();frames.Dispose();identities.Dispose();
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(read);
                Object.DestroyImmediate(semantic);Object.DestroyImmediate(identity);
            }
        }

        private static bool Demand(string method, HoCharacterSpecializationSettings settings) =>
            (bool)PassType.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,new object[] {settings});

        private static HoCharacterSpecializationSettings Disabled() => new HoCharacterSpecializationSettings {
            eyeRevealEnabled=false, hairDropShadowEnabled=true, faceHairDiffuseEnabled=false,
            subjectOutlineEnabled=false, enhancedOutlineEnabled=false, eyeRevealAngleEnabled=true };

        private static void ValidateCaptureDemand()
        {
            foreach (HoCharacterSpecializationDebugMode mode in Enum.GetValues(typeof(HoCharacterSpecializationDebugMode)))
            {
                var settings=Disabled(); settings.debugMode=mode;
                bool eye = mode == HoCharacterSpecializationDebugMode.All || mode == HoCharacterSpecializationDebugMode.EyeColor
                    || mode == HoCharacterSpecializationDebugMode.EyeAlpha || mode == HoCharacterSpecializationDebugMode.EyeRevealMask;
                bool diffuse = (int)mode >= 5 && (int)mode <= 8 || (int)mode >= 18 && (int)mode <= 21 || mode == HoCharacterSpecializationDebugMode.All;
                Require(Demand("RequiresCharacterCapture",settings)==eye,"Unexpected eye capture demand: "+mode);
                Require(Demand("RequiresFaceCapture",settings)==(eye||diffuse),"Unexpected face capture demand: "+mode);
                Require(Demand("RequiresCompositeEyeColorSample",settings)==(mode==HoCharacterSpecializationDebugMode.EyeColor||mode==HoCharacterSpecializationDebugMode.All),"Unexpected color sample demand: "+mode);
            }
            var volume=ScriptableObject.CreateInstance<HoCharacterSpecializationVolume>();
            var settingsCopy=Disabled();
            try
            {
                volume.Effects.value.hairShadowMainLightInfluence=.73f;
                volume.CopyEffectsTo(settingsCopy);
                Require(Mathf.Approximately(settingsCopy.hairShadowMainLightInfluence,.73f),"Volume lost light influence.");
                var old=new HoCharacterSpecializationEffects(); JsonUtility.FromJsonOverwrite("{\"hairShadowAngleDegrees\":123}",old);
                Require(old.hairShadowMainLightInfluence==0 && old.hairShadowAngleDegrees==123,"Legacy defaults changed.");
            }
            finally { Object.DestroyImmediate(volume); }
        }

        private static void ValidateCompatibilityResources()
        {
            Type type=PassType.Assembly.GetType("lilToon.URP.Extensions.CharacterSpecialization.HoCharacterSpecializationRenderTargets",true);
            object targets=Activator.CreateInstance(type,true);
            MethodInfo allocate=type.GetMethod("ReAllocateIfNeeded");
            var settings=Disabled(); var descriptor=new RenderTextureDescriptor(32,32,RenderTextureFormat.ARGBHalf,24);
            FieldInfo poolField=typeof(UniversalRenderPipeline).GetField("s_RTHandlePool",BindingFlags.Static|BindingFlags.NonPublic);
            bool ownsPool=poolField.GetValue(null)==null;
            if(ownsPool) poolField.SetValue(null,Activator.CreateInstance(poolField.FieldType,true));
            try
            {
                allocate.Invoke(targets,new object[] {descriptor,settings,false,true});
                Require(type.GetProperty("EyeColorTexture").GetValue(targets)==null && type.GetProperty("EyeDataTexture").GetValue(targets)==null,"Non-capture mode allocated capture colors.");
                allocate.Invoke(targets,new object[] {descriptor,settings,true,true});
                Require(type.GetProperty("EyeColorTexture").GetValue(targets)!=null,"Capture colors were not allocated on demand.");
                allocate.Invoke(targets,new object[] {descriptor,settings,false,true});
                Require(type.GetProperty("EyeColorTexture").GetValue(targets)==null && type.GetProperty("EyeDataTexture").GetValue(targets)==null,"Capture colors survived disabling demand.");
            }
            finally
            {
                type.GetMethod("Release").Invoke(targets,null);
                if(ownsPool)
                {
                    poolField.FieldType.GetMethod("Cleanup",Private).Invoke(poolField.GetValue(null),null);
                    poolField.SetValue(null,null);
                }
            }
        }

        private static void ValidateGraph(HoCharacterSpecializationDebugMode mode)
        {
            var graph=new RenderGraph("Character debug demand validation");
            var command=new CommandBuffer(); var frame=new ContextContainer();
            var cameraObject=new GameObject("Character graph validation camera");
            var material=new Material(Shader.Find("Hidden/lilToon-HoCharacterSpecialization/URP/Composite"));
            var semantic=new Material(Shader.Find("Hidden/lilToon-HoCharacterSpecialization/URP/ObjectSemantic"));
            var pass=(ScriptableRenderPass)Activator.CreateInstance(PassType,true);
            bool recording=false;
            try
            {
                object parameters=new RenderGraphParameters { commandBuffer=command };
                typeof(RenderGraphParameters).GetField("invalidContextForTesting",Private).SetValue(parameters,true);
                graph.BeginRecording((RenderGraphParameters)parameters); recording=true;
                TextureHandle input=graph.CreateTexture(new TextureDesc(32,32) {colorFormat=GraphicsFormat.R16G16B16A16_SFloat,name="ValidationInput"});
                var resources=frame.GetOrCreate<UniversalResourceData>();
                typeof(UniversalResourceDataBase).GetMethod("InitFrame",Private).Invoke(resources,null);
                typeof(UniversalResourceData).GetField("_cameraColor",Private).SetValue(resources,input);
                var camera=frame.GetOrCreate<UniversalCameraData>(); camera.camera=cameraObject.AddComponent<Camera>();
                camera.cameraTargetDescriptor=new RenderTextureDescriptor(32,32,RenderTextureFormat.ARGBHalf,24);
                frame.GetOrCreate<UniversalRenderingData>(); frame.GetOrCreate<UniversalLightData>();
                object ac=GetFrame(frame,"AttributeComposite.HoAttributeCompositeRenderGraphResources");
                foreach(string name in new[] {"identityId0Texture","identityId1Texture","identityCoverageTexture"}) ac.GetType().GetField(name).SetValue(ac,input);
                ac.GetType().GetField("laneCount").SetValue(ac,8);
                var selections=(TextureHandle[])ac.GetType().GetField("selectionTextures").GetValue(ac);
                for(int i=0;i<selections.Length;i++) selections[i]=input;
                object geometry=GetFrame(frame,"GeometryBuffer.HoGeometryBufferRenderGraphResources");
                geometry.GetType().GetField("normalDepthTexture").SetValue(geometry,input);
                var settings=Disabled(); settings.debugMode=mode;
                PassType.GetMethod("SetupRenderGraph").Invoke(pass,new object[] {settings,material,null,null,null,semantic});
                pass.RecordRenderGraph(graph,frame);
                var passes=(IList)typeof(RenderGraph).GetField("m_RenderPasses",Private).GetValue(graph);
                Require(passes.Count==2,"Direction/angle-only mode scheduled capture or extra passes: "+mode+" count="+passes.Count);
                Type dataOwner=passes[1].GetType();
                while(dataOwner.GetField("data",Private)==null) dataOwner=dataOwner.BaseType;
                object composite=dataOwner.GetField("data",Private).GetValue(passes[1]);
                Require(!((TextureHandle)composite.GetType().GetField("eyeColorTexture").GetValue(composite)).IsValid()
                    && !((TextureHandle)composite.GetType().GetField("eyeDataTexture").GetValue(composite)).IsValid(),"Graph allocated unneeded capture textures.");
                Debug.Log("HO_CHARACTER_DEBUG_GRAPH_PASSED mode="+mode+" passes="+passes.Count+" capture=0");
            }
            finally
            {
                if(recording) graph.ResetGraphAndLogException(new OperationCanceledException("Recording-only validation"));
                graph.Cleanup(); frame.Dispose(); command.Release();
                PassType.GetMethod("Dispose").Invoke(pass,null);
                Object.DestroyImmediate(material);Object.DestroyImmediate(semantic);Object.DestroyImmediate(cameraObject);
            }
        }

        private static object GetFrame(ContextContainer frame,string suffix) => typeof(ContextContainer).GetMethod("GetOrCreate")
            .MakeGenericMethod(PassType.Assembly.GetType("lilToon.URP.Extensions."+suffix,true)).Invoke(frame,null);
        private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException("[Ho Hair Shadow] "+message); }
    }
}
