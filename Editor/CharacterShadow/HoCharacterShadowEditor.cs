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
        private static readonly GUIContent[] ReceiverModes =
        {
            new GUIContent("整组", "接收该 OB 组的所有部件，包括以后新增的部件。"),
            new GUIContent("指定部件", "从该 OB 组已有的部件中勾选接收对象。")
        };

        private static bool showRuntime = true;
        private static bool showStatus = true;

        private readonly BoxBoundsHandle boundsHandle = new BoxBoundsHandle();
        private SerializedProperty objectGroup;
        private SerializedProperty receiveWholeGroup;
        private SerializedProperty receiverParts;
        private SerializedProperty boundsAnchor;
        private SerializedProperty center;
        private SerializedProperty size;
        private SerializedProperty edgeBlend;

        private void OnEnable()
        {
            objectGroup = serializedObject.FindProperty("objectGroup");
            receiveWholeGroup = serializedObject.FindProperty("receiveWholeGroup");
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
                "只提高这个接收盒里的天光投影精度，不改材质（沿用 lilToon 原有的接收开关与强度）。",
                MessageType.Info);

            DrawRuntime(subject);
            serializedObject.ApplyModifiedProperties();
            DrawStatus(subject);
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
                DrawReceiverParts();
                DrawProperty(boundsAnchor, "包围盒锚点");
                DrawProperty(center, "中心");
                DrawProperty(size, "尺寸");
                DrawProperty(edgeBlend, "边缘回退");

                using (new EditorGUI.DisabledScope(objectGroup.objectReferenceValue == null))
                {
                    if (GUILayout.Button(new GUIContent("从接收对象计算包围盒",
                        "按当前姿态计算一次包围盒，不会每帧跟随蒙皮。动作幅度大的角色需预留空间，或用场景手柄调整。")))
                    {
                        // FitBounds reads the component, so commit this event's selection first.
                        serializedObject.ApplyModifiedProperties();
                        FitBounds(subject);
                        serializedObject.Update();
                    }
                }
            }
        }

        private void DrawReceiverParts()
        {
            var group = objectGroup.objectReferenceValue as HoObjectBufferGroup;
            var names = new List<string>();
            if (group != null)
            {
                foreach (var part in group.parts)
                    if (part != null && !string.IsNullOrEmpty(part.name) && !names.Contains(part.name))
                        names.Add(part.name);
            }

            int mode = receiveWholeGroup.boolValue ? 0 : 1;
            Rect modeRect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(modeRect, new GUIContent("接收范围"), receiveWholeGroup);
            mode = EditorGUI.Popup(modeRect, new GUIContent("接收范围"), mode, ReceiverModes);
            receiveWholeGroup.boolValue = mode == 0;
            EditorGUI.EndProperty();

            if (group != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("配置 OB 部件…", "在 OB 组的「部件 → 渲染器」中拖入物体。"),
                        EditorStyles.miniButton, GUILayout.Width(110)))
                    {
                        serializedObject.ApplyModifiedProperties();
                        Selection.activeObject = group;
                        EditorGUIUtility.PingObject(group);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            string warning = group == null ? "先指定接收组；物体在 OB 的「部件 → 渲染器」中设置。"
                : names.Count == 0 ? "OB 组还没有具名部件，请先配置 OB 部件与渲染器。" : null;

            if (group != null && mode == 1)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("勾选接收部件", EditorStyles.boldLabel);
                        if (GUILayout.Button("全选", EditorStyles.miniButtonLeft, GUILayout.Width(42)))
                        {
                            receiverParts.ClearArray();
                            foreach (string name in names) AddReceiverPart(name);
                        }
                        if (GUILayout.Button("清空", EditorStyles.miniButtonRight, GUILayout.Width(42)))
                            receiverParts.ClearArray();
                    }
                    foreach (string name in names)
                    {
                        bool selected = HasReceiverPart(name);
                        var label = new GUIContent(name, "接收 OB 部件「" + name + "」中的渲染器。物体在 OB 中配置。");
                        bool next = EditorGUILayout.ToggleLeft(label, selected);
                        if (next != selected)
                        {
                            if (next) AddReceiverPart(name);
                            else RemoveReceiverPart(name);
                        }
                    }
                }
                if (names.Count > 0 && !names.Exists(HasReceiverPart))
                    warning = "尚未勾选接收部件，当前不接收高精度阴影。";
            }
            if (warning != null) EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }

        private bool HasReceiverPart(string name)
        {
            for (int i = 0; i < receiverParts.arraySize; i++)
                if (receiverParts.GetArrayElementAtIndex(i).stringValue == name) return true;
            return false;
        }

        private void AddReceiverPart(string name)
        {
            int index = receiverParts.arraySize;
            receiverParts.InsertArrayElementAtIndex(index);
            receiverParts.GetArrayElementAtIndex(index).stringValue = name;
        }

        private void RemoveReceiverPart(string name)
        {
            for (int i = receiverParts.arraySize - 1; i >= 0; i--)
                if (receiverParts.GetArrayElementAtIndex(i).stringValue == name)
                    receiverParts.DeleteArrayElementAtIndex(i);
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
                int matched = CountMatchedRenderers(subject);
                EditorGUILayout.LabelField("接收渲染器", matched + " 个");
                if (hasGroup && matched == 0)
                    EditorGUILayout.HelpBox("当前没有接收对象。请检查 OB 部件的渲染器配置和上方的接收范围。", MessageType.Warning);
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
                        "没分配到 tile（正在用普通天光投影）：盒不在相机视锥内 / 图集容量不足 / 同组挂了多个 CS 组件 / "
                        + "接收部件名对不上 / feature 未启用或主光没开阴影。",
                        MessageType.Info);
                }
            }
        }

        private static int CountMatchedRenderers(HoCharacterShadow subject)
        {
            HoObjectBufferGroup group = subject.objectGroup;
            if (group == null || group.groupId <= 0) return 0;
            var renderers = new List<Renderer>();
            subject.GetReceiverRenderers(renderers);
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
            subject.GetReceiverRenderers(renderers);
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
