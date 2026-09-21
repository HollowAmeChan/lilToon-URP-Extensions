using System.Collections.Generic;
using lilToon.URP.Extensions.CharacterShadow;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterShadow
{
    [CustomEditor(typeof(HoCharacterShadow))]
    public sealed class HoCharacterShadowEditor : UnityEditor.Editor
    {
        private readonly BoxBoundsHandle boundsHandle = new BoxBoundsHandle();
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var subject = (HoCharacterShadow)target;
            EditorGUILayout.HelpBox("只提高指定对象的天光投影精度。场景与其他角色自动参与投影；材质沿用环境投影接收设置。", MessageType.Info);
            if (GUILayout.Button("从接收对象计算包围盒")) FitBounds(subject);
            EditorGUILayout.LabelField("当前状态", subject.status);
            if (subject.atlasSlice >= 0)
            {
                EditorGUILayout.LabelField("图集 Tile", subject.atlasSlice.ToString());
                EditorGUILayout.LabelField("投影深度", subject.depthRange.ToString("F3") + " m");
                EditorGUILayout.LabelField("世界单位 / texel", subject.worldUnitsPerTexel.ToString("F6") + " m");
            }
        }

        private static void FitBounds(HoCharacterShadow subject)
        {
            if (subject.objectGroup == null) return;
            var renderers = new List<Renderer>();
            subject.objectGroup.GetAssignedRenderers(renderers, subject.receiverParts);
            bool found = false;
            Bounds result = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds bounds = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = subject.Anchor.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { result = new Bounds(p, Vector3.zero); found = true; }
                    else result.Encapsulate(p);
                }
            }
            if (!found) return;
            Undo.RecordObject(subject, "Fit CS Bounds");
            subject.center = result.center;
            subject.size = Vector3.Max(result.size * 1.1f, Vector3.one * 0.01f);
            EditorUtility.SetDirty(subject);
        }

        private void OnSceneGUI()
        {
            var subject = (HoCharacterShadow)target;
            using (new Handles.DrawingScope(new Color(0.2f, 0.85f, 1), subject.Anchor.localToWorldMatrix))
            {
                boundsHandle.center = subject.center;
                boundsHandle.size = subject.size;
                EditorGUI.BeginChangeCheck();
                boundsHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(subject, "Edit CS Bounds");
                    subject.center = boundsHandle.center;
                    subject.size = Vector3.Max(boundsHandle.size, Vector3.one * 0.01f);
                    EditorUtility.SetDirty(subject);
                }
            }
        }
    }

    [CustomEditor(typeof(HoCharacterShadowRendererFeature))]
    public sealed class HoCharacterShadowRendererFeatureEditor : UnityEditor.Editor
    {
        private void OnEnable()
        {
            serializedObject.Update();
            var shader = serializedObject.FindProperty("settings").FindPropertyRelative("debugShader");
            if (shader.objectReferenceValue == null)
            {
                shader.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(
                    "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowDebug.shader");
                serializedObject.ApplyModifiedProperties();
            }
        }
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("CS 只替换角色的主方向光采样。Debug 显示 atlas / 单角色深度图，不改材质输出。单角色分辨率固定，容量不足时回退普通阴影。", MessageType.Info);
        }
    }
}
