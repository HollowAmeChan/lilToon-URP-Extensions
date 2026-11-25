using System;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class HoCapsuleAOVolumeTests
    {
        [Test]
        public void AOStackEntryDoesNotChangeTheFiveColorCompositionStages()
        {
            var order=HoCharacterCompositionOrder.Normalize(new[]{HoCharacterCompositionEffect.EyeReveal,
                HoCharacterCompositionEffect.CapsuleAO,HoCharacterCompositionEffect.EyeReveal,(HoCharacterCompositionEffect)99});
            Assert.That(order.Length,Is.EqualTo(HoCharacterCompositionOrder.Count));
            Assert.That(order,Does.Contain(HoCharacterCompositionEffect.CapsuleAO));
            HoCharacterCompositionOrder.Pack(order,out Vector4 first,out Vector4 last);
            Assert.That(first,Is.EqualTo(new Vector4(4,0,1,2)));
            Assert.That(last,Is.EqualTo(new Vector4(3,1,0,0)));
            Assert.That(new HoCharacterSpecializationEffects().capsuleAOEnabled,Is.False);
        }

        [Test]
        public void ActualVolumeEditorMeasuresModeFieldsAndResetsOnlyCapsuleParameters()
        {
            var volume=ScriptableObject.CreateInstance<HoCharacterSpecializationVolume>();
            var editor=UnityEditor.Editor.CreateEditor(volume);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            string key="lilToon.CharacterSpecialization.CapsuleAO";
            bool previous=SessionState.GetBool(key,false);
            try
            {
                volume.Effects.value.capsuleAOEnabled=true;
                volume.Effects.value.capsuleAOStrength=.2f;
                volume.Effects.value.faceHairDiffuseStrength=.123f;
                editor.serializedObject.Update();
                editor.GetType().GetMethod("EnsureCompositionOrder",flags).Invoke(editor,null);
                var height=editor.GetType().GetMethod("GetElementHeight",flags);
                SessionState.SetBool(key,true);
                float capsule=(float)height.Invoke(editor,new object[]{5});
                volume.Effects.value.capsuleAOMode=HoCapsuleAOMode.Cross;editor.serializedObject.Update();
                float cross=(float)height.Invoke(editor,new object[]{5});
                Assert.That(cross,Is.GreaterThan(capsule));
                editor.GetType().GetMethod("ResetEffectToDefaults",flags).Invoke(editor,new object[]{5});
                Assert.That(volume.Effects.value.capsuleAOEnabled,Is.False);
                Assert.That(volume.Effects.value.capsuleAOStrength,Is.EqualTo(.75f));
                Assert.That(volume.Effects.value.capsuleAOMode,Is.EqualTo(HoCapsuleAOMode.Capsule));
                Assert.That(volume.Effects.value.faceHairDiffuseStrength,Is.EqualTo(.123f));
                Assert.That(volume.Effects.overrideState,Is.True);
            }
            finally
            {
                SessionState.SetBool(key,previous);Undo.ClearUndo(volume);
                Object.DestroyImmediate(editor);Object.DestroyImmediate(volume);
            }
        }
    }
}
