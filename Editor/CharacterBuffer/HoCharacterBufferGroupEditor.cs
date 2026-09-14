using lilToon.URP.Extensions.CharacterBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterBuffer
{
    /// <summary>
    /// 组件抽屉：把"艺术家看到的名字"和"像素里真正跑的 ID"摆在一起——
    /// ID 由注册表分配，改名就会换 ID，所以这一栏必须能当场看见（规划 §5.3 / §5.7）。
    /// </summary>
    [CustomEditor(typeof(HoCharacterBufferGroup))]
    internal sealed class HoCharacterBufferGroupEditor : UnityEditor.Editor
    {
        private static bool showIdentity = true;
        private SerializedProperty priorityProperty;
        private SerializedProperty characterIdProperty;
        private SerializedProperty characterTagsProperty;
        private SerializedProperty partsProperty;
        private SerializedProperty selectionsProperty;

        private void OnEnable()
        {
            priorityProperty = serializedObject.FindProperty("priority");
            characterIdProperty = serializedObject.FindProperty("characterId");
            characterTagsProperty = serializedObject.FindProperty("characterTags");
            partsProperty = serializedObject.FindProperty("parts");
            selectionsProperty = serializedObject.FindProperty("selections");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "部件名在角色内唯一、选择名全局唯一。名字 → 像素里的整数 ID，属性则全部留在 palette 表里（像素不存属性）。\n" +
                "角色 ID 从 1 开始：0 保留给「未注册」，它也是 RSUV 被重置后的值。",
                MessageType.Info);

            EditorGUILayout.PropertyField(priorityProperty);
            EditorGUILayout.PropertyField(characterIdProperty);
            EditorGUILayout.PropertyField(characterTagsProperty);

            EditorGUILayout.Space();
            showIdentity = EditorGUILayout.Foldout(showIdentity, "已分配的 ID（由注册表编译）", true);
            if (showIdentity)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    DrawIdentityPreview();
                }
            }

            EditorGUILayout.Space();
            DrawParts();

            EditorGUILayout.Space();
            DrawSelections();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawIdentityPreview()
        {
            var group = (HoCharacterBufferGroup)target;
            int characterId = Mathf.Clamp(group.characterId, 0, HoCharacterBufferPaletteLimits.MaxCharacters - 1);
            if (characterId == 0)
            {
                EditorGUILayout.HelpBox("角色 ID 0 被保留，这个 group 不会写进 palette。", MessageType.Error);
                return;
            }

            if (partsProperty == null || partsProperty.arraySize == 0)
            {
                EditorGUILayout.LabelField("（还没有部件）", EditorStyles.miniLabel);
            }

            for (int i = 0; i < (partsProperty?.arraySize ?? 0); i++)
            {
                SerializedProperty part = partsProperty.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = part.FindPropertyRelative("name");
                string partName = nameProperty != null ? nameProperty.stringValue : string.Empty;
                uint partId = HoCharacterBufferRegistry.GetPartId(characterId, partName);
                int row = HoCharacterBufferRegistry.GetPartRow(partId);
                string line = string.IsNullOrEmpty(partName)
                    ? $"槽位 {i}: （空名字，跳过）"
                    : $"槽位 {i}: 0x{partId:X4}（角色 {HoCharacterBufferRegistry.GetCharacterId(partId)} / 槽位 {HoCharacterBufferRegistry.GetSlotId(partId)}），行 {row}";
                EditorGUILayout.LabelField(partName, line, EditorStyles.miniLabel);
            }

            int selectionCount = HoCharacterBufferRegistry.SelectionCount;
            EditorGUILayout.LabelField("选择", $"已注册 {selectionCount} / {HoCharacterBufferPaletteLimits.MaxSelections - 1}", EditorStyles.miniLabel);
            for (int i = 0; i < (selectionsProperty?.arraySize ?? 0); i++)
            {
                SerializedProperty selection = selectionsProperty.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = selection.FindPropertyRelative("name");
                string selectionName = nameProperty != null ? nameProperty.stringValue : string.Empty;
                uint selectionId = HoCharacterBufferRegistry.GetSelectionId(selectionName);
                EditorGUILayout.LabelField(selectionName, selectionId > 0 ? $"选择 ID {selectionId}" : "未注册", EditorStyles.miniLabel);
            }
        }

        private void DrawParts()
        {
            if (partsProperty == null)
            {
                return;
            }

            EditorGUILayout.LabelField($"部件（{partsProperty.arraySize}）", EditorStyles.boldLabel);
            for (int i = 0; i < partsProperty.arraySize; i++)
            {
                SerializedProperty part = partsProperty.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = part.FindPropertyRelative("name");
                string title = nameProperty != null && !string.IsNullOrEmpty(nameProperty.stringValue)
                    ? nameProperty.stringValue
                    : $"部件 {i}";
                EditorGUILayout.PropertyField(part, new GUIContent(title), true);
            }

            if (GUILayout.Button("添加部件"))
            {
                partsProperty.InsertArrayElementAtIndex(partsProperty.arraySize);
            }
        }

        private void DrawSelections()
        {
            if (selectionsProperty == null)
            {
                return;
            }

            EditorGUILayout.LabelField($"选择（{selectionsProperty.arraySize}）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "选择是给材质引用的具名选区（取代 custom0~3 这类匿名通道）。" +
                "材质侧只能引用名字、不能定义名字，所以在这里改名不会破资产。",
                MessageType.None);
            for (int i = 0; i < selectionsProperty.arraySize; i++)
            {
                SerializedProperty selection = selectionsProperty.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = selection.FindPropertyRelative("name");
                string title = nameProperty != null && !string.IsNullOrEmpty(nameProperty.stringValue)
                    ? nameProperty.stringValue
                    : $"选择 {i}";
                EditorGUILayout.PropertyField(selection, new GUIContent(title), true);
            }

            if (GUILayout.Button("添加选择"))
            {
                selectionsProperty.InsertArrayElementAtIndex(selectionsProperty.arraySize);
            }
        }
    }
}
