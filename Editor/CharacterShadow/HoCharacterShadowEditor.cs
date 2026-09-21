using System.Collections.Generic;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterShadow
{
    [CustomEditor(typeof(HoCharacterShadow))]
    public sealed class HoCharacterShadowEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color StatusColor = new Color(0.45f, 0.64f, 0.96f);

        private static bool showRuntime = true;
        private static bool showStatus = true;

        private readonly BoxBoundsHandle boundsHandle = new BoxBoundsHandle();
        private SerializedProperty objectGroup;
        private SerializedProperty receiverParts;
        private SerializedProperty boundsAnchor;
        private SerializedProperty center;
        private SerializedProperty size;
        private SerializedProperty edgeBlend;

        private void OnEnable()
        {
            objectGroup = serializedObject.FindProperty("objectGroup");
            receiverParts = serializedObject.FindProperty("receiverParts");
            boundsAnchor = serializedObject.FindProperty("boundsAnchor");
            center = serializedObject.FindProperty("center");
            size = serializedObject.FindProperty("size");
            edgeBlend = serializedObject.FindProperty("edgeBlend");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var subject = (HoCharacterShadow)target;

            EditorGUILayout.HelpBox(
                "CS 只提高这个接收盒里的天光投影精度：feature 生成一张围绕盒子的高精度天光深度图，"
                + "lilToon 在盒内用它替换主光实时阴影采样，盒外回退原采样。场景、其他角色、其他部件只要符合天光"
                + "普通投影规则都会参与投影。不改材质：仍然沿用 lilToon 原有的接收开关、Mask 与各层接收强度。",
                MessageType.Info);

            DrawRuntime(subject);
            DrawStatus(subject);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime(HoCharacterShadow subject)
        {
            string summary = (subject.objectGroup != null ? "组 " + subject.objectGroup.groupId : "未指定组")
                + " / " + FormatSize(subject.size);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(objectGroup, "接收组");
                DrawProperty(receiverParts, "接收部件");
                DrawProperty(boundsAnchor, "包围盒锚点");
                DrawProperty(center, "中心");
                DrawProperty(size, "尺寸");
                DrawProperty(edgeBlend, "边缘回退");

                if (GUILayout.Button("从接收对象计算包围盒"))
                {
                    FitBounds(subject);
                }

                EditorGUILayout.HelpBox(
                    "「从接收对象计算包围盒」是一次性工具：按当前姿态把接收部件包进去，不会每帧跟随蒙皮收缩。"
                    + "动作幅度大的角色留出余量，或用场景里的盒手柄直接调整。",
                    MessageType.None);
            }
        }

        private void DrawStatus(HoCharacterShadow subject)
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showStatus, "运行状态", subject.status, StatusColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool hasGroup = subject.objectGroup != null;
                EditorGUILayout.LabelField("接收组", hasGroup ? LilUrpEditorSectionGui.FormatAvailable(true) : LilUrpEditorSectionGui.FormatAvailable(false));
                int matched = CountMatchedParts(subject);
                EditorGUILayout.LabelField("接收部件匹配", matched + " 个");
                EditorGUILayout.LabelField("状态", subject.status);

                if (subject.atlasSlice >= 0)
                {
                    EditorGUILayout.LabelField("图集 Tile", subject.atlasSlice.ToString());
                    EditorGUILayout.LabelField("投影深度", subject.depthRange.ToString("F3") + " m");
                    EditorGUILayout.LabelField("世界单位 / texel", subject.worldUnitsPerTexel.ToString("F6") + " m");
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "当前没有分配到图集 tile，lilToon 用的是普通天光投影。常见原因：接收盒不在当前相机视锥内、"
                        + "图集容量不足（降低单角色分辨率或提高图集上限）、同一个 OB 组挂了多个 CS 组件、"
                        + "接收部件名对不上、feature 未启用或主方向光没开阴影。",
                        MessageType.Info);
                }

                EditorGUILayout.HelpBox(
                    "Atlas / Character 调试画面在 Ho-CharacterShadow Volume 的「调试」分组里选；"
                    + "这里只报这个组件的分配结果。",
                    MessageType.None);
            }
        }

        private static int CountMatchedParts(HoCharacterShadow subject)
        {
            HoObjectBufferGroup group = subject.objectGroup;
            if (group == null || group.groupId <= 0) return 0;
            var renderers = new List<Renderer>();
            group.GetAssignedRenderers(renderers, subject.receiverParts);
            return renderers.Count;
        }

        private static string FormatSize(Vector3 value)
        {
            return "盒 " + value.x.ToString("0.00") + "×" + value.y.ToString("0.00") + "×" + value.z.ToString("0.00");
        }

        private static void DrawProperty(SerializedProperty property, string label)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }

        private void FitBounds(HoCharacterShadow subject)
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
}
