using System.Collections.Generic;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    [CustomEditor(typeof(HoCharacterRimLight)), CanEditMultipleObjects]
    public sealed class HoCharacterRimLightEditor : UnityEditor.Editor
    {
        private static readonly GUIContent[] RegionNames =
        {
            new GUIContent("所属整组"), new GUIContent("所属组部件"), new GUIContent("具名选择组")
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("color"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("intensity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("width"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("softness"));
            var region = serializedObject.FindProperty("receiverRegion");
            var kind = region.FindPropertyRelative("kind");
            var name = region.FindPropertyRelative("name");
            EditorGUI.showMixedValue = kind.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(new GUIContent("接收区域", region.tooltip), kind.enumValueIndex, RegionNames);
            if (EditorGUI.EndChangeCheck()) { kind.enumValueIndex = selected; name.stringValue = string.Empty; }
            EditorGUI.showMixedValue = false;
            if (!kind.hasMultipleDifferentValues && kind.enumValueIndex != 0)
            {
                var names = new List<string>();
                bool first = true;
                foreach (HoCharacterRimLight source in targets)
                {
                    var group = source.OwnerGroup;
                    var available = new List<string>();
                    if (group != null && kind.enumValueIndex == (int)HoCharacterRimRegionKind.Part) available.AddRange(group.GetPartNames());
                    else if (group != null && group.selections != null)
                        foreach (var selection in group.selections)
                            if (selection != null && !string.IsNullOrEmpty(selection.name) && !available.Contains(selection.name)) available.Add(selection.name);
                    if (first) { names.AddRange(available); first = false; }
                    else names.RemoveAll(value => !available.Contains(value));
                }
                int current = names.IndexOf(name.stringValue);
                names.Insert(0, string.IsNullOrEmpty(name.stringValue) ? "请选择" : "未匹配：" + name.stringValue);
                EditorGUI.showMixedValue = name.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int next = EditorGUILayout.Popup("区域名称", current + 1, names.ToArray());
                if (EditorGUI.EndChangeCheck() && next > 0) name.stringValue = names[next];
                EditorGUI.showMixedValue = false;
            }
            var castShadows = serializedObject.FindProperty("castShadows");
            EditorGUILayout.PropertyField(castShadows);
            using (new EditorGUI.DisabledScope(!castShadows.hasMultipleDifferentValues && !castShadows.boolValue))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shadowStrength"));
            serializedObject.ApplyModifiedProperties();

            if (targets.Length == 1)
            {
                var source = (HoCharacterRimLight)target;
                var owner = source.OwnerGroup;
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("自动所属 OB", owner, typeof(HoObjectBufferGroup), true);
                    EditorGUILayout.IntField("当前组号", owner != null ? owner.groupId : 0);
                    EditorGUILayout.Vector3Field(new GUIContent("指向光源（世界）", "表面朝向光源的方向，为 Transform 蓝色轴的反方向。"), source.Direction);
                }
                if (owner == null)
                    EditorGUILayout.HelpBox("将空物体放到角色 HoObjectBufferGroup 的子层级，再用 ParentConstraint 跟随骨骼。", MessageType.Warning);
                else if (!owner.isActiveAndEnabled)
                    EditorGUILayout.HelpBox("最近父级 OB 组已停用；启用该组后才能发布此边光。", MessageType.Warning);
                else EditorGUILayout.HelpBox(source.Status, MessageType.Info);
                var unityLight = source.GetComponent<Light>();
                if (unityLight != null && unityLight.enabled)
                    EditorGUILayout.HelpBox("此对象上的 Unity Light 仍参与普通照明；专用 Rim 只使用 Transform。以空物体创建，或关闭该 Unity Light。", MessageType.Warning);
            }
        }

        [DrawGizmo(GizmoType.Selected)]
        private static void DrawDirection(HoCharacterRimLight source, GizmoType type)
        {
            Color color = source.color;
            if (float.IsNaN(color.r) || float.IsInfinity(color.r) || float.IsNaN(color.g) || float.IsInfinity(color.g)
                || float.IsNaN(color.b) || float.IsInfinity(color.b) || color.maxColorComponent < .0001f) color = Color.yellow;
            float maximum = Mathf.Max(.0001f, color.maxColorComponent);
            Color previous = Gizmos.color;
            try
            {
                Gizmos.color = new Color(Mathf.Clamp01(color.r / maximum), Mathf.Clamp01(color.g / maximum),
                    Mathf.Clamp01(color.b / maximum), source.isActiveAndEnabled ? 1 : .35f);
                Vector3 origin = source.transform.position;
                Vector3 direction = source.transform.forward;
                Vector3 tip = origin + direction * .35f;
                Gizmos.DrawWireSphere(origin, .025f);
                Gizmos.DrawLine(origin, tip);
                Gizmos.DrawLine(tip, tip - direction * .07f + source.transform.up * .035f);
                Gizmos.DrawLine(tip, tip - direction * .07f - source.transform.up * .035f);
            }
            finally { Gizmos.color = previous; }
        }

        [MenuItem("GameObject/Rendering/Ho Character Rim Light", false, 30)]
        private static void Create(MenuCommand command)
        {
            var created = new GameObject("Rim Light", typeof(HoCharacterRimLight));
            GameObjectUtility.SetParentAndAlign(created, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(created, "Create Character Rim Light");
            Selection.activeGameObject = created;
        }
    }
}
