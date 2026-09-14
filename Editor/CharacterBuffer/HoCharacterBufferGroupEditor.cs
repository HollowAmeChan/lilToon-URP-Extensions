using lilToon.URP.Extensions.CharacterBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterBuffer
{
    /// <summary>
    /// 组件抽屉：沿用 MetadataBuffer group 的旧款式（彩色通道条 + `+` / `×` + 拖拽 + 全场景刷新），
    /// 但条目本身换成了新模型——部件是具名的、类别/标签/材质属性都挂在条目上，选择也是具名的。
    /// 每条通道条上还会显示**注册表实际分配的 ID**：改名就会换 ID，所以这一栏必须当场看得见。
    /// </summary>
    [CustomEditor(typeof(HoCharacterBufferGroup))]
    [CanEditMultipleObjects]
    internal sealed class HoCharacterBufferGroupEditor : UnityEditor.Editor
    {
        private const float Spacing = 6.0f;
        private const float ChannelHeaderHeight = 32.0f;
        private const float ChannelElementHeight = 22.0f;
        private const float ChannelButtonWidth = 22.0f;

        private static readonly Color[] EntryColors =
        {
            new Color(0.35f, 0.58f, 0.95f),
            new Color(0.95f, 0.48f, 0.50f),
            new Color(0.38f, 0.76f, 0.55f),
            new Color(0.96f, 0.70f, 0.33f),
            new Color(0.55f, 0.48f, 0.90f),
            new Color(0.30f, 0.72f, 0.78f),
            new Color(0.78f, 0.64f, 0.42f),
            new Color(0.55f, 0.55f, 0.55f)
        };

        private static readonly Color SelectionColor = new Color(0.80f, 0.55f, 0.85f);
        private static readonly Color IdentityColor = new Color(0.40f, 0.62f, 0.78f);
        private static readonly GUIContent AddSlotLabel = new GUIContent("+", "添加空槽");
        private static readonly GUIContent ClearLabel = new GUIContent("×", "清空本通道");
        private static readonly GUIContent RemoveEntryLabel = new GUIContent("删除这个部件", "从部件表里删掉这一条（像素里的 ID 会随之重排）。");
        private static readonly GUIContent RemoveSelectionLabel = new GUIContent("删除这个选择", "从选择表里删掉这一条（材质里对它的引用会失效）。");
        private static readonly GUIContent RefreshLabel = new GUIContent("刷新全场景 RSUV", "重新编译 palette 并把 RSUV 索引写回所有 renderer（RSUV 不会被序列化，重载后必须重写）。");
        private static GUIStyle entryNameStyle;

        private SerializedProperty priorityProperty;
        private SerializedProperty characterIdProperty;
        private SerializedProperty characterTagsProperty;
        private SerializedProperty includeChildrenProperty;
        private SerializedProperty faceBoneProperty;
        private SerializedProperty faceForwardAxisProperty;
        private SerializedProperty faceRightAxisProperty;
        private SerializedProperty faceUpAxisProperty;
        private SerializedProperty partsProperty;
        private SerializedProperty selectionsProperty;
        private string validationMessage;

        private void OnEnable()
        {
            priorityProperty = serializedObject.FindProperty("priority");
            characterIdProperty = serializedObject.FindProperty("characterId");
            characterTagsProperty = serializedObject.FindProperty("characterTags");
            includeChildrenProperty = serializedObject.FindProperty("includeChildrenForListedObjects");
            faceBoneProperty = serializedObject.FindProperty("faceBone");
            faceForwardAxisProperty = serializedObject.FindProperty("faceForwardAxis");
            faceRightAxisProperty = serializedObject.FindProperty("faceRightAxis");
            faceUpAxisProperty = serializedObject.FindProperty("faceUpAxis");
            partsProperty = serializedObject.FindProperty("parts");
            selectionsProperty = serializedObject.FindProperty("selections");
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();
            validationMessage = null;

            // 让"已分配的 ID"这一栏显示的是最新表（只在标脏后才真正重建）。
            HoCharacterBufferRegistry.EnsureBuilt();

            DrawIdentitySection();
            DrawPartsSection();
            DrawSelectionsSection();
            DrawFooter();

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                ApplyTargets();
            }
        }

        private void DrawIdentitySection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(priorityProperty, new GUIContent("优先级", "同一个 Renderer 被多个 HoCharacterBufferGroup 命中时，优先级高者生效；相同时离 Renderer 最近的组生效。"));
                DrawProperty(characterIdProperty, new GUIContent("角色 ID", "1-255。角色 0 保留：ID 0 表示「未注册」，它也是 RSUV 被重置后的值。"));
                DrawProperty(characterTagsProperty, new GUIContent("角色级标签", "放在角色表那一行，用于「整角色」语义（例如 CharacterFull），不必在每个部件行重复。"));
                DrawProperty(includeChildrenProperty, new GUIContent("展开子级", "拖入 GameObject 或预制件实例时，包含它下面的子级 Renderer；关闭时只使用物体自身的 Renderer。"));
                EditorGUILayout.Space(2.0f);
                DrawProperty(faceBoneProperty, new GUIContent("面部朝向", "确定角色面部朝向的 Transform——可以是骨骼，也可以是一个朝向正确的空物体。仅供各消费者系统读取（眼透相机角度修正、未来的 SDF 等）；留空表示未提供。以 Transform 的局部轴配合下方三个轴向设置来定义脸前/右/上。"));
                DrawProperty(faceForwardAxisProperty, new GUIContent("脸前轴", "骨骼的哪个局部轴作为“脸前方”。默认 +Z。若正面/侧面的衰减方向反了，换成 +Z / -Z 试试。"));
                DrawProperty(faceRightAxisProperty, new GUIContent("右轴", "骨骼的哪个局部轴作为“角色右侧（画面左侧）”。默认 +X。"));
                DrawProperty(faceUpAxisProperty, new GUIContent("上轴", "骨骼的哪个局部轴作为“角色上方”。默认 +Y。俯仰角按此轴分解，若俯视/仰视不生效请检查此项。"));

                int characterId = Mathf.Clamp(characterIdProperty != null ? characterIdProperty.intValue : 0, 0, HoCharacterBufferPaletteLimits.MaxCharacters - 1);
                if (characterId == 0)
                {
                    validationMessage = "角色 ID 0 被保留，这个 group 不会写进 palette。请改成 1-255。";
                }
            }
        }

        private void DrawPartsSection()
        {
            EditorGUILayout.Space(Spacing);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (partsProperty == null)
                {
                    return;
                }

                for (int i = 0; i < partsProperty.arraySize; i++)
                {
                    DrawPartEntry(i);
                }

                if (partsProperty.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("还没有部件。每个部件 = 一个具名条目（类别 / 标签 / 材质属性）+ 它包含的 Renderer。", MessageType.None);
                }

                EditorGUILayout.Space(2.0f);
                if (GUILayout.Button("+ 添加部件"))
                {
                    int index = partsProperty.arraySize;
                    partsProperty.InsertArrayElementAtIndex(index);
                    SerializedProperty entry = partsProperty.GetArrayElementAtIndex(index);
                    SerializedProperty nameProperty = entry.FindPropertyRelative("name");
                    if (nameProperty != null)
                    {
                        nameProperty.stringValue = $"部件 {index}";
                    }

                    entry.isExpanded = true;
                }
            }
        }

        private void DrawPartEntry(int index)
        {
            SerializedProperty entry = partsProperty.GetArrayElementAtIndex(index);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            SerializedProperty categoryProperty = entry.FindPropertyRelative("category");
            SerializedProperty renderersProperty = entry.FindPropertyRelative("renderers");
            string partName = nameProperty != null ? nameProperty.stringValue : string.Empty;

            Color color = EntryColors[index % EntryColors.Length];
            string title = string.IsNullOrEmpty(partName) ? $"（空名字 · 槽位 {index}）" : partName;
            string idText = BuildPartIdText(index, partName, categoryProperty);
            DrawEntryHeader(entry, title, idText, color, renderersProperty, true, false);

            if (!entry.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            DrawProperty(nameProperty, new GUIContent("名字", "角色内唯一。它决定槽位号 = 像素里 ID 的低字节。"));
            DrawProperty(categoryProperty, new GUIContent("类别", "单值，回答「这是什么」。多归属语义请用标签位。"));
            DrawProperty(entry.FindPropertyRelative("tags"), new GUIContent("标签", "位掩码。像 CharacterFull 这种「多归属」语义用标签最自然，消费端一次 & 即可查询。"));
            DrawProperty(entry.FindPropertyRelative("materialClass"), new GUIContent("材质分类"));
            DrawProperty(entry.FindPropertyRelative("thickness"), new GUIContent("厚度 (SSS)"));
            DrawProperty(entry.FindPropertyRelative("curvature"), new GUIContent("曲率"));
            DrawProperty(entry.FindPropertyRelative("transmittance"), new GUIContent("透射提示"));
            DrawProperty(entry.FindPropertyRelative("roughness"), new GUIContent("粗糙度"));
            DrawProperty(entry.FindPropertyRelative("metallic"), new GUIContent("金属度"));
            DrawProperty(entry.FindPropertyRelative("reflectance"), new GUIContent("反射率"));
            DrawProperty(entry.FindPropertyRelative("plrStrength"), new GUIContent("平面反射强度"));
            DrawProperty(entry.FindPropertyRelative("displayColor"), new GUIContent("显示色", "debug 与 Nuke color picker 用的颜色；像素里不存颜色，只存 ID。"));
            DrawProperty(entry.FindPropertyRelative("includeChildren"), new GUIContent("展开子级"));

            EditorGUILayout.Space(2.0f);
            DrawObjectList(renderersProperty, color, $"{title} · Renderer");

            EditorGUILayout.Space(2.0f);
            if (GUILayout.Button(RemoveEntryLabel))
            {
                DeleteArrayElement(partsProperty, index);
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(Spacing);
        }

        private void DrawSelectionsSection()
        {
            EditorGUILayout.Space(Spacing);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (selectionsProperty == null)
                {
                    return;
                }

                EditorGUILayout.LabelField(
                    new GUIContent("选择", "具名的选区，取代 custom0~3 这类匿名通道。材质侧只能引用名字、不能定义名字，所以在这里改名不会破资产。"),
                    EditorStyles.miniLabel);

                for (int i = 0; i < selectionsProperty.arraySize; i++)
                {
                    DrawSelectionEntry(i);
                }

                if (selectionsProperty.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("还没有选择。注册了选择才会分配那张选择图（没注册就不产出）。", MessageType.None);
                }

                EditorGUILayout.Space(2.0f);
                if (GUILayout.Button("+ 添加选择"))
                {
                    int index = selectionsProperty.arraySize;
                    selectionsProperty.InsertArrayElementAtIndex(index);
                    SerializedProperty entry = selectionsProperty.GetArrayElementAtIndex(index);
                    SerializedProperty nameProperty = entry.FindPropertyRelative("name");
                    if (nameProperty != null)
                    {
                        nameProperty.stringValue = $"选择 {index}";
                    }

                    entry.isExpanded = true;
                }
            }
        }

        private void DrawSelectionEntry(int index)
        {
            SerializedProperty entry = selectionsProperty.GetArrayElementAtIndex(index);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            string selectionName = nameProperty != null ? nameProperty.stringValue : string.Empty;
            uint selectionId = HoCharacterBufferRegistry.GetSelectionId(selectionName);
            string title = string.IsNullOrEmpty(selectionName) ? $"（空名字 · {index}）" : selectionName;
            string idText = selectionId > 0 ? $"选择 ID {selectionId}" : "未注册";

            DrawEntryHeader(entry, title, idText, SelectionColor, null, false, true);

            if (!entry.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            DrawProperty(nameProperty, new GUIContent("名字", "全局唯一。材质里引用的是这个名字。"));
            DrawProperty(entry.FindPropertyRelative("tags"), new GUIContent("标签"));
            DrawProperty(entry.FindPropertyRelative("displayColor"), new GUIContent("显示色", "debug 与 AOV manifest 用的颜色。"));
            EditorGUILayout.Space(2.0f);
            if (GUILayout.Button(RemoveSelectionLabel))
            {
                DeleteArrayElement(selectionsProperty, index);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(Spacing);
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(Spacing);
            using (new EditorGUILayout.HorizontalScope())
            {
                int partRows = Mathf.Max(0, HoCharacterBufferRegistry.PartRowCount - 1);
                EditorGUILayout.LabelField(
                    new GUIContent($"已注册：部件 {partRows} / {HoCharacterBufferPaletteLimits.MaxPartRows}，选择 {HoCharacterBufferRegistry.SelectionCount} / {HoCharacterBufferPaletteLimits.MaxSelections - 1}"),
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(RefreshLabel, GUILayout.Width(150.0f)))
                {
                    RefreshScene();
                }
            }

            if (!string.IsNullOrEmpty(validationMessage))
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
            }
        }

        private string BuildPartIdText(int slot, string partName, SerializedProperty categoryProperty)
        {
            var group = target as HoCharacterBufferGroup;
            int characterId = Mathf.Clamp(characterIdProperty != null ? characterIdProperty.intValue : (group != null ? group.characterId : 0), 0, HoCharacterBufferPaletteLimits.MaxCharacters - 1);
            if (group == null || characterId == 0 || string.IsNullOrEmpty(partName))
            {
                return $"槽位 {slot} · 未注册";
            }

            uint partId = HoCharacterBufferRegistry.GetPartId(characterId, partName);
            if (partId == 0u)
            {
                return $"槽位 {slot} · 未注册";
            }

            string category = categoryProperty != null && categoryProperty.enumDisplayNames != null && categoryProperty.enumValueIndex >= 0 && categoryProperty.enumValueIndex < categoryProperty.enumDisplayNames.Length
                ? categoryProperty.enumDisplayNames[categoryProperty.enumValueIndex]
                : string.Empty;
            return string.IsNullOrEmpty(category)
                ? $"0x{partId:X4} · 槽位 {slot}"
                : $"0x{partId:X4} · 槽位 {slot} · {category}";
        }

        private void DrawEntryHeader(SerializedProperty entry, string title, string subtitle, Color color, SerializedProperty listProperty, bool addSlot, bool removeEntry)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, ChannelHeaderHeight);
            Event currentEvent = Event.current;
            bool hover = rect.Contains(currentEvent.mousePosition);
            bool dragging = hover && (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform);

            bool hasList = listProperty != null && listProperty.isArray;
            bool dropTarget = hasList && dragging;
            EditorGUI.DrawRect(rect, GetChannelColor(color, hover, dropTarget, false));

            Rect foldoutRect = new Rect(rect.x + 4.0f, rect.y + 7.0f, 14.0f, EditorGUIUtility.singleLineHeight);
            entry.isExpanded = EditorGUI.Foldout(foldoutRect, entry.isExpanded, GUIContent.none, true);

            Rect labelRect = new Rect(rect.x + 24.0f, rect.y + 6.0f, rect.width - 128.0f, 20.0f);
            GUI.Label(labelRect, title, entryNameStyle);

            Rect subtitleRect = new Rect(rect.x + 24.0f, rect.y + 17.0f, rect.width - 128.0f, 14.0f);
            GUI.Label(subtitleRect, subtitle, EditorStyles.miniLabel);

            Rect countRect = new Rect(rect.xMax - 96.0f, rect.y + 7.0f, 40.0f, 18.0f);
            if (hasList)
            {
                GUI.Label(countRect, $"{listProperty.arraySize} 项", EditorStyles.miniLabel);
            }

            Rect addRect = new Rect(rect.xMax - 52.0f, rect.y + 6.0f, ChannelButtonWidth, 20.0f);
            Rect clearRect = new Rect(rect.xMax - 28.0f, rect.y + 6.0f, ChannelButtonWidth, 20.0f);
            if (hasList && addSlot)
            {
                if (GUI.Button(addRect, AddSlotLabel, EditorStyles.miniButtonLeft))
                {
                    InsertEmptySlot(listProperty);
                }

                using (new EditorGUI.DisabledScope(listProperty.arraySize == 0))
                {
                    if (GUI.Button(clearRect, ClearLabel, EditorStyles.miniButtonRight))
                    {
                        listProperty.ClearArray();
                    }
                }
            }
            else if (removeEntry)
            {
                if (GUI.Button(clearRect, ClearLabel, EditorStyles.miniButtonRight))
                {
                    listProperty?.ClearArray();
                }
            }

            if (hasList)
            {
                HandleDrop(rect, listProperty);
            }

            if (currentEvent.type == EventType.MouseDown
                && rect.Contains(currentEvent.mousePosition)
                && !foldoutRect.Contains(currentEvent.mousePosition)
                && !addRect.Contains(currentEvent.mousePosition)
                && !clearRect.Contains(currentEvent.mousePosition))
            {
                entry.isExpanded = !entry.isExpanded;
                currentEvent.Use();
            }
        }

        private void DrawObjectList(SerializedProperty property, Color channelColor, string displayName)
        {
            if (property == null || !property.isArray)
            {
                return;
            }

            RemoveInvalidEntries(property);
            Rect headerRect = EditorGUILayout.GetControlRect(false, ChannelHeaderHeight);
            DrawListHeader(headerRect, property, displayName, channelColor);
            HandleDrop(headerRect, property);

            if (!property.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            if (property.arraySize == 0)
            {
                DrawEmptyDropZone(property, channelColor);
            }

            for (int i = 0; i < property.arraySize; i++)
            {
                if (DrawObjectElement(property, i, channelColor))
                {
                    i--;
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawListHeader(Rect rect, SerializedProperty property, string displayName, Color channelColor)
        {
            Event currentEvent = Event.current;
            bool hover = rect.Contains(currentEvent.mousePosition);
            bool dragging = hover && (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform);
            EditorGUI.DrawRect(rect, GetChannelColor(channelColor, hover, dragging, true));

            Rect foldoutRect = new Rect(rect.x + 4.0f, rect.y + 7.0f, 14.0f, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none, true);

            Rect labelRect = new Rect(rect.x + 24.0f, rect.y + 6.0f, rect.width - 122.0f, 20.0f);
            GUI.Label(labelRect, displayName, entryNameStyle);

            Rect countRect = new Rect(rect.xMax - 96.0f, rect.y + 7.0f, 40.0f, 18.0f);
            GUI.Label(countRect, $"{property.arraySize} 项", EditorStyles.miniLabel);

            Rect addRect = new Rect(rect.xMax - 52.0f, rect.y + 6.0f, ChannelButtonWidth, 20.0f);
            Rect clearRect = new Rect(rect.xMax - 28.0f, rect.y + 6.0f, ChannelButtonWidth, 20.0f);
            if (GUI.Button(addRect, AddSlotLabel, EditorStyles.miniButtonLeft))
            {
                InsertEmptySlot(property);
            }

            using (new EditorGUI.DisabledScope(property.arraySize == 0))
            {
                if (GUI.Button(clearRect, ClearLabel, EditorStyles.miniButtonRight))
                {
                    property.ClearArray();
                }
            }

            if (currentEvent.type == EventType.MouseDown
                && rect.Contains(currentEvent.mousePosition)
                && !foldoutRect.Contains(currentEvent.mousePosition)
                && !addRect.Contains(currentEvent.mousePosition)
                && !clearRect.Contains(currentEvent.mousePosition))
            {
                property.isExpanded = !property.isExpanded;
                currentEvent.Use();
            }
        }

        private void DrawEmptyDropZone(SerializedProperty property, Color channelColor)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 12.0f);
            EditorGUI.DrawRect(rect, GetChannelColor(channelColor, rect.Contains(Event.current.mousePosition), false, true));
            HandleDrop(rect, property);
        }

        private bool DrawObjectElement(SerializedProperty property, int index, Color channelColor)
        {
            SerializedProperty element = property.GetArrayElementAtIndex(index);
            Object current = element.objectReferenceValue;
            Rect rect = EditorGUILayout.GetControlRect(false, ChannelElementHeight);
            EditorGUI.DrawRect(rect, GetChannelColor(channelColor, rect.Contains(Event.current.mousePosition), false, true));
            Rect fieldRect = new Rect(rect.x, rect.y + 1.0f, rect.width - 30.0f, EditorGUIUtility.singleLineHeight);
            Rect removeRect = new Rect(rect.xMax - 24.0f, rect.y + 1.0f, 24.0f, EditorGUIUtility.singleLineHeight);

            EditorGUI.BeginChangeCheck();
            Object next = EditorGUI.ObjectField(fieldRect, current, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                Object normalized = NormalizeAllowedObject(next);
                if (next != null && normalized == null)
                {
                    validationMessage = "这里只接受场景或 Prefab 模式里的 GameObject / Renderer。Mesh、材质和 prefab 资源不会写入 RSUV。";
                    element.objectReferenceValue = current;
                }
                else if (normalized != null && ContainsReference(property, normalized, index))
                {
                    validationMessage = "该对象已经在当前列表中，已跳过重复引用。";
                    element.objectReferenceValue = current;
                }
                else
                {
                    element.objectReferenceValue = normalized;
                }
            }

            if (GUI.Button(removeRect, "-", EditorStyles.miniButton))
            {
                DeleteArrayElement(property, index);
                return true;
            }

            return false;
        }

        private void HandleDrop(Rect rect, SerializedProperty property)
        {
            Event currentEvent = Event.current;
            if (!rect.Contains(currentEvent.mousePosition)
                || (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform))
            {
                return;
            }

            bool hasAllowedObject = false;
            foreach (Object draggedObject in DragAndDrop.objectReferences)
            {
                if (NormalizeAllowedObject(draggedObject) != null)
                {
                    hasAllowedObject = true;
                    break;
                }
            }

            DragAndDrop.visualMode = hasAllowedObject ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddDroppedObjects(property, DragAndDrop.objectReferences);
            }

            currentEvent.Use();
        }

        private void AddDroppedObjects(SerializedProperty property, Object[] droppedObjects)
        {
            int addedCount = 0;
            int rejectedCount = 0;
            int duplicateCount = 0;

            foreach (Object droppedObject in droppedObjects)
            {
                Object allowedObject = NormalizeAllowedObject(droppedObject);
                if (allowedObject == null)
                {
                    rejectedCount++;
                    continue;
                }

                if (ContainsReference(property, allowedObject, -1))
                {
                    duplicateCount++;
                    continue;
                }

                int index = property.arraySize;
                property.InsertArrayElementAtIndex(index);
                property.GetArrayElementAtIndex(index).objectReferenceValue = allowedObject;
                addedCount++;
            }

            if (rejectedCount > 0)
            {
                validationMessage = "已拒绝部分拖拽对象：这里只接受场景或 Prefab 模式里的 GameObject / Renderer。";
            }
            else if (addedCount == 0 && duplicateCount > 0)
            {
                validationMessage = "拖拽对象已经在当前列表中，已跳过重复引用。";
            }
        }

        private void RemoveInvalidEntries(SerializedProperty property)
        {
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                Object current = element.objectReferenceValue;
                if (current == null || NormalizeAllowedObject(current) != null)
                {
                    continue;
                }

                DeleteArrayElement(property, i);
                i--;
                validationMessage = "已清理无效引用：这里只保存场景或 Prefab 模式里的 GameObject / Renderer。";
            }
        }

        private void RefreshScene()
        {
            HoCharacterBufferGroup.RefreshLoadedScenes();
            foreach (Object targetObject in targets)
            {
                if (targetObject is HoCharacterBufferGroup group)
                {
                    group.ApplyIdentity();
                    EditorUtility.SetDirty(group);
                }
            }
        }

        private void ApplyTargets()
        {
            // 条目改名会改变槽位/ID，改完必须重新编译表并把 RSUV 写回去。
            HoCharacterBufferRegistry.MarkDirty();
            HoCharacterBufferRegistry.EnsureBuilt();
            foreach (Object targetObject in targets)
            {
                if (targetObject is HoCharacterBufferGroup group)
                {
                    EditorUtility.SetDirty(group);
                }
            }
        }

        private static void DrawProperty(SerializedProperty property, GUIContent label, bool includeChildren = false)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, label, includeChildren);
            }
        }

        private static void InsertEmptySlot(SerializedProperty property)
        {
            int index = property.arraySize;
            property.InsertArrayElementAtIndex(index);
            property.GetArrayElementAtIndex(index).objectReferenceValue = null;
        }

        private static void DeleteArrayElement(SerializedProperty property, int index)
        {
            int previousSize = property.arraySize;
            property.DeleteArrayElementAtIndex(index);
            if (property.arraySize == previousSize)
            {
                property.DeleteArrayElementAtIndex(index);
            }
        }

        private static Color GetChannelColor(Color baseColor, bool hover, bool dragging, bool childRow)
        {
            Color neutral = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.18f)
                : new Color(0.93f, 0.93f, 0.93f);
            float strength = childRow ? 0.18f : 0.34f;
            if (hover)
            {
                strength += 0.08f;
            }

            if (dragging)
            {
                strength += 0.18f;
            }

            Color color = Color.Lerp(neutral, baseColor, Mathf.Clamp01(strength));
            color.a = 1.0f;
            return color;
        }

        private static Object NormalizeAllowedObject(Object value)
        {
            if (value is Renderer renderer && IsAllowedSceneObject(renderer.gameObject))
            {
                return renderer;
            }

            if (value is GameObject gameObject && IsAllowedSceneObject(gameObject))
            {
                return gameObject;
            }

            return null;
        }

        private static bool IsAllowedSceneObject(GameObject gameObject)
        {
            return gameObject != null && !EditorUtility.IsPersistent(gameObject) && gameObject.scene.IsValid();
        }

        private static bool ContainsReference(SerializedProperty property, Object value, int ignoredIndex)
        {
            if (value == null)
            {
                return false;
            }

            int instanceId = value.GetInstanceID();
            for (int i = 0; i < property.arraySize; i++)
            {
                if (i == ignoredIndex)
                {
                    continue;
                }

                SerializedProperty element = property.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue != null && element.objectReferenceValue.GetInstanceID() == instanceId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureStyles()
        {
            if (entryNameStyle != null)
            {
                return;
            }

            entryNameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
        }
    }
}
