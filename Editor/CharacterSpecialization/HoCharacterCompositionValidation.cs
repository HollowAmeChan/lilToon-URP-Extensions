using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    public static class HoCharacterCompositionValidation
    {
        [MenuItem("HoLil/Validation/Validate Character Composition Order")]
        public static void Validate()
        {
            Require(Application.isBatchMode, "Run in an isolated batch editor; the undo checks use the editor undo/redo history.");
            CheckPacked(null, 0, 1, 2, 3, 4);
            CheckPacked(Array.Empty<HoCharacterCompositionEffect>(), 0, 1, 2, 3, 4);
            CheckPacked(new[] { (HoCharacterCompositionEffect)4, (HoCharacterCompositionEffect)4,
                (HoCharacterCompositionEffect)(-1), (HoCharacterCompositionEffect)99 }, 4, 0, 1, 2, 3);
            CheckPacked(new[] { (HoCharacterCompositionEffect)3, (HoCharacterCompositionEffect)1 }, 3, 1, 0, 2, 4);
            foreach (var order in Permutations()) CheckPacked(order, order.Select(e => (int)e).ToArray());
            ValidateSerializationAndEditor();
            ValidateMaterialBinding();
            Debug.Log("HO_CHARACTER_COMPOSITION_CPU_PASSED permutations=120 legacy/default/invalid/save/undo/layout/binding=passed");
        }

        private static void ValidateSerializationAndEditor()
        {
            var volume = ScriptableObject.CreateInstance<HoCharacterSpecializationVolume>();
            var restored = ScriptableObject.CreateInstance<HoCharacterSpecializationVolume>();
            UnityEditor.Editor editor = null;
            var expansion = new Dictionary<string, bool>();
            try
            {
                JsonUtility.FromJsonOverwrite("{\"eyeRevealStrength\":0.37,\"hairShadowOpacity\":0.61,\"faceHairDiffuseEnabled\":true}", volume.Effects.value);
                Require(Mathf.Approximately(volume.Effects.value.eyeRevealStrength, .37f), "Legacy eye parameter changed");
                Require(Mathf.Approximately(volume.Effects.value.hairShadowOpacity, .61f), "Legacy shadow parameter changed");
                CheckPacked(volume.Effects.value.compositionOrder, 0, 1, 2, 3, 4);
                editor = UnityEditor.Editor.CreateEditor(volume);
                var serialized = editor.serializedObject;
                var order = serialized.FindProperty("Effects").FindPropertyRelative("m_Value").FindPropertyRelative("compositionOrder");
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(volume, "Move Character Composition Effect");
                order.MoveArrayElement(4, 0);
                serialized.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                CheckPacked(volume.Effects.value.compositionOrder, 4, 0, 1, 2, 3);
                Undo.PerformUndo();
                CheckPacked(volume.Effects.value.compositionOrder, 0, 1, 2, 3, 4);
                Undo.PerformRedo();
                CheckPacked(volume.Effects.value.compositionOrder, 4, 0, 1, 2, 3);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(volume), restored);
                CheckPacked(restored.Effects.value.compositionOrder, 4, 0, 1, 2, 3);
                Require(restored.Effects.overrideState && restored.Effects.value.faceHairDiffuseEnabled
                    && Mathf.Approximately(restored.Effects.value.eyeRevealStrength, .37f)
                    && Mathf.Approximately(restored.Effects.value.hairShadowOpacity, .61f), "Save/reorder changed parameters or override");

                serialized.Update();
                var getHeight = editor.GetType().GetMethod("GetElementHeight", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int i = 0; i < 5; i++)
                {
                    string key = "lilToon.CharacterSpecialization." + volume.Effects.value.compositionOrder[i];
                    expansion[key] = SessionState.GetBool(key, false);
                    SessionState.SetBool(key, false);
                    float closed = (float)getHeight.Invoke(editor, new object[] { i });
                    SessionState.SetBool(key, true);
                    float expanded = (float)getHeight.Invoke(editor, new object[] { i });
                    Require(expanded > closed && closed > 0, "Expanded effect row has no measured parameter height");
                }
            }
            finally
            {
                foreach (var state in expansion) SessionState.SetBool(state.Key, state.Value);
                if (editor != null) Object.DestroyImmediate(editor);
                Undo.ClearUndo(volume);
                Object.DestroyImmediate(volume); Object.DestroyImmediate(restored);
            }
        }

        private static void ValidateMaterialBinding()
        {
            var volume = ScriptableObject.CreateInstance<HoCharacterSpecializationVolume>();
            var material = new Material(Shader.Find("Hidden/Ho-CharacterSpecialization/CompositionOrderProbe"));
            try
            {
                volume.Effects.value.compositionOrder = new[] { (HoCharacterCompositionEffect)4, (HoCharacterCompositionEffect)3,
                    (HoCharacterCompositionEffect)2, (HoCharacterCompositionEffect)1, (HoCharacterCompositionEffect)0 };
                var settings = new HoCharacterSpecializationSettings();
                volume.CopyEffectsTo(settings);
                // Changing the source array after resolution cannot change the camera's packed value.
                volume.Effects.value.compositionOrder[0] = HoCharacterCompositionEffect.DropShadow;
                Type pass = typeof(HoCharacterSpecializationSettings).Assembly.GetType("lilToon.URP.Extensions.CharacterSpecialization.HoCharacterSpecializationPass", true);
                var apply = pass.GetMethod("ApplyMaterialProperties", BindingFlags.Static | BindingFlags.NonPublic,
                    null, new[] { typeof(Material), typeof(HoCharacterSpecializationSettings), typeof(float) }, null);
                apply.Invoke(null, new object[] { material, settings, 1f });
                Require(material.GetVector(HoCharacterSpecializationShaderConstants.CompositionOrderFirstId) == new Vector4(4,3,2,1)
                    && material.GetVector(HoCharacterSpecializationShaderConstants.CompositionOrderLastId).x == 0, "Resolved camera/material order was not copied");
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(volume); }
        }

        public static void ValidateGpu()
        {
            Validate();
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            RenderTexture previousTarget = RenderTexture.active;
            var material = new Material(Shader.Find("Hidden/Ho-CharacterSpecialization/CompositionOrderProbe"));
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var image = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            float maximum = 0; int cases = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false; target.Create();
                material.SetVector("_TestSource", new Vector4(.25f,.4f,.6f,.37f));
                material.SetVector("_TestEye", new Vector4(.9f,.25f,.1f,0));
                material.SetVector("_TestDiffuse", new Vector4(.3f,1.2f,.2f,0));
                material.SetVector("_TestEnhanced", new Vector4(.1f,.4f,1.3f,0));
                material.SetVector("_TestSubject", new Vector4(.7f,.2f,.6f,0));
                material.SetVector("_HoCharacterHairShadowColor", new Vector4(.2f,.3f,.4f,1));
                material.SetVector("_TestAmounts", new Vector4(.7f,.65f,.4f,.2f));
                foreach (var order in Permutations())
                for (int shadow = 0; shadow < 2; shadow++)
                for (int diffuse = 0; diffuse < 3; diffuse++)
                for (int fog = 0; fog < 2; fog++)
                {
                    SetOrder(material, order);
                    material.SetVector("_HoCharacterHairShadowParams1", new Vector4(0,shadow,0,0));
                    material.SetVector("_HoCharacterFaceHairDiffuseParams", new Vector4(0,0,0,diffuse));
                    material.SetVector("_TestSubjectOptions", new Vector4(.55f,fog,0,0));
                    Color actual = Read(material, target, image);
                    Vector3 expected = Reference(order, shadow, diffuse, fog != 0);
                    float error = Mathf.Max(Mathf.Abs(actual.r-expected.x), Mathf.Abs(actual.g-expected.y), Mathf.Abs(actual.b-expected.z), Mathf.Abs(actual.a-.37f));
                    maximum = Mathf.Max(maximum,error); cases++;
                    Require(error < .003f, $"GPU order/blend mismatch: {error}, order={string.Join(",",order)}");
                }
                var defaults = HoCharacterCompositionOrder.CreateDefault();
                material.SetVector("_HoCharacterHairShadowParams1", Vector4.zero);
                material.SetVector("_HoCharacterFaceHairDiffuseParams", new Vector4(0,0,0,1));
                material.SetVector("_TestSubjectOptions", new Vector4(.55f,0,0,0));
                SetOrder(material, defaults); Color eyeLast = Read(material,target,image);
                SetOrder(material, new[] { HoCharacterCompositionEffect.EyeReveal, HoCharacterCompositionEffect.DropShadow,
                    HoCharacterCompositionEffect.FaceHairDiffuse, HoCharacterCompositionEffect.EnhancedOutline, HoCharacterCompositionEffect.SubjectOutline });
                Color eyeFirst = Read(material,target,image);
                Require(Mathf.Abs(eyeLast.r-eyeFirst.r) > .05f, "Reordering changed no actual output");
                material.SetVector(HoCharacterSpecializationShaderConstants.CompositionOrderLastId, Vector4.zero);
                Color unbound = Read(material,target,image);
                Require(Mathf.Abs(unbound.r-eyeLast.r) < .003f, "Unbound order did not default to EyeReveal last");
                material.SetVector("_TestAmounts", Vector4.zero); material.SetVector("_TestSubjectOptions", Vector4.zero);
                Color disabled = Read(material,target,image);
                Require(Mathf.Abs(disabled.r-.25f)<.003f && Mathf.Abs(disabled.g-.4f)<.003f && Mathf.Abs(disabled.b-.6f)<.003f,
                    "Disabled effects changed the source");
                Shader production = Shader.Find("Hidden/lilToon-HoCharacterSpecialization/URP/Composite");
                var productionMaterial = new Material(production);
                try { ShaderUtil.CompilePass(productionMaterial, 0, true); Require(!ShaderUtil.ShaderHasError(production), "Production composite shader failed compilation"); }
                finally { Object.DestroyImmediate(productionMaterial); }
                Debug.Log($"HO_CHARACTER_COMPOSITION_GPU_PASSED api={SystemInfo.graphicsDeviceType} cases={cases} maxError={maximum:R} eyeLast={eyeLast.r:R} eyeFirst={eyeFirst.r:R}");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previousAsync; RenderTexture.active = previousTarget;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image); Object.DestroyImmediate(material);
            }
        }

        private static void SetOrder(Material material, HoCharacterCompositionEffect[] order)
        {
            HoCharacterCompositionOrder.Pack(order,out Vector4 first,out Vector4 last);
            material.SetVector(HoCharacterSpecializationShaderConstants.CompositionOrderFirstId,first);
            material.SetVector(HoCharacterSpecializationShaderConstants.CompositionOrderLastId,last);
        }
        private static Color Read(Material material, RenderTexture target, Texture2D image)
        {
            Graphics.Blit(null,target,material); RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1,1),0,0); image.Apply(); return image.GetPixel(0,0);
        }
        private static Vector3 Reference(HoCharacterCompositionEffect[] order, int shadow, int diffuse, bool subjectFog)
        {
            Vector3 c=new Vector3(.25f,.4f,.6f);
            foreach(var effect in order)
                switch(effect)
                {
                    case HoCharacterCompositionEffect.DropShadow:
                        Vector3 tint=new Vector3(.2f,.3f,.4f); c=shadow==0?Vector3.Scale(c,Vector3.Lerp(Vector3.one,tint,.65f)):Vector3.Lerp(c,tint,.65f); break;
                    case HoCharacterCompositionEffect.FaceHairDiffuse:
                        Vector3 d=new Vector3(.3f,1.2f,.2f); Vector3 result=diffuse==0?d:diffuse==1?c+d:Screen(c,d);
                        c=Vector3.Lerp(c,Vector3.Max(result,Vector3.zero),.4f); break;
                    case HoCharacterCompositionEffect.EnhancedOutline: c=Fog(c,new Vector3(.1f,.4f,1.3f),.2f); break;
                    case HoCharacterCompositionEffect.SubjectOutline: c=subjectFog?Fog(c,new Vector3(.7f,.2f,.6f),.55f):Vector3.Lerp(c,new Vector3(.7f,.2f,.6f),.55f); break;
                    case HoCharacterCompositionEffect.EyeReveal: c=Vector3.Lerp(c,new Vector3(.9f,.25f,.1f),.7f); break;
                }
            return c;
        }
        private static Vector3 Screen(Vector3 a,Vector3 b) => Vector3.one-Vector3.Scale(Vector3.one-Clamp(a),Vector3.one-Clamp(b));
        private static Vector3 Clamp(Vector3 v) => new Vector3(Mathf.Clamp01(v.x),Mathf.Clamp01(v.y),Mathf.Clamp01(v.z));
        private static Vector3 Fog(Vector3 a,Vector3 b,float amount) => Vector3.Lerp(a,Vector3.Max(a,Screen(a,b)+Vector3.Max(b-Clamp(b),Vector3.zero)),amount);
        private static void CheckPacked(IReadOnlyList<HoCharacterCompositionEffect> order, params int[] expected)
        {
            HoCharacterCompositionOrder.Pack(order,out Vector4 first,out Vector4 last);
            for(int i=0;i<5;i++) Require((int)(i<4?first[i]:last.x)==expected[i],"Packed order mismatch");
            Require(last.y==1,"Missing packed-order validity flag");
        }
        private static IEnumerable<HoCharacterCompositionEffect[]> Permutations()
        {
            for(int a=0;a<5;a++) for(int b=0;b<5;b++) for(int c=0;c<5;c++) for(int d=0;d<5;d++) for(int e=0;e<5;e++)
                if(a!=b&&a!=c&&a!=d&&a!=e&&b!=c&&b!=d&&b!=e&&c!=d&&c!=e&&d!=e)
                    yield return new[] {(HoCharacterCompositionEffect)a,(HoCharacterCompositionEffect)b,(HoCharacterCompositionEffect)c,(HoCharacterCompositionEffect)d,(HoCharacterCompositionEffect)e};
        }
        private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException("[Ho Character Composition] "+message); }
    }
}
