using lilToon.URP.Extensions.MetadataBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MetadataBuffer
{
    [CustomEditor(typeof(HoMetadataBufferGroup))]
    [CanEditMultipleObjects]
    internal sealed class HoMetadataBufferGroupEditor : UnityEditor.Editor
    {
        private const float Spacing = 6.0f;
        private const float ChannelHeaderHeight = 32.0f;
        private const float ChannelElementHeight = 22.0f;
        private const float ChannelButtonWidth = 22.0f;

        private static readonly string[] ObjectCustomProperties =
        {
            "objectCustom0",
            "objectCustom1",
            "objectCustom2",
            "objectCustom3",
            "objectCustom4",
            "objectCustom5",
            "objectCustom6",
            "objectCustom7"
        };

        private static readonly GUIContent[] ObjectCustomLabels =
        {
            new GUIContent("CharacterFull / 全角色", "整套角色、衣服、背景道具和特效等需要作为完整对象参与后期的 Renderer。"),
            new GUIContent("脸", "脸部对象或 Renderer。"),
            new GUIContent("前发", "前发对象或 Renderer。眼透通常会用到这个通道。"),
            new GUIContent("眼睛", "眼睛对象或 Renderer。"),
            new GUIContent("眼透区域", "允许眼睛透出的区域对象或 Renderer。"),
            new GUIContent("配件", "配件对象或 Renderer。"),
            new GUIContent("CharacterBody / 人体", "人体或身体核心 Renderer。可用于在全角色轮廓之外继续强调身体局部分组。"),
            new GUIContent("预留 7", "预留给项目自定义 Object metadata。")
        };

        private static readonly Color[] ObjectCustomColors =
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

        private static readonly GUIContent IdOnlyLabel = new GUIContent("仅写 ID", "这些对象命中的 Renderer 只写入角色组 ID、部件 ID 和标记；ObjectCustom 位保持为 0。");
        private static readonly Color IdOnlyColor = new Color(0.40f, 0.62f, 0.78f);
        private static readonly GUIContent AddSlotLabel = new GUIContent("+", "添加空槽");
        private static readonly GUIContent ClearLabel = new GUIContent("×", "清空本通道");
        private static readonly GUIContent RefreshLabel = new GUIContent("刷新全场景 RSUV");
        private static GUIStyle channelNameStyle;

        private string validationMessage;

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();
            validationMessage = null;

            DrawIdentitySection();
            DrawObjectCustomSection();
            DrawPrioritySection();

            bool changed = serializedObject.ApplyModifiedProperties();
            bool refreshScene = false;
            EditorGUILayout.Space(Spacing);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(RefreshLabel, GUILayout.Width(150.0f)))
                {
                    refreshScene = true;
                }
            }

            if (!string.IsNullOrEmpty(validationMessage))
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
            }

            if (refreshScene)
            {
                HoMetadataBufferGroup.RefreshLoadedScenes();
                foreach (Object targetObject in targets)
                {
                    if (targetObject is HoMetadataBufferGroup group)
                    {
                        EditorUtility.SetDirty(group);
                    }
                }
            }
            else if (changed)
            {
                ApplyTargets();
            }
        }

        private void DrawIdentitySection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("characterId", new GUIContent("角色组 ID (CharacterId)"));
                DrawProperty("partId", new GUIContent("部件 ID (PartId)"));
                DrawProperty("flags", new GUIContent("标记 (Flags)"));
                EditorGUILayout.Space(2.0f);
                DrawObjectList("explicitRenderers", IdOnlyLabel, IdOnlyColor, "仅写 ID");
            }
        }

        private void DrawObjectCustomSection()
        {
            EditorGUILayout.Space(Spacing);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < ObjectCustomProperties.Length; i++)
                {
                    DrawObjectChannel(i);
                }
            }
        }

        private void DrawPrioritySection()
        {
            EditorGUILayout.Space(Spacing);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("includeChildrenForListedObjects", new GUIContent("展开预制件", "拖入 GameObject 或预制件实例时，包含它下面的子级 Renderer；关闭时只使用物体自身的 Renderer。"));
                DrawProperty("priority", new GUIContent("优先级", "同一个 Renderer 被多个 HoMetadataBufferGroup 命中时，优先级高者生效；相同时离 Renderer 最近的组生效。"));
            }
        }

        private void DrawProperty(string propertyName, GUIContent label, bool includeChildren = false)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, label, includeChildren);
            }
        }

        private void DrawObjectChannel(int channelIndex)
        {
            GUIContent label = ObjectCustomLabels[channelIndex];
            DrawObjectList(
                ObjectCustomProperties[channelIndex],
                label,
                ObjectCustomColors[channelIndex],
                $"通道 {channelIndex}  {label.text}");
        }

        private void DrawObjectList(string propertyName, GUIContent label, Color channelColor, string displayName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                return;
            }

            RemoveInvalidEntries(property);
            Rect headerRect = EditorGUILayout.GetControlRect(false, ChannelHeaderHeight);
            DrawChannelHeader(headerRect, property, label, channelColor, displayName);
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

        private static void DrawChannelHeader(Rect rect, SerializedProperty property, GUIContent label, Color channelColor, string displayName)
        {
            Event currentEvent = Event.current;
            bool hover = rect.Contains(currentEvent.mousePosition);
            bool dragging = hover && (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform);
            EditorGUI.DrawRect(rect, GetChannelColor(channelColor, hover, dragging, false));
            Rect foldoutRect = new Rect(rect.x + 4.0f, rect.y + 7.0f, 14.0f, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none, true);

            Rect labelRect = new Rect(rect.x + 24.0f, rect.y + 6.0f, rect.width - 122.0f, 20.0f);
            GUI.Label(labelRect, new GUIContent(displayName, label.tooltip), channelNameStyle);

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
                    validationMessage = "这里只接受场景或 Prefab 模式里的 GameObject / Renderer。Mesh、MeshFilter、材质和 prefab 资源不会写入 RSUV。";
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

        private static void InsertEmptySlot(SerializedProperty property)
        {
            int index = property.arraySize;
            property.InsertArrayElementAtIndex(index);
            property.GetArrayElementAtIndex(index).objectReferenceValue = null;
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

        private void ApplyTargets()
        {
            foreach (Object targetObject in targets)
            {
                if (targetObject is HoMetadataBufferGroup group)
                {
                    group.Apply();
                    EditorUtility.SetDirty(group);
                }
            }
        }

        private static void EnsureStyles()
        {
            if (channelNameStyle != null)
            {
                return;
            }

            channelNameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
        }
    }
}
