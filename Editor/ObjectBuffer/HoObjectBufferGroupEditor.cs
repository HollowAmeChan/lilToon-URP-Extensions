using System.Collections.Generic;
using lilToon.URP.Extensions.Editor.PostProcessing;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.ObjectBuffer
{
    /// <summary>
    /// 组件抽屉：**左列 = 身份清单，右列 = 选中的那一项的设置**（与后处理图层栈同一套排版惯例：
    /// 扁平无边框按钮、窄面板自动折成一列、颜色块就是 debug 颜色）。
    /// <list type="bullet">
    /// <item>左列每一行 = 一个部件：色块用它的 displayColor，右侧是它实际分到的 ID；</item>
    /// <item>**列表顺序 = 槽位 = ID 的低字节**，所以左列支持拖动排序，改完立刻重建表并重写 RSUV；</item>
    /// <item>右列只画当前选中项的字段：以后给部件加新的写入（物体位、SB 语义 lane…）直接往下排，不用挤宽度。</item>
    /// </list>
    /// </summary>
    [CustomEditor(typeof(HoObjectBufferGroup))]
    [CanEditMultipleObjects]
    internal sealed class HoObjectBufferGroupEditor : UnityEditor.Editor
    {
        /// <summary>左列宽度。够放下"色块 + 名字 + ID"，又不至于把右列的字段挤扁。</summary>
        private const float ListWidth = 152.0f;

        private const float RowHeight = 20.0f;
        private const float RowSpacing = 1.0f;
        private const float SwatchSize = 12.0f;
        private const float ElementHeight = 22.0f;
        private const float SectionSpacing = 6.0f;

        /// <summary>窄于这个宽度就不分列：清单折到上面，详情接在下面（跟着后处理那边的阈值习惯）。</summary>
        private const float MinSplitWidth = 300.0f;

        // 皮肤相关的东西**不能在静态初始化器里读**（Unity 明确禁止在 ScriptableObject 构造期调
        // EditorGUIUtility，读了会抛 TypeInitializationException 把整个抽屉打死），所以在
        // EnsureStyles() 里按需算一次。
        private static Color listBackground;
        private static bool themeResolved;
        private static readonly Color RowHighlight = new Color(0.30f, 0.55f, 0.95f, 0.16f);
        private static readonly Color RowHover = new Color(1.0f, 1.0f, 1.0f, 0.06f);
        private static readonly Color RowAccent = new Color(0.35f, 0.65f, 1.0f, 0.85f);

        private static readonly GUIContent AddPartLabel = new GUIContent("+", "添加一个部件（名字先给个占位，其余自己填）");
        private static readonly GUIContent RemovePartLabel = new GUIContent("-", "删除当前选中的部件");
        private static readonly GUIContent AddSelectionLabel = new GUIContent("+", "添加一个具名选区");
        private static readonly GUIContent RemoveLabel = new GUIContent("-", "删除");
        private static readonly GUIContent RefreshLabel = new GUIContent("刷新全场景 RSUV", "重新编译 palette 并把 RSUV 索引写回所有 renderer（RSUV 不会被序列化，场景/域重载后必须重写）。");
        private static GUIStyle rowNameStyle;

        private SerializedProperty priorityProperty;
        private SerializedProperty groupIdProperty;
        private SerializedProperty groupTagsProperty;
        private SerializedProperty faceBoneProperty;
        private SerializedProperty faceForwardAxisProperty;
        private SerializedProperty faceRightAxisProperty;
        private SerializedProperty faceUpAxisProperty;
        private SerializedProperty partsProperty;
        private SerializedProperty selectionsProperty;
        private string validationMessage;

        private int selectedPart;
        private int selectedSelection;
        private int draggingPart = -1;
        private bool partOrderChanged;
        private bool showGroupSettings;
        private bool showSelections;

        private void OnEnable()
        {
            priorityProperty = serializedObject.FindProperty("priority");
            groupIdProperty = serializedObject.FindProperty("groupId");
            groupTagsProperty = serializedObject.FindProperty("groupTags");
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

            // 让左列显示的 ID 是最新表（只在标脏后才真正重建）。
            HoObjectBufferRegistry.EnsureBuilt();

            bool structureChanged;
            if (EditorGUIUtility.currentViewWidth >= MinSplitWidth)
            {
                EditorGUILayout.BeginHorizontal();
                DrawPartList();
                EditorGUILayout.BeginVertical();
                DrawDetail();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                DrawPartList();
                EditorGUILayout.Space(4.0f);
                DrawDetail();
            }

            DrawFooter();

            // 拖动排序只标记一次（松手时），否则每越过一行就重建一次表、刷一屏日志。
            structureChanged = partOrderChanged;
            partOrderChanged = false;

            if (serializedObject.ApplyModifiedProperties() | structureChanged)
            {
                ApplyTargets();
                Repaint();
            }
        }

        // ------------------------------------------------------------------ 左列：身份清单

        private void DrawPartList()
        {
            int count = partsProperty != null ? partsProperty.arraySize : 0;
            selectedPart = Mathf.Clamp(selectedPart, 0, Mathf.Max(0, count - 1));

            using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
            {
                Rect header = EditorGUILayout.GetControlRect(false, RowHeight, GUILayout.Width(ListWidth));
                EditorGUI.LabelField(
                    new Rect(header.x, header.y, header.width - 34.0f, header.height),
                    $"部件（{count}）",
                    EditorStyles.miniBoldLabel);

                Rect addRect = new Rect(header.xMax - 32.0f, header.y, 16.0f, header.height);
                Rect removeRect = new Rect(header.xMax - 16.0f, header.y, 16.0f, header.height);
                if (EffectBrowserView.DrawChromeLessButton(addRect, AddPartLabel))
                {
                    AddPart();
                }

                using (new EditorGUI.DisabledScope(count == 0))
                {
                    if (EffectBrowserView.DrawChromeLessButton(removeRect, RemovePartLabel, count > 0))
                    {
                        DeleteArrayElement(partsProperty, selectedPart);
                        selectedPart = Mathf.Max(0, selectedPart - 1);
                        GUI.changed = true;
                    }
                }

                float listHeight = Mathf.Max(RowHeight, count * (RowHeight + RowSpacing));
                Rect list = EditorGUILayout.GetControlRect(
                    false,
                    listHeight,
                    GUILayout.Width(ListWidth),
                    GUILayout.Height(listHeight));
                EditorGUI.DrawRect(list, listBackground);

                for (int i = 0; i < count; i++)
                {
                    Rect row = new Rect(list.x, list.y + i * (RowHeight + RowSpacing), list.width, RowHeight);
                    DrawPartRow(row, i);
                }

                // 拖动排序：槽位就是列表顺序，所以排序是一次真正的身份改动（松手时统一重建一次表）。
                if (Event.current.type == EventType.MouseUp && draggingPart >= 0)
                {
                    draggingPart = -1;
                    partOrderChanged = true;
                }
            }
        }

        private void DrawPartRow(Rect row, int index)
        {
            SerializedProperty entry = partsProperty.GetArrayElementAtIndex(index);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            SerializedProperty colorProperty = entry.FindPropertyRelative("displayColor");
            string partName = nameProperty != null ? nameProperty.stringValue : string.Empty;
            string id = BuildRowIdText(partName);
            bool selected = index == selectedPart;
            bool hover = row.Contains(Event.current.mousePosition);

            if (selected)
            {
                EditorGUI.DrawRect(row, RowHighlight);
                EditorGUI.DrawRect(new Rect(row.x, row.y, 2.0f, row.height), RowAccent);
            }
            else if (hover)
            {
                EditorGUI.DrawRect(row, RowHover);
            }

            Rect swatch = new Rect(row.x + 6.0f, row.y + (row.height - SwatchSize) * 0.5f, SwatchSize, SwatchSize);
            EditorGUI.DrawRect(swatch, colorProperty != null ? colorProperty.colorValue : Color.gray);

            Rect idRect = new Rect(row.xMax - 48.0f, row.y, 44.0f, row.height);
            Rect nameRect = new Rect(
                swatch.xMax + 6.0f,
                row.y,
                Mathf.Max(0.0f, idRect.x - swatch.xMax - 8.0f),
                row.height);

            string title = string.IsNullOrEmpty(partName) ? "（空名字）" : partName;
            GUI.Label(
                nameRect,
                new GUIContent(title, $"{title}\n槽位 {index} · {id}\n列表顺序 = 槽位 = ID 的低字节；拖动可排序"),
                rowNameStyle);
            EditorGUI.LabelField(idRect, id, EditorStyles.centeredGreyMiniLabel);

            EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
            HandlePartRowInput(row, index);
        }

        private void HandlePartRowInput(Rect row, int index)
        {
            Event currentEvent = Event.current;
            if (!row.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                selectedPart = index;
                draggingPart = index;
                GUI.FocusControl(null);
                currentEvent.Use();
                Repaint();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && draggingPart >= 0 && draggingPart != index)
            {
                // 只在 Editor 状态里换位（ApplyModifiedProperties 会写回去），表等松手后再重建：
                // 拖动过程中每越过一行就重建一次表，是白刷日志。
                partsProperty.MoveArrayElement(draggingPart, index);
                selectedPart = index;
                draggingPart = index;
                currentEvent.Use();
            }
        }

        private void AddPart()
        {
            int index = partsProperty.arraySize;
            partsProperty.InsertArrayElementAtIndex(index);
            SerializedProperty entry = partsProperty.GetArrayElementAtIndex(index);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            if (nameProperty != null)
            {
                nameProperty.stringValue = $"部件 {index}";
            }

            SerializedProperty renderers = entry.FindPropertyRelative("renderers");
            if (renderers != null)
            {
                renderers.ClearArray();
            }

            SerializedProperty colorProperty = entry.FindPropertyRelative("displayColor");
            if (colorProperty != null)
            {
                colorProperty.colorValue = new Color(0.75f, 0.75f, 0.75f, 1.0f);
            }

            selectedPart = index;
            GUI.changed = true;
        }

        private string BuildRowIdText(string partName)
        {
            var group = target as HoObjectBufferGroup;
            int groupId = Mathf.Clamp(
                groupIdProperty != null ? groupIdProperty.intValue : (group != null ? group.groupId : 0),
                0,
                HoObjectBufferPaletteLimits.MaxGroups - 1);
            if (groupId == 0 || string.IsNullOrEmpty(partName))
            {
                return "未注册";
            }

            uint partId = HoObjectBufferRegistry.GetPartId(groupId, partName);
            return partId == 0u ? "未注册" : $"0x{partId:X4}";
        }

        // ------------------------------------------------------------------ 右列：设置

        /// <summary>右列：选中部件的字段 + 两个折叠区（组设置 / 具名选区）。</summary>
        private void DrawDetail()
        {
            int count = partsProperty != null ? partsProperty.arraySize : 0;
            selectedPart = Mathf.Clamp(selectedPart, 0, Mathf.Max(0, count - 1));

            if (count > 0)
            {
                DrawSelectedPart();
            }

            DrawGroupSettings();
            DrawSelections();
        }

        private void DrawSelectedPart()
        {
            SerializedProperty entry = partsProperty.GetArrayElementAtIndex(selectedPart);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            string partName = nameProperty != null ? nameProperty.stringValue : string.Empty;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SerializedProperty colorProperty = entry.FindPropertyRelative("displayColor");
                Rect header = EditorGUILayout.GetControlRect(false, RowHeight);
                Rect swatch = new Rect(header.x, header.y + (header.height - SwatchSize) * 0.5f, SwatchSize, SwatchSize);
                EditorGUI.DrawRect(swatch, colorProperty != null ? colorProperty.colorValue : Color.gray);
                GUI.Label(
                    new Rect(swatch.xMax + 6.0f, header.y, header.width - SwatchSize - 8.0f, header.height),
                    new GUIContent(
                        string.IsNullOrEmpty(partName) ? "（空名字）" : partName,
                        "这一项的身份：名字决定槽位，槽位决定像素里 ID 的低字节。"),
                    rowNameStyle);

                DrawProperty(nameProperty, new GUIContent("名字", "组内唯一。它决定槽位号 = 像素里 ID 的低字节。"));
                DrawProperty(entry.FindPropertyRelative("category"), new GUIContent("类别", "单值，回答「这是什么」。多归属语义请用标签位。"));
                DrawProperty(entry.FindPropertyRelative("tags"), new GUIContent("标签", "位掩码：一个部件同时属于多个语义时用它（例如 CharacterFull = 该组任意部件）。"));
                DrawProperty(colorProperty, new GUIContent("显示色", "debug 视图与面板色块用的颜色；像素里不存颜色，只存 ID。"));
                DrawProperty(entry.FindPropertyRelative("includeChildren"), new GUIContent("展开子级", "拖入 GameObject 或预制件实例时，包含它下面的子级 Renderer。"));

                EditorGUILayout.Space(4.0f);
                DrawRendererList(entry.FindPropertyRelative("renderers"), colorProperty != null ? colorProperty.colorValue : Color.gray);
            }

            EditorGUILayout.Space(SectionSpacing);
        }

        private void DrawGroupSettings()
        {
            showGroupSettings = EditorGUILayout.Foldout(showGroupSettings, "组设置", true);
            if (!showGroupSettings)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(groupIdProperty, new GUIContent("组 ID", "1-255，它是 ID 的高字节。组 0 保留：0 表示「未注册」，也是 RSUV 被重置后的值。"));
                DrawProperty(groupTagsProperty, new GUIContent("组级标签", "放在组表那一行，用于「整组」语义（例如 CharacterFull），不必在每个部件行重复。"));
                DrawProperty(priorityProperty, new GUIContent("优先级", "同一个 Renderer 被多个组命中时优先级高者生效；相同时离 Renderer 最近的组生效。"));
                EditorGUILayout.Space(2.0f);
                DrawProperty(faceBoneProperty, new GUIContent("面部朝向", "角色朝向的参考 Transform（骨骼或朝向正确的空物体）。逐像素朝向会与层 0 的获胜身份同步 resolve；留空表示不产出朝向图。"));
                DrawProperty(faceForwardAxisProperty, new GUIContent("脸前轴", "骨骼的哪个局部轴作为「脸前方」。默认 +Z。"));
                DrawProperty(faceRightAxisProperty, new GUIContent("右轴", "骨骼的哪个局部轴作为「角色右侧」。默认 +X。"));
                DrawProperty(faceUpAxisProperty, new GUIContent("上轴", "骨骼的哪个局部轴作为「角色上方」。默认 +Y。"));

                int groupId = Mathf.Clamp(groupIdProperty != null ? groupIdProperty.intValue : 0, 0, HoObjectBufferPaletteLimits.MaxGroups - 1);
                if (groupId == 0)
                {
                    validationMessage = "组 ID 0 被保留，这个组不会写进 palette。请改成 1-255。";
                }
            }
        }

        private void DrawSelections()
        {
            int count = selectionsProperty != null ? selectionsProperty.arraySize : 0;
            selectedSelection = Mathf.Clamp(selectedSelection, 0, Mathf.Max(0, count - 1));

            Rect header = EditorGUILayout.GetControlRect(false, RowHeight);
            showSelections = EditorGUI.Foldout(
                new Rect(header.x, header.y, header.width - 34.0f, header.height),
                showSelections,
                $"具名选区（{count}）",
                true);

            Rect addRect = new Rect(header.xMax - 32.0f, header.y, 16.0f, header.height);
            Rect removeRect = new Rect(header.xMax - 16.0f, header.y, 16.0f, header.height);
            if (EffectBrowserView.DrawChromeLessButton(addRect, AddSelectionLabel))
            {
                int index = selectionsProperty.arraySize;
                selectionsProperty.InsertArrayElementAtIndex(index);
                SerializedProperty nameProperty = selectionsProperty.GetArrayElementAtIndex(index).FindPropertyRelative("name");
                if (nameProperty != null)
                {
                    nameProperty.stringValue = $"选择 {index}";
                }

                selectedSelection = index;
                GUI.changed = true;
            }

            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (EffectBrowserView.DrawChromeLessButton(removeRect, RemoveLabel, count > 0))
                {
                    DeleteArrayElement(selectionsProperty, selectedSelection);
                    selectedSelection = Mathf.Max(0, selectedSelection - 1);
                    GUI.changed = true;
                }
            }

            if (!showSelections || count == 0)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < count; i++)
                {
                    DrawSelectionRow(i);
                }

                EditorGUILayout.Space(2.0f);
                SerializedProperty entry = selectionsProperty.GetArrayElementAtIndex(selectedSelection);
                DrawProperty(entry.FindPropertyRelative("name"), new GUIContent("名字", "全局唯一。材质里引用的是这个名字。"));
                DrawProperty(entry.FindPropertyRelative("tags"), new GUIContent("标签"));
                DrawProperty(entry.FindPropertyRelative("displayColor"), new GUIContent("显示色", "debug 与 AOV manifest 用的颜色。"));
            }

            EditorGUILayout.Space(SectionSpacing);
        }

        private void DrawSelectionRow(int index)
        {
            SerializedProperty entry = selectionsProperty.GetArrayElementAtIndex(index);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            SerializedProperty colorProperty = entry.FindPropertyRelative("displayColor");
            string selectionName = nameProperty != null ? nameProperty.stringValue : string.Empty;
            uint selectionId = HoObjectBufferRegistry.GetSelectionId(selectionName);

            Rect row = EditorGUILayout.GetControlRect(false, RowHeight);
            bool selected = index == selectedSelection;
            if (selected)
            {
                EditorGUI.DrawRect(row, RowHighlight);
                EditorGUI.DrawRect(new Rect(row.x, row.y, 2.0f, row.height), RowAccent);
            }

            Rect swatch = new Rect(row.x + 6.0f, row.y + (row.height - SwatchSize) * 0.5f, SwatchSize, SwatchSize);
            EditorGUI.DrawRect(swatch, colorProperty != null ? colorProperty.colorValue : Color.gray);

            Rect idRect = new Rect(row.xMax - 34.0f, row.y, 30.0f, row.height);
            Rect nameRect = new Rect(swatch.xMax + 6.0f, row.y, Mathf.Max(0.0f, idRect.x - swatch.xMax - 8.0f), row.height);
            GUI.Label(
                nameRect,
                new GUIContent(
                    string.IsNullOrEmpty(selectionName) ? "（空名字）" : selectionName,
                    "选择是跨部件的具名集合：ID 是独立的 8 bit 空间，不与身份 ID 混用。"),
                rowNameStyle);
            EditorGUI.LabelField(idRect, selectionId > 0 ? selectionId.ToString() : "—", EditorStyles.centeredGreyMiniLabel);

            EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && row.Contains(Event.current.mousePosition))
            {
                selectedSelection = index;
                Event.current.Use();
                Repaint();
            }
        }

        // ------------------------------------------------------------------ 底部：状态与刷新

        private void DrawFooter()
        {
            EditorGUILayout.Space(SectionSpacing);
            using (new EditorGUILayout.HorizontalScope())
            {
                int partRows = Mathf.Max(0, HoObjectBufferRegistry.PartRowCount - 1);
                int groupId = Mathf.Clamp(groupIdProperty != null ? groupIdProperty.intValue : 0, 0, HoObjectBufferPaletteLimits.MaxGroups - 1);
                EditorGUILayout.LabelField(
                    $"组 {groupId} · 已注册 部件 {partRows} / {HoObjectBufferPaletteLimits.MaxPartRows}，选择 {HoObjectBufferRegistry.SelectionCount} / {HoObjectBufferPaletteLimits.MaxSelections - 1}",
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

            DrawConflicts();
        }

        /// <summary>
        /// 重复指定（一个 Renderer 被多个条目命中）。拖父级展开子级时最容易撞上：
        /// 裁决是确定性的，但必须逐条列出来，否则"我明明拖进去了却没生效"没人查得动。
        /// </summary>
        private void DrawConflicts()
        {
            var group = target as HoObjectBufferGroup;
            if (group == null)
            {
                return;
            }

            IReadOnlyList<HoObjectBufferConflict> conflicts = HoObjectBufferGroup.GetConflicts();
            int total = 0;
            var lines = new List<string>();
            for (int i = 0; i < conflicts.Count; i++)
            {
                HoObjectBufferConflict conflict = conflicts[i];
                if (!conflict.Involves(group))
                {
                    continue;
                }

                total++;
                if (lines.Count < 6)
                {
                    lines.Add("· " + conflict.Describe());
                }
            }

            if (total == 0)
            {
                return;
            }

            string message = $"重复指定 {total} 条（同一个 Renderer 被多个部件条目命中）：\n" + string.Join("\n", lines);
            if (total > lines.Count)
            {
                message += $"\n…还有 {total - lines.Count} 条";
            }

            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        // ------------------------------------------------------------------ Renderer 列表（沿用原来的拖放行为）

        private void DrawRendererList(SerializedProperty property, Color color)
        {
            if (property == null || !property.isArray)
            {
                return;
            }

            RemoveInvalidEntries(property);
            EditorGUILayout.LabelField($"Renderer（{property.arraySize}）", EditorStyles.miniBoldLabel);

            if (property.arraySize == 0)
            {
                Rect zone = EditorGUILayout.GetControlRect(false, 14.0f);
                EditorGUI.DrawRect(zone, GetChildColor(color, zone.Contains(Event.current.mousePosition)));
                GUI.Label(new Rect(zone.x + 6.0f, zone.y, zone.width - 12.0f, zone.height), "把 GameObject / Renderer 拖到这里", EditorStyles.miniLabel);
                HandleDrop(zone, property);
                return;
            }

            for (int i = 0; i < property.arraySize; i++)
            {
                if (DrawObjectElement(property, i, color))
                {
                    i--;
                }
            }

            Rect dropZone = EditorGUILayout.GetControlRect(false, 10.0f);
            EditorGUI.DrawRect(dropZone, GetChildColor(color, dropZone.Contains(Event.current.mousePosition)));
            HandleDrop(dropZone, property);
        }

        private bool DrawObjectElement(SerializedProperty property, int index, Color color)
        {
            SerializedProperty element = property.GetArrayElementAtIndex(index);
            Object current = element.objectReferenceValue;
            Rect rect = EditorGUILayout.GetControlRect(false, ElementHeight);
            EditorGUI.DrawRect(rect, GetChildColor(color, rect.Contains(Event.current.mousePosition)));
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

        private static Object NormalizeAllowedObject(Object candidate)
        {
            if (candidate is GameObject || candidate is Renderer)
            {
                return candidate;
            }

            return null;
        }

        private static bool ContainsReference(SerializedProperty property, Object candidate, int skipIndex)
        {
            for (int i = 0; i < property.arraySize; i++)
            {
                if (i == skipIndex)
                {
                    continue;
                }

                if (property.GetArrayElementAtIndex(i).objectReferenceValue == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ 表重建

        private void RefreshScene()
        {
            HoObjectBufferGroup.RefreshLoadedScenes();
            foreach (Object targetObject in targets)
            {
                if (targetObject is HoObjectBufferGroup group)
                {
                    group.ApplyIdentity();
                    EditorUtility.SetDirty(group);
                }
            }
        }

        private void ApplyTargets()
        {
            // 条目改名/排序会改变槽位与 ID，改完必须重新编译表并把 RSUV 写回去。
            HoObjectBufferGroup.RefreshLoadedScenes();
            foreach (Object targetObject in targets)
            {
                if (targetObject is HoObjectBufferGroup group)
                {
                    EditorUtility.SetDirty(group);
                }
            }
        }

        // ------------------------------------------------------------------ 小工具

        private static void DrawProperty(SerializedProperty property, GUIContent label, bool includeChildren = false)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, label, includeChildren);
            }
        }

        private static void DeleteArrayElement(SerializedProperty property, int index)
        {
            if (property == null || index < 0 || index >= property.arraySize)
            {
                return;
            }

            int previousSize = property.arraySize;
            property.DeleteArrayElementAtIndex(index);
            if (property.arraySize == previousSize)
            {
                property.DeleteArrayElementAtIndex(index);
            }
        }

        private static Color GetChildColor(Color baseColor, bool hover)
        {
            Color neutral = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.18f, 1.0f)
                : new Color(0.93f, 0.93f, 0.93f, 1.0f);
            Color color = Color.Lerp(neutral, baseColor, hover ? 0.42f : 0.30f);
            color.a = 1.0f;
            return color;
        }

        private static void EnsureStyles()
        {
            if (!themeResolved)
            {
                themeResolved = true;
                listBackground = EditorGUIUtility.isProSkin
                    ? new Color(0.0f, 0.0f, 0.0f, 0.22f)
                    : new Color(0.0f, 0.0f, 0.0f, 0.06f);
            }

            if (rowNameStyle != null)
            {
                return;
            }

            rowNameStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
        }
    }
}
