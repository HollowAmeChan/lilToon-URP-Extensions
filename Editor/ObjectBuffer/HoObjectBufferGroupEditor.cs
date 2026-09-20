using System.Collections.Generic;
using lilToon.URP.Extensions.Editor.PostProcessing;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.ObjectBuffer
{
    /// <summary>
    /// 组件抽屉：**左列 = 身份清单（部件 / 选区两张表，顶部切换），右列 = 选中的那一项的设置**
    /// （沿用后处理图层栈的排版惯例：扁平无边框按钮、窄面板自动折成一列、颜色块就是 debug 颜色）。
    /// <list type="bullet">
    /// <item>部件行：色块用它的 displayColor，右侧是它实际分到的 ID；**列表顺序 = 槽位 = ID 低字节**，可拖动排序；</item>
    /// <item>选区行：色块 + 名字 + 独立的 8 bit 选择 ID；(选择不进入像素的 ranked 层，排序只影响它的编号顺序)</item>
    /// <item>顶部 <c>+</c> / <c>-</c> 作用于**当前显示的那张表**；右列只画当前选中项的字段，
    /// 以后给部件加新的写入（物体位、SB 语义 lane…）直接往下排，不用挤宽度。</item>
    /// </list>
    /// </summary>
    [CustomEditor(typeof(HoObjectBufferGroup))]
    [CanEditMultipleObjects]
    internal sealed class HoObjectBufferGroupEditor : UnityEditor.Editor
    {
        private enum ListMode
        {
            Parts,
            Selections
        }

        /// <summary>左列宽度：装得下"名字 + 编号"，再宽就是白占右列的地方。</summary>
        private const float ListWidth = 128.0f;

        private const float RowHeight = 18.0f;
        private const float RowSpacing = 1.0f;
        private const float ElementHeight = 20.0f;
        private const float SectionSpacing = 4.0f;
        private const float SwitchWidth = 40.0f;

        /// <summary>两列之间的空隙：不留的话右列折叠三角会和左列右端的 +/- 挤在一起。</summary>
        private const float ColumnGap = 6.0f;

        /// <summary>
        /// 右列字段的标签宽度。Unity 默认按面板宽度取比例，宽面板下标签会把字段挤到最右边、
        /// 中间空一大片；这里钉成固定值，所有字段左对齐成一条线。
        /// </summary>
        private const float DetailLabelWidth = 84.0f;

        /// <summary>窄于这个宽度就不分列：清单折到上面，详情接在下面（跟着后处理那边的阈值习惯）。</summary>
        private const float MinSplitWidth = 300.0f;

        // 皮肤相关的颜色/样式**不能在静态初始化器里准备**（Unity 禁止在 ScriptableObject 构造期调
        // EditorGUIUtility / EditorStyles，读了会抛 TypeInitializationException 把抽屉整个打死），
        // 统一在 EnsureStyles() 里按需建一次。
        private static GUIStyle rowNameStyle;
        private static GUIStyle switchLabelStyle;
        private static GUIStyle switchActiveLabelStyle;
        private static bool stylesResolved;

        private static readonly Color RowHighlight = new Color(0.30f, 0.55f, 0.95f, 0.16f);
        private static readonly Color RowHover = new Color(1.0f, 1.0f, 1.0f, 0.06f);
        private static readonly Color RowAccent = new Color(0.35f, 0.65f, 1.0f, 0.85f);
        private static readonly Color ColumnBackground = new Color(0.0f, 0.0f, 0.0f, 0.10f);
        private static readonly Color NeutralRow = new Color(0.0f, 0.0f, 0.0f, 0.10f);

        private static readonly GUIContent AddPartLabel = new GUIContent("+", "添加一个部件（名字先给个占位，其余自己填）");
        private static readonly GUIContent RemovePartLabel = new GUIContent("-", "删除当前选中的部件");
        private static readonly GUIContent AddSelectionLabel = new GUIContent("+", "添加一个具名选区");
        private static readonly GUIContent RemoveSelectionLabel = new GUIContent("-", "删除当前选中的选区");
        private static readonly GUIContent RefreshLabel = new GUIContent("刷新全场景 RSUV", "重新编译 palette 并把 RSUV 索引写回所有 renderer（RSUV 不会被序列化，场景/域重载后必须重写）。");

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

        private ListMode listMode = ListMode.Parts;
        private int selectedPart;
        private int selectedSelection;
        private int draggingIndex = -1;
        private bool structureChanged;
        private bool showGroupSettings;

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

            if (EditorGUIUtility.currentViewWidth >= MinSplitWidth)
            {
                EditorGUILayout.BeginHorizontal();
                DrawListColumn();
                GUILayout.Space(ColumnGap);
                EditorGUILayout.BeginVertical();
                DrawDetailColumn();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                DrawListColumn();
                EditorGUILayout.Space(4.0f);
                DrawDetailColumn();
            }

            DrawFooter();

            // 拖动排序 / 切换表只在这里统一标记一次，避免每越过一行就重建一次表、白刷一屏日志。
            bool apply = serializedObject.ApplyModifiedProperties() | structureChanged;
            structureChanged = false;
            if (apply)
            {
                ApplyTargets();
                Repaint();
            }
        }

        // ------------------------------------------------------------------ 左列：身份清单

        private SerializedProperty ActiveList => listMode == ListMode.Parts ? partsProperty : selectionsProperty;

        private void DrawListColumn()
        {
            ClampSelection();

            using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
            {
                Rect header = EditorGUILayout.GetControlRect(false, RowHeight, GUILayout.Width(ListWidth));
                Rect partsRect = new Rect(header.x, header.y, SwitchWidth, header.height);
                Rect selectionsRect = new Rect(partsRect.xMax + 2.0f, header.y, SwitchWidth, header.height);
                Rect addRect = new Rect(header.xMax - 32.0f, header.y, 16.0f, header.height);
                Rect removeRect = new Rect(header.xMax - 16.0f, header.y, 16.0f, header.height);

                int partCount = partsProperty != null ? partsProperty.arraySize : 0;
                int selectionCount = selectionsProperty != null ? selectionsProperty.arraySize : 0;
                if (DrawSwitchButton(
                    partsRect,
                    new GUIContent("部件", $"部件（{partCount}）：像素里的 16 bit 身份（组 8 + 槽位 8）。列表顺序 = 槽位，可拖动排序。"),
                    listMode == ListMode.Parts))
                {
                    SwitchMode(ListMode.Parts);
                }

                if (DrawSwitchButton(
                    selectionsRect,
                    new GUIContent("选区", $"具名选区（{selectionCount}）：跨部件的命名集合，独立的 8 bit ID 空间，不与身份 ID 混用。"),
                    listMode == ListMode.Selections))
                {
                    SwitchMode(ListMode.Selections);
                }

                // 表头先处理完再读行数：+ / - 会当场改数组大小，先读行数的话这一帧就会拿着旧行数
                // 去索引已经变短的数组（"Retrieving array element that was out of bounds"）。
                bool parts = listMode == ListMode.Parts;
                int currentCount = parts ? partCount : selectionCount;
                if (EffectBrowserView.DrawChromeLessButton(addRect, parts ? AddPartLabel : AddSelectionLabel))
                {
                    AddEntry(parts);
                }

                using (new EditorGUI.DisabledScope(currentCount == 0))
                {
                    if (EffectBrowserView.DrawChromeLessButton(removeRect, parts ? RemovePartLabel : RemoveSelectionLabel, currentCount > 0))
                    {
                        RemoveSelectedEntry(parts);
                    }
                }

                ClampSelection();
                SerializedProperty list = ActiveList;
                int count = list != null ? list.arraySize : 0;

                float listHeight = Mathf.Max(RowHeight, count * (RowHeight + RowSpacing));
                // 宽度必须显式钉住：只给 options 的话在横向布局里行会被拉宽，右列的字段就跟着变窄。
                Rect area = GUILayoutUtility.GetRect(
                    ListWidth,
                    listHeight,
                    GUILayout.Width(ListWidth),
                    GUILayout.Height(listHeight));

                // 整列底色从视口最左铺到列的右边界：Unity 的那圈内边距就被填掉了，
                // 左边不再留一条空白；行自己的颜色再叠在这个底色上。
                float bleedLeft = 0.0f;
                float columnRight = area.xMax;
                EditorGUI.DrawRect(
                    new Rect(bleedLeft, header.y, columnRight - bleedLeft, area.yMax - header.y),
                    ColumnBackground);

                for (int i = 0; i < count && list != null && i < list.arraySize; i++)
                {
                    Rect row = new Rect(area.x, area.y + i * (RowHeight + RowSpacing), area.width, RowHeight);
                    if (parts)
                    {
                        DrawPartRow(row, bleedLeft, i);
                    }
                    else
                    {
                        DrawSelectionRow(row, bleedLeft, i);
                    }
                }

                if (Event.current.type == EventType.MouseUp && draggingIndex >= 0)
                {
                    draggingIndex = -1;
                    structureChanged = true;
                }
            }
        }

        private void DrawPartRow(Rect row, float bleedLeft, int index)
        {
            SerializedProperty entry = partsProperty.GetArrayElementAtIndex(index);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            SerializedProperty colorProperty = entry.FindPropertyRelative("displayColor");
            string partName = nameProperty != null ? nameProperty.stringValue : string.Empty;
            string id = BuildRowIdText(partName);
            string title = string.IsNullOrEmpty(partName) ? "（空名字）" : partName;

            Rect paint = DrawRowVisual(
                row,
                bleedLeft,
                colorProperty != null ? colorProperty.colorValue : Color.gray,
                title,
                id,
                $"{title}\n槽位 {index} · {id}\n列表顺序 = 槽位 = ID 的低字节；拖动可排序",
                index == selectedPart);

            HandleRowInput(paint, partsProperty, index, ref selectedPart);
        }

        private void DrawSelectionRow(Rect row, float bleedLeft, int index)
        {
            SerializedProperty entry = selectionsProperty.GetArrayElementAtIndex(index);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            SerializedProperty colorProperty = entry.FindPropertyRelative("displayColor");
            string selectionName = nameProperty != null ? nameProperty.stringValue : string.Empty;
            uint selectionId = HoObjectBufferRegistry.GetSelectionId(selectionName);
            string title = string.IsNullOrEmpty(selectionName) ? "（空名字）" : selectionName;

            Rect paint = DrawRowVisual(
                row,
                bleedLeft,
                colorProperty != null ? colorProperty.colorValue : Color.gray,
                title,
                selectionId > 0 ? selectionId.ToString() : "—",
                $"{title}\nCryptomatte ID {(selectionId > 0 ? selectionId.ToString() : "未注册")}\n跨部件的具名集合；材质侧引用的是名字",
                index == selectedSelection);

            HandleRowInput(paint, selectionsProperty, index, ref selectedSelection);
        }

        /// <summary>
        /// 整行按 displayColor 染色——面板上的颜色就是调试视图里那个颜色，一个颜色只表示一件事。
        /// 底色向编辑器背景混合，保证行里的文字仍然读得清；选中/悬停只调混合强度 + 左侧 accent 条。
        /// </summary>
        private static Color GetRowColor(Color baseColor, bool selected, bool hover)
        {
            Color neutral = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.18f, 1.0f)
                : new Color(0.93f, 0.93f, 0.93f, 1.0f);
            float strength = selected ? 0.52f : 0.34f;
            if (hover)
            {
                strength += 0.08f;
            }

            Color color = Color.Lerp(neutral, baseColor, Mathf.Clamp01(strength));
            color.a = 1.0f;
            return color;
        }

        /// <summary>
        /// 画一行，返回**实际染色的整条 rect**（从视口最左一直到列右边界）：
        /// 命中区要用它，否则"看着在行上、点下去没反应"的那条窄缝又回来了。
        /// </summary>
        private static Rect DrawRowVisual(Rect row, float bleedLeft, Color color, string title, string rightText, string tooltip, bool selected)
        {
            Rect paint = new Rect(bleedLeft, row.y, row.xMax - bleedLeft, row.height);
            bool hover = paint.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(paint, GetRowColor(color, selected, hover));
            if (selected)
            {
                EditorGUI.DrawRect(new Rect(bleedLeft, row.y, 2.0f, row.height), RowAccent);
            }

            Rect rightRect = new Rect(row.xMax - 40.0f, row.y, 36.0f, row.height);
            Rect nameRect = new Rect(
                row.x + 2.0f,
                row.y,
                Mathf.Max(0.0f, rightRect.x - row.x - 4.0f),
                row.height);

            GUI.Label(nameRect, new GUIContent(title, tooltip), rowNameStyle);
            EditorGUI.LabelField(rightRect, rightText, EditorStyles.centeredGreyMiniLabel);
            EditorGUIUtility.AddCursorRect(paint, MouseCursor.Link);
            return paint;
        }

        private void HandleRowInput(Rect row, SerializedProperty list, int index, ref int selectedIndex)
        {
            Event currentEvent = Event.current;
            if (!row.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                selectedIndex = index;
                draggingIndex = index;
                GUI.FocusControl(null);
                currentEvent.Use();
                Repaint();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && draggingIndex >= 0 && draggingIndex != index)
            {
                // 只在序列化状态里换位（ApplyModifiedProperties 会写回去），表等松手后再重建一次。
                list.MoveArrayElement(draggingIndex, index);
                selectedIndex = index;
                draggingIndex = index;
                currentEvent.Use();
            }
        }

        /// <summary>切换按钮：选中态加底色 + 底部 accent 条，未选中态只有悬停微亮（无按钮边框）。</summary>
        private static bool DrawSwitchButton(Rect rect, GUIContent content, bool active)
        {
            bool hovered = rect.Contains(Event.current.mousePosition);
            if (active)
            {
                EditorGUI.DrawRect(rect, RowHighlight);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2.0f, rect.width, 2.0f), RowAccent);
            }
            else if (hovered)
            {
                EditorGUI.DrawRect(rect, RowHover);
            }

            EditorGUI.LabelField(rect, content, active ? switchActiveLabelStyle : switchLabelStyle);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        private void SwitchMode(ListMode mode)
        {
            if (listMode == mode)
            {
                return;
            }

            listMode = mode;
            draggingIndex = -1;
            GUI.FocusControl(null);
            Repaint();
        }

        private void ClampSelection()
        {
            int partCount = partsProperty != null ? partsProperty.arraySize : 0;
            int selectionCount = selectionsProperty != null ? selectionsProperty.arraySize : 0;
            selectedPart = Mathf.Clamp(selectedPart, 0, Mathf.Max(0, partCount - 1));
            selectedSelection = Mathf.Clamp(selectedSelection, 0, Mathf.Max(0, selectionCount - 1));
        }

        private void AddEntry(bool parts)
        {
            SerializedProperty list = parts ? partsProperty : selectionsProperty;
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            SerializedProperty entry = list.GetArrayElementAtIndex(index);

            // 新条目要停在**类型的默认值**上：InsertArrayElementAtIndex 会复制上一条的字段，
            // 不逐字段重置的话，类别/标签/展开子级这些会从上一条"继承"过来（看着像自动填的，其实是脏的）。
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            if (nameProperty != null)
            {
                nameProperty.stringValue = parts ? $"部件 {index}" : $"选择 {index}";
            }

            if (parts)
            {
                var defaults = new HoObjectBufferPartEntry();
                entry.FindPropertyRelative("category").enumValueIndex = (int)defaults.category;
                entry.FindPropertyRelative("tags").intValue = (int)defaults.tags;
                entry.FindPropertyRelative("displayColor").colorValue = defaults.displayColor;
                entry.FindPropertyRelative("includeChildren").boolValue = defaults.includeChildren;
                SerializedProperty renderers = entry.FindPropertyRelative("renderers");
                if (renderers != null)
                {
                    renderers.ClearArray();
                }

                selectedPart = index;
            }
            else
            {
                var defaults = new HoObjectBufferSelectionEntry();
                entry.FindPropertyRelative("tags").intValue = (int)defaults.tags;
                entry.FindPropertyRelative("displayColor").colorValue = defaults.displayColor;
                selectedSelection = index;
            }

            structureChanged = true;
        }

        private void RemoveSelectedEntry(bool parts)
        {
            if (parts)
            {
                DeleteArrayElement(partsProperty, selectedPart);
                selectedPart = Mathf.Max(0, selectedPart - 1);
            }
            else
            {
                DeleteArrayElement(selectionsProperty, selectedSelection);
                selectedSelection = Mathf.Max(0, selectedSelection - 1);
            }

            structureChanged = true;
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

        private void DrawDetailColumn()
        {
            ClampSelection();

            // 标签宽度只在画右列时收紧，画完立刻还原（别影响面板里其他部分）。
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = DetailLabelWidth;
            try
            {
                if (listMode == ListMode.Parts)
                {
                    if (partsProperty != null && partsProperty.arraySize > 0)
                    {
                        DrawSelectedPart();
                    }
                }
                else if (selectionsProperty != null && selectionsProperty.arraySize > 0)
                {
                    DrawSelectedSelection();
                }

                DrawGroupSettings();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        private void DrawSelectedPart()
        {
            SerializedProperty entry = partsProperty.GetArrayElementAtIndex(selectedPart);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            SerializedProperty colorProperty = entry.FindPropertyRelative("displayColor");
            string partName = nameProperty != null ? nameProperty.stringValue : string.Empty;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawDetailHeader(
                    colorProperty != null ? colorProperty.colorValue : Color.gray,
                    string.IsNullOrEmpty(partName) ? "（空名字）" : partName,
                    BuildRowIdText(partName),
                    "这一项的身份：名字决定槽位，槽位决定像素里 ID 的低字节。");

                DrawProperty(nameProperty, new GUIContent("名字", "组内唯一。它决定槽位号 = 像素里 ID 的低字节。"));
                DrawProperty(entry.FindPropertyRelative("category"), new GUIContent("类别", "单值，回答「这是什么」。多归属语义请用标签位。"));
                DrawProperty(entry.FindPropertyRelative("tags"), new GUIContent("标签", "位掩码：一个部件同时属于多个语义时用它（例如 CharacterFull = 该组任意部件）。"));
                DrawProperty(colorProperty, new GUIContent("显示色", "debug 视图与面板色块用的颜色；像素里不存颜色，只存 ID。"));
                DrawProperty(entry.FindPropertyRelative("includeChildren"), new GUIContent("展开子级", "拖入 GameObject 或预制件实例时，包含它下面的子级 Renderer。"));

                EditorGUILayout.Space(2.0f);
                DrawRendererList(entry.FindPropertyRelative("renderers"));
            }

            EditorGUILayout.Space(SectionSpacing);
        }

        private void DrawSelectedSelection()
        {
            SerializedProperty entry = selectionsProperty.GetArrayElementAtIndex(selectedSelection);
            SerializedProperty nameProperty = entry.FindPropertyRelative("name");
            SerializedProperty colorProperty = entry.FindPropertyRelative("displayColor");
            string selectionName = nameProperty != null ? nameProperty.stringValue : string.Empty;
            uint selectionId = HoObjectBufferRegistry.GetSelectionId(selectionName);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawDetailHeader(
                    colorProperty != null ? colorProperty.colorValue : Color.gray,
                    string.IsNullOrEmpty(selectionName) ? "（空名字）" : selectionName,
                    selectionId > 0 ? selectionId.ToString() : "—",
                    "选区是跨部件的具名集合：它有自己的 8 bit ID 空间，材质侧引用的是名字。");

                DrawProperty(nameProperty, new GUIContent("名字", "全局唯一。材质里引用的是这个名字。"));
                DrawProperty(entry.FindPropertyRelative("tags"), new GUIContent("标签"));
                DrawProperty(colorProperty, new GUIContent("显示色", "debug 与 AOV manifest 用的颜色。"));
            }

            EditorGUILayout.Space(SectionSpacing);
        }

        private static void DrawDetailHeader(Color color, string title, string rightText, string tooltip)
        {
            Rect header = EditorGUILayout.GetControlRect(false, RowHeight);
            // 与左列选中的那一行同一种染色，一眼能把"左列选的是谁"和"右列在编辑谁"连起来。
            EditorGUI.DrawRect(header, GetRowColor(color, true, header.Contains(Event.current.mousePosition)));
            EditorGUI.DrawRect(new Rect(header.x, header.y, 2.0f, header.height), RowAccent);

            Rect rightRect = new Rect(header.xMax - 40.0f, header.y, 36.0f, header.height);
            GUI.Label(
                new Rect(header.x + 6.0f, header.y, Mathf.Max(0.0f, rightRect.x - header.x - 8.0f), header.height),
                new GUIContent(title, tooltip),
                rowNameStyle);
            EditorGUI.LabelField(rightRect, rightText, EditorStyles.centeredGreyMiniLabel);
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
                // 组 ID 是 1-255 的索引，不是连续可调的参数：给个整数框，比一条 250px 的滑块省地方也更好对齐。
                var groupIdLabel = new GUIContent("组 ID", "1-255，它是 ID 的高字节。组 0 保留：0 表示「未注册」，也是 RSUV 被重置后的值。");
                int groupId = Mathf.Clamp(groupIdProperty != null ? groupIdProperty.intValue : 1, 1, HoObjectBufferPaletteLimits.MaxGroups - 1);
                int editedGroupId = EditorGUILayout.DelayedIntField(groupIdLabel, groupId);
                editedGroupId = Mathf.Clamp(editedGroupId, 1, HoObjectBufferPaletteLimits.MaxGroups - 1);
                if (groupIdProperty != null && editedGroupId != groupIdProperty.intValue)
                {
                    groupIdProperty.intValue = editedGroupId;
                    structureChanged = true;
                }

                if (groupIdProperty != null && groupIdProperty.intValue == 0)
                {
                    validationMessage = "组 ID 是 0（保留值）：这个组不会写进 palette。随便改成 1-255 即可。";
                }

                DrawProperty(groupTagsProperty, new GUIContent("组级标签", "放在组表那一行，用于「整组」语义（例如 CharacterFull），不必在每个部件行重复。"));
                DrawProperty(priorityProperty, new GUIContent("优先级", "同一个 Renderer 被多个组命中时优先级高者生效；相同时离 Renderer 最近的组生效。"));
                DrawProperty(faceBoneProperty, new GUIContent("面部朝向", "角色朝向的参考 Transform（骨骼或朝向正确的空物体）。逐像素朝向会与层 0 的获胜身份同步 resolve；留空表示不产出朝向图。"));
                DrawProperty(faceForwardAxisProperty, new GUIContent("脸前轴", "骨骼的哪个局部轴作为「脸前方」。默认 +Z。"));
                DrawProperty(faceRightAxisProperty, new GUIContent("右轴", "骨骼的哪个局部轴作为「角色右侧」。默认 +X。"));
                DrawProperty(faceUpAxisProperty, new GUIContent("上轴", "骨骼的哪个局部轴作为「角色上方」。默认 +Y。"));
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

        private void DrawRendererList(SerializedProperty property)
        {
            if (property == null || !property.isArray)
            {
                return;
            }

            RemoveInvalidEntries(property);
            EditorGUILayout.LabelField($"Renderer（{property.arraySize}）", EditorStyles.miniBoldLabel);

            if (property.arraySize == 0)
            {
                Rect zone = EditorGUILayout.GetControlRect(false, 16.0f);
                EditorGUI.DrawRect(zone, NeutralRow);
                GUI.Label(new Rect(zone.x + 4.0f, zone.y, zone.width - 8.0f, zone.height), "把 GameObject / Renderer 拖到这里", EditorStyles.miniLabel);
                HandleDrop(zone, property);
                return;
            }

            for (int i = 0; i < property.arraySize; i++)
            {
                if (DrawObjectElement(property, i))
                {
                    i--;
                }
            }

            Rect dropZone = EditorGUILayout.GetControlRect(false, 8.0f);
            EditorGUI.DrawRect(dropZone, NeutralRow);
            HandleDrop(dropZone, property);
        }

        private bool DrawObjectElement(SerializedProperty property, int index)
        {
            SerializedProperty element = property.GetArrayElementAtIndex(index);
            Object current = element.objectReferenceValue;
            Rect rect = EditorGUILayout.GetControlRect(false, ElementHeight);
            Rect fieldRect = new Rect(rect.x, rect.y + 1.0f, rect.width - 26.0f, EditorGUIUtility.singleLineHeight);
            Rect removeRect = new Rect(rect.xMax - 20.0f, rect.y + 1.0f, 20.0f, EditorGUIUtility.singleLineHeight);

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

        private static void EnsureStyles()
        {
            if (!stylesResolved)
            {
                stylesResolved = true;
                rowNameStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Ellipsis
                };
                switchLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(6, 2, 0, 0)
                };
                switchActiveLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(6, 2, 0, 0)
                };
            }
        }
    }
}
