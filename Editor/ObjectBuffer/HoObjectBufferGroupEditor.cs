using System.Collections.Generic;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.ObjectBuffer
{
    /// <summary>
    /// 组件抽屉：沿用 MetadataBuffer group 的旧款式（一条彩色条 = 一个条目，条上是名字 + 计数 + `+` / `×`，
    /// 展开后是它的内容和对象行），但信息按新模型收窄了——部件只回答身份，材质数值不在组件里。
    /// 条上的副标题显示**注册表实际分配的 ID**：改名就会换 ID，所以这一栏必须当场看得见。
    /// </summary>
    [CustomEditor(typeof(HoObjectBufferGroup))]
    [CanEditMultipleObjects]
    internal sealed class HoObjectBufferGroupEditor : UnityEditor.Editor
    {
        private const float SectionSpacing = 6.0f;
        private const float EntryHeaderHeight = 38.0f;
        private const float ElementHeight = 22.0f;
        private const float ButtonWidth = 22.0f;
        private const float RightReserve = 100.0f;

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

        /// <summary>
        /// 拆分出来的条目**不自作主张填语义值**：类别 / 标签 / 显示色一律停在"待分配"状态，
        /// 由人自己定。这里只是条目类型的默认显示色（没分配之前面板上看到的就是它）。
        /// </summary>
        private static readonly Color UnassignedDisplayColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        private static readonly GUIContent AddSlotLabel = new GUIContent("+", "添加一个空槽（也可以直接把 GameObject / Renderer 拖到这条上）");
        private static readonly GUIContent ClearLabel = new GUIContent("×", "清空本条的 Renderer 列表");
        private static readonly GUIContent RemoveSelectionLabel = new GUIContent("×", "删除这个选择");
        private static readonly GUIContent RefreshLabel = new GUIContent("刷新全场景 RSUV", "重新编译 palette 并把 RSUV 索引写回所有 renderer（RSUV 不会被序列化，场景/域重载后必须重写）。");
        private static readonly GUIContent SplitPartsLabel = new GUIContent(
            "按 Renderer 拆分部件",
            "把覆盖了多个 Renderer（或开着「展开子级」）的条目拆成「一个 Renderer 一个部件」：\n" +
            "· 名字取 Renderer 所在物体名，重名自动加序号；\n" +
            "· 类别 / 标签 / 显示色**不猜也不继承**，全部停在待分配状态，由你自己填；\n" +
            "· 只覆盖一个 Renderer 且没开子级的条目原样保留，手工调过的值不会被冲掉；\n" +
            "· 拆完立即重建表并重写 RSUV，不需要再手点刷新。");
        private static GUIStyle entryNameStyle;

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
        private string statusMessage;

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
            statusMessage = null;

            // 让"已分配的 ID"显示的是最新表（只在标脏后才真正重建）。
            HoObjectBufferRegistry.EnsureBuilt();

            EditorGUILayout.HelpBox(
                "身份从这里出：**部件**回答“这个 Renderer 是谁”（名字在角色内唯一，它决定像素里的 16 bit ID）；" +
                "**选择**回答“我想把哪一块单独拿出来调”（具名选区，材质引用名字）。\n" +
                "材质数值（厚度 / 粗糙度 / 金属度 …）不在这个组件里——它们在材质上已经填过一遍。",
                MessageType.Info);

            DrawIdentitySection();
            DrawPartsSection();
            DrawSelectionsSection();
            DrawFooter();

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                ApplyTargets();
                // 冲突列表刚被重建，立即重画一次，别让人以为"拖进去没反应"。
                Repaint();
            }
        }

        private void DrawIdentitySection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(priorityProperty, new GUIContent("优先级", "同一个 Renderer 被多个 Group 命中时，优先级高者生效；相同时离 Renderer 最近的组生效。"));
                DrawProperty(groupIdProperty, new GUIContent("组 ID", "1-255。组 0 保留：ID 0 表示「未注册」，它也是 RSUV 被重置后的值。"));
                DrawProperty(groupTagsProperty, new GUIContent("组级标签", "放在组表那一行，用于「整组」语义（例如 CharacterFull），不必在每个部件行重复。"));
                EditorGUILayout.Space(2.0f);
                DrawProperty(faceBoneProperty, new GUIContent("面部朝向", "确定角色面部朝向的 Transform——可以是骨骼，也可以是一个朝向正确的空物体。供眼透相机角度修正、将来的 SDF 等消费者读取；留空表示未提供。"));
                DrawProperty(faceForwardAxisProperty, new GUIContent("脸前轴", "骨骼的哪个局部轴作为“脸前方”。默认 +Z。若正面/侧面的衰减方向反了，换成 +Z / -Z 试试。"));
                DrawProperty(faceRightAxisProperty, new GUIContent("右轴", "骨骼的哪个局部轴作为“角色右侧（画面左侧）”。默认 +X。"));
                DrawProperty(faceUpAxisProperty, new GUIContent("上轴", "骨骼的哪个局部轴作为“角色上方”。默认 +Y。俯仰角按此轴分解，若俯视/仰视不生效请检查此项。"));

                int groupId = Mathf.Clamp(groupIdProperty != null ? groupIdProperty.intValue : 0, 0, HoObjectBufferPaletteLimits.MaxGroups - 1);
                if (groupId == 0)
                {
                    validationMessage = "组 ID 0 被保留，这个 group 不会写进 palette。请改成 1-255。";
                }
            }
        }

        private void DrawPartsSection()
        {
            EditorGUILayout.Space(SectionSpacing);
            if (partsProperty == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < partsProperty.arraySize; i++)
                {
                    DrawPartEntry(i);
                }

                if (partsProperty.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("还没有部件。一个部件 = 一个具名身份 + 它包含的 Renderer。", MessageType.None);
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

                EditorGUILayout.Space(2.0f);
                DrawGranularityRow();
            }
        }

        /// <summary>
        /// 身份粒度：像素里的 ID 是"组 8 + 槽位 8"，**槽位 = 部件在列表里的序号**，
        /// 所以"一个条目覆盖多少个 Renderer"直接决定层 0/1..3 能分辨到什么程度。
        /// 只覆盖一个 Renderer 的条目就是最细粒度，不动它（手工调过的名字和显示色要保住）。
        /// </summary>
        private void DrawGranularityRow()
        {
            var group = target as HoObjectBufferGroup;
            int covered = 0;
            int coarsest = 0;
            int splitCandidates = 0;
            int emptyEntries = 0;
            if (group != null)
            {
                var buffer = new List<Renderer>();
                for (int i = 0; i < group.parts.Count; i++)
                {
                    HoObjectBufferPartEntry entry = group.parts[i];
                    if (entry == null || string.IsNullOrEmpty(entry.name))
                    {
                        continue;
                    }

                    buffer.Clear();
                    HoObjectBufferGroup.CollectEntryRenderers(entry, buffer);
                    covered += buffer.Count;
                    coarsest = Mathf.Max(coarsest, buffer.Count);
                    if (buffer.Count == 0)
                    {
                        emptyEntries++;
                    }
                    else if (buffer.Count > 1 || entry.includeChildren)
                    {
                        splitCandidates++;
                    }
                }
            }

            if (partsProperty.arraySize > HoObjectBufferPaletteLimits.MaxSlotsPerGroup)
            {
                AppendValidation(
                    $"部件数 {partsProperty.arraySize} 超过槽位上限 {HoObjectBufferPaletteLimits.MaxSlotsPerGroup}：" +
                    "槽位是 ID 的低 8 bit，超出的部件不会被写进表。");
            }

            if (emptyEntries > 0)
            {
                AppendValidation($"{emptyEntries} 个条目没有覆盖任何 Renderer：它们不写 RSUV，但照样占一个槽位。");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(splitCandidates == 0))
                {
                    if (GUILayout.Button(SplitPartsLabel, GUILayout.Width(190.0f)))
                    {
                        SplitPartsByRenderer();
                    }
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    splitCandidates == 0
                        ? $"覆盖 {covered} 个 Renderer · 已是最细粒度"
                        : $"覆盖 {covered} 个 Renderer · 最粗的条目占 {coarsest} 个",
                    EditorStyles.miniLabel);
            }
        }

        private void AppendValidation(string message)
        {
            validationMessage = string.IsNullOrEmpty(validationMessage) ? message : validationMessage + "\n" + message;
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
            string subtitle = BuildPartIdText(index, partName, categoryProperty);
            int conflictCount = CountPartConflicts(partName);
            if (conflictCount > 0)
            {
                subtitle += $" · ⚠ 重复 {conflictCount}";
            }

            DrawEntryHeader(entry, partsProperty, index, title, subtitle, color, renderersProperty, true);

            if (!entry.isExpanded)
            {
                EditorGUILayout.Space(SectionSpacing);
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.Space(2.0f);
            DrawProperty(nameProperty, new GUIContent("名字", "组内唯一。它决定槽位号 = 像素里 ID 的低字节。"));
            DrawProperty(categoryProperty, new GUIContent("类别", "单值，回答「这是什么」。多归属语义请用标签位。"));
            DrawProperty(entry.FindPropertyRelative("tags"), new GUIContent("标签", "位掩码：一个部件同时属于多个语义时用它（例如 CharacterFull = 该组任意部件）。"));
            DrawProperty(entry.FindPropertyRelative("displayColor"), new GUIContent("显示色", "debug 与 Nuke color picker 用的颜色；像素里不存颜色，只存 ID。"));
            DrawProperty(entry.FindPropertyRelative("includeChildren"), new GUIContent("展开子级", "拖入 GameObject 或预制件实例时，包含它下面的子级 Renderer。"));
            EditorGUILayout.Space(4.0f);
            DrawRendererList(renderersProperty, color);

            EditorGUILayout.Space(4.0f);
            if (GUILayout.Button("删除这个部件"))
            {
                DeleteArrayElement(partsProperty, index);
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(SectionSpacing);
        }

        private void DrawSelectionsSection()
        {
            EditorGUILayout.Space(SectionSpacing);
            if (selectionsProperty == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Cryptomatte（具名选区）", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "部件是“这是谁”；Cryptomatte 选择是“想单独调哪一块”——它可以横跨多个部件，也可以是某个部件里的一段遮罩。" +
                    "名字全局唯一，材质侧只引用名字（所以在这里改名不破资产）。注册了选择才会产出那张选择图；" +
                    "导出时可选规范合规的 crypto_* 层，Nuke 里能直接点选。",
                    MessageType.None);

                for (int i = 0; i < selectionsProperty.arraySize; i++)
                {
                    DrawSelectionEntry(i);
                }

                EditorGUILayout.Space(2.0f);
                if (GUILayout.Button("+ 添加 Cryptomatte 选择"))
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
            uint selectionId = HoObjectBufferRegistry.GetSelectionId(selectionName);
            string title = string.IsNullOrEmpty(selectionName) ? $"（空名字 · {index}）" : selectionName;
            string subtitle = selectionId > 0 ? $"Cryptomatte ID {selectionId}" : "未注册";
            DrawEntryHeader(entry, selectionsProperty, index, title, subtitle, SelectionColor, null, false);

            if (!entry.isExpanded)
            {
                EditorGUILayout.Space(SectionSpacing);
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.Space(2.0f);
            DrawProperty(nameProperty, new GUIContent("名字", "全局唯一。材质里引用的是这个名字。"));
            DrawProperty(entry.FindPropertyRelative("tags"), new GUIContent("标签"));
            DrawProperty(entry.FindPropertyRelative("displayColor"), new GUIContent("显示色", "debug 与 AOV manifest 用的颜色。"));
            EditorGUILayout.Space(4.0f);
            if (GUILayout.Button(RemoveSelectionLabel.text + " 删除这个选择"))
            {
                DeleteArrayElement(selectionsProperty, index);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(SectionSpacing);
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(SectionSpacing);
            using (new EditorGUILayout.HorizontalScope())
            {
                int partRows = Mathf.Max(0, HoObjectBufferRegistry.PartRowCount - 1);
                EditorGUILayout.LabelField(
                    $"已注册：部件 {partRows} / {HoObjectBufferPaletteLimits.MaxPartRows}，选择 {HoObjectBufferRegistry.SelectionCount} / {HoObjectBufferPaletteLimits.MaxSelections - 1}",
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

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
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

        private int CountPartConflicts(string partName)
        {
            var group = target as HoObjectBufferGroup;
            if (group == null || string.IsNullOrEmpty(partName))
            {
                return 0;
            }

            IReadOnlyList<HoObjectBufferConflict> conflicts = HoObjectBufferGroup.GetConflicts();
            int count = 0;
            for (int i = 0; i < conflicts.Count; i++)
            {
                if (conflicts[i].InvolvesPart(group, partName))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>条目条：一条 = 一个部件/选择。标题与副标题分行绘制，互不重叠。</summary>
        private void DrawEntryHeader(
            SerializedProperty entry,
            SerializedProperty parentProperty,
            int index,
            string title,
            string subtitle,
            Color color,
            SerializedProperty listProperty,
            bool editableList)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EntryHeaderHeight);
            Event currentEvent = Event.current;
            bool hasList = listProperty != null && listProperty.isArray;
            bool hover = rect.Contains(currentEvent.mousePosition);
            bool dragging = hover && hasList && (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform);
            EditorGUI.DrawRect(rect, GetEntryColor(color, hover, dragging));

            float rightEdge = rect.xMax - 4.0f;
            Rect clearRect = new Rect(rightEdge - ButtonWidth, rect.y + (EntryHeaderHeight - 20.0f) * 0.5f, ButtonWidth, 20.0f);
            Rect addRect = new Rect(clearRect.x - ButtonWidth, clearRect.y, ButtonWidth, 20.0f);
            Rect countRect = new Rect(addRect.x - 46.0f, clearRect.y + 2.0f, 44.0f, 16.0f);
            float labelWidth = Mathf.Max(40.0f, countRect.x - (rect.x + 26.0f) - 4.0f);

            Rect foldoutRect = new Rect(rect.x + 4.0f, rect.y + (EntryHeaderHeight - EditorGUIUtility.singleLineHeight) * 0.5f, 14.0f, EditorGUIUtility.singleLineHeight);
            entry.isExpanded = EditorGUI.Foldout(foldoutRect, entry.isExpanded, GUIContent.none, true);

            var titleRect = new Rect(rect.x + 24.0f, rect.y + 4.0f, labelWidth, 17.0f);
            GUI.Label(titleRect, title, entryNameStyle);
            var subtitleRect = new Rect(rect.x + 24.0f, rect.y + 20.0f, labelWidth, 14.0f);
            GUI.Label(subtitleRect, subtitle, EditorStyles.miniLabel);

            if (hasList)
            {
                GUI.Label(countRect, $"{listProperty.arraySize} 项", EditorStyles.miniLabel);
            }

            if (editableList && hasList)
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

                HandleDrop(rect, listProperty);
            }
            else if (GUI.Button(clearRect, RemoveSelectionLabel, EditorStyles.miniButtonRight))
            {
                DeleteArrayElement(parentProperty, index);
                return;
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

        private string BuildPartIdText(int slot, string partName, SerializedProperty categoryProperty)
        {
            var group = target as HoObjectBufferGroup;
            int groupId = Mathf.Clamp(
                groupIdProperty != null ? groupIdProperty.intValue : (group != null ? group.groupId : 0),
                0,
                HoObjectBufferPaletteLimits.MaxGroups - 1);
            if (group == null || groupId == 0 || string.IsNullOrEmpty(partName))
            {
                return $"槽位 {slot} · 未注册";
            }

            uint partId = HoObjectBufferRegistry.GetPartId(groupId, partName);
            if (partId == 0u)
            {
                return $"槽位 {slot} · 未注册";
            }

            string category = string.Empty;
            if (categoryProperty != null && categoryProperty.enumDisplayNames != null
                && categoryProperty.enumValueIndex >= 0 && categoryProperty.enumValueIndex < categoryProperty.enumDisplayNames.Length)
            {
                category = categoryProperty.enumDisplayNames[categoryProperty.enumValueIndex];
            }

            return string.IsNullOrEmpty(category)
                ? $"0x{partId:X4} · 槽位 {slot}"
                : $"0x{partId:X4} · 槽位 {slot} · {category}";
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

        /// <summary>
        /// 拆分计划的一项。先算完再一次性写回 SerializedProperty：边改边读必然算错。
        /// <para>
        /// 两个来源要分清楚：**新拆出来的**子条目由拆分决定（一个 Renderer + 未分配值）；
        /// **原样保留的**条目必须逐字段照抄原件（含物体引用是 GameObject 还是 Renderer），
        /// 否则"只拆别的条目"会顺手改掉人已经拖好的那份数据。
        /// </para>
        /// </summary>
        private readonly struct PartPlan
        {
            private PartPlan(
                string name,
                HoObjectBufferPartCategory category,
                HoObjectBufferPartTags tags,
                Color displayColor,
                Object[] sourceObjects,
                Renderer[] renderers,
                bool includeChildren)
            {
                this.name = name;
                this.category = category;
                this.tags = tags;
                this.displayColor = displayColor;
                this.sourceObjects = sourceObjects;
                this.renderers = renderers;
                this.includeChildren = includeChildren;
            }

            public readonly string name;
            public readonly HoObjectBufferPartCategory category;
            public readonly HoObjectBufferPartTags tags;
            public readonly Color displayColor;
            /// <summary>非 null 表示"照抄原件"（原样保留的条目），此时忽略 <see cref="renderers"/>。</summary>
            public readonly Object[] sourceObjects;
            public readonly Renderer[] renderers;
            public readonly bool includeChildren;

            /// <summary>原样保留：字段照抄，物体引用一个字节都不动。</summary>
            public static PartPlan Keep(HoObjectBufferPartEntry entry)
            {
                return new PartPlan(
                    entry.name,
                    entry.category,
                    entry.tags,
                    entry.displayColor,
                    entry.renderers,
                    System.Array.Empty<Renderer>(),
                    entry.includeChildren);
            }

            /// <summary>新拆出来的子条目：结构化结果由拆分决定，语义值留空待分配。</summary>
            public static PartPlan SplitChild(string name, Renderer renderer, Color unassignedColor)
            {
                return new PartPlan(
                    name,
                    HoObjectBufferPartCategory.Unspecified,
                    HoObjectBufferPartTags.None,
                    unassignedColor,
                    null,
                    new[] { renderer },
                    false);
            }
        }

        /// <summary>
        /// 把每个"覆盖多个 Renderer"的条目就地拆成"一个 Renderer 一个部件"。
        /// 只覆盖一个 Renderer 且没开「展开子级」的条目原样保留——手工调过的名字与显示色不能被这次重构冲掉。
        /// </summary>
        private void SplitPartsByRenderer()
        {
            var group = target as HoObjectBufferGroup;
            if (group == null || partsProperty == null)
            {
                return;
            }

            if (targets.Length > 1)
            {
                validationMessage = "拆分只支持单选：多选时各组的 Renderer 组成不同，请逐个组执行。";
                return;
            }

            var plans = new List<PartPlan>();
            var usedNames = new HashSet<string>(System.StringComparer.Ordinal);
            var buffer = new List<Renderer>();
            int splitEntries = 0;
            int createdParts = 0;

            // 第一遍：登记不动的条目的名字，避免拆出来的子条目跟它们撞名。
            for (int i = 0; i < group.parts.Count; i++)
            {
                HoObjectBufferPartEntry entry = group.parts[i];
                if (entry == null || string.IsNullOrEmpty(entry.name))
                {
                    continue;
                }

                buffer.Clear();
                HoObjectBufferGroup.CollectEntryRenderers(entry, buffer);
                if (IsFineGrained(entry, buffer))
                {
                    usedNames.Add(entry.name);
                }
            }

            // 第二遍：按原顺序重建。拆出来的子条目占据父条目原来的位置，槽位序号跟着列表顺序走。
            for (int i = 0; i < group.parts.Count; i++)
            {
                HoObjectBufferPartEntry entry = group.parts[i];
                if (entry == null || string.IsNullOrEmpty(entry.name))
                {
                    continue;
                }

                buffer.Clear();
                HoObjectBufferGroup.CollectEntryRenderers(entry, buffer);
                if (IsFineGrained(entry, buffer) || buffer.Count == 0)
                {
                    plans.Add(PartPlan.Keep(entry));
                    continue;
                }

                splitEntries++;
                for (int r = 0; r < buffer.Count; r++)
                {
                    Renderer renderer = buffer[r];
                    string objectName = renderer != null ? renderer.gameObject.name : "Part";
                    plans.Add(PartPlan.SplitChild(MakeUniquePartName(objectName, usedNames), renderer, UnassignedDisplayColor));
                    createdParts++;
                }
            }

            if (splitEntries == 0)
            {
                validationMessage = "没有可拆的条目：每个部件都已经只覆盖一个 Renderer。";
                return;
            }

            partsProperty.ClearArray();
            for (int i = 0; i < plans.Count; i++)
            {
                WritePartPlan(partsProperty, i, plans[i]);
            }

            // 先把上面这一串数组改动落进对象，再重建表：ApplyTargets → ApplyIdentity 读的是组件上的
            // List（运行期真值），不 Apply 的话它重建的还是拆分前那份表，等于白拆一次。
            serializedObject.ApplyModifiedProperties();

            statusMessage = $"已拆分 {splitEntries} 个条目 → 新建 {createdParts} 个部件，共 {plans.Count} 个槽位；类别 / 标签 / 显示色待你分配。";
            ApplyTargets();
            Repaint();
        }

        private static bool IsFineGrained(HoObjectBufferPartEntry entry, List<Renderer> covered)
        {
            return entry != null && !entry.includeChildren && covered.Count == 1;
        }

        private static void WritePartPlan(SerializedProperty partsProperty, int index, in PartPlan plan)
        {
            partsProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = partsProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("name").stringValue = plan.name;
            // 枚举值从 0 起连续，enumValueIndex 即枚举值本身（与 HoObjectBufferPartCategory 的定义绑定）。
            element.FindPropertyRelative("category").enumValueIndex = (int)plan.category;
            element.FindPropertyRelative("tags").intValue = (int)plan.tags;
            element.FindPropertyRelative("displayColor").colorValue = plan.displayColor;
            element.FindPropertyRelative("includeChildren").boolValue = plan.includeChildren;

            Object[] objects = plan.sourceObjects ?? plan.renderers;
            SerializedProperty renderers = element.FindPropertyRelative("renderers");
            renderers.arraySize = objects.Length;
            for (int i = 0; i < objects.Length; i++)
            {
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
            }

            element.isExpanded = false;
        }

        /// <summary>拆分出来的名字取物体名（重名加序号）。这是唯一一个不用人填的值：它是身份的唯一键。</summary>
        private static string MakeUniquePartName(string baseName, HashSet<string> used)
        {
            string name = string.IsNullOrWhiteSpace(baseName) ? "Part" : baseName.Trim();
            if (used.Add(name))
            {
                return name;
            }

            for (int suffix = 2; ; suffix++)
            {
                string candidate = $"{name} ({suffix})";
                if (used.Add(candidate))
                {
                    return candidate;
                }
            }
        }

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
            // 条目改名会改变槽位/ID，改完必须重新编译表并把 RSUV 写回去。
            HoObjectBufferGroup.RefreshLoadedScenes();
            foreach (Object targetObject in targets)
            {
                if (targetObject is HoObjectBufferGroup group)
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

        private static Color GetEntryColor(Color baseColor, bool hover, bool dragging)
        {
            Color neutral = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.18f)
                : new Color(0.93f, 0.93f, 0.93f);
            float strength = 0.34f;
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

        private static Color GetChildColor(Color baseColor, bool hover)
        {
            Color neutral = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.18f)
                : new Color(0.93f, 0.93f, 0.93f);
            Color color = Color.Lerp(neutral, baseColor, hover ? 0.26f : 0.18f);
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
