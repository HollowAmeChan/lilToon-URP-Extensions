using lilToon.URP.Extensions.AOV;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.AOV
{
    [CustomEditor(typeof(HoAovSubject))]
    internal sealed class HoAovSubjectEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "HoAovSubject 是高级/兼容覆盖组件。普通的角色、脸、前发、眼睛等 Object AOV 分组请使用 HoAovGroup。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "这个组件会通过 MaterialPropertyBlock 覆盖系统 AOV、ID、厚度、曲率或材质 custom 值。MPB 可能影响 SRP Batcher；只在需要覆盖材质默认值或兼容旧流程时使用。",
                MessageType.Warning);

            DrawSystemSection();
            DrawIdSection();
            DrawSurfaceSection();
            DrawMaterialCustomSection();

            bool changed = serializedObject.ApplyModifiedProperties();
            if (GUILayout.Button("立即应用到子级 Renderer"))
            {
                changed = true;
            }

            if (changed)
            {
                foreach (Object targetObject in targets)
                {
                    if (targetObject is HoAovSubject subject)
                    {
                        subject.ApplyToRenderers();
                        EditorUtility.SetDirty(subject);
                    }
                }
            }
        }

        private void DrawSystemSection()
        {
            EditorGUILayout.Space(6.0f);
            EditorGUILayout.LabelField("系统 AOV 覆盖", EditorStyles.boldLabel);
            DrawProperty("systemWriteChannels");
            DrawProperty("maskWeight");
        }

        private void DrawIdSection()
        {
            EditorGUILayout.Space(6.0f);
            EditorGUILayout.LabelField("旧式 ID / Flags 覆盖", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Object AOV 的 CharacterId / PartId / Flags 优先由 HoAovGroup 写入 RSUV。这里的 ID 字段只作为旧 MPB 覆盖路径保留。",
                MessageType.None);
            DrawProperty("groupId");
            DrawProperty("objectId");
            DrawProperty("flags");
            DrawProperty("materialClass");
            DrawProperty("utility");
            DrawProperty("debugColor");
        }

        private void DrawSurfaceSection()
        {
            EditorGUILayout.Space(6.0f);
            EditorGUILayout.LabelField("表面数据覆盖", EditorStyles.boldLabel);
            DrawProperty("thickness");
            DrawProperty("curvature");
        }

        private void DrawMaterialCustomSection()
        {
            EditorGUILayout.Space(6.0f);
            EditorGUILayout.LabelField("材质自定义通道 0..3 覆盖", EditorStyles.boldLabel);
            DrawProperty("overrideMaterialCustomChannels");
            SerializedProperty overrideCustom = serializedObject.FindProperty("overrideMaterialCustomChannels");
            if (overrideCustom == null || !overrideCustom.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "关闭时使用材质面板里的 HoCustomAOV / 材质自定义通道 0..3。打开后才会用下面的 MPB 值覆盖。",
                    MessageType.None);
                return;
            }

            DrawProperty("customWriteMask");
            DrawProperty("customValues", true);
        }

        private void DrawProperty(string propertyName, bool includeChildren = false)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, includeChildren);
            }
        }
    }
}
