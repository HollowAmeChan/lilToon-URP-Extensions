using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor
{
    public static class HoVolumeUIValidation
    {
        [MenuItem("HoLil/Validation/Validate Volume UI")]
        public static void Validate()
        {
            var names = new HashSet<string>();
            foreach (FieldInfo field in typeof(HoVolumeNames).GetFields(BindingFlags.Public | BindingFlags.Static))
                names.Add((string)field.GetRawConstantValue());
            int count = 0, tooltips = 0;
            foreach (Type type in TypeCache.GetTypesDerivedFrom<VolumeComponent>())
            {
                if (type.IsAbstract || type.Assembly != typeof(HoVolumeNames).Assembly) continue;
                var menu = type.GetCustomAttribute<VolumeComponentMenu>();
                Require(menu != null && menu.menu.StartsWith("Ho ") && !menu.menu.Contains("/") && !menu.menu.EndsWith("Volume"),
                    type.Name + " must declare a flat Ho menu name without a Volume suffix.");
                Require(names.Remove(menu.menu), "Unknown or duplicate Volume name: " + menu.menu);
                var component = (VolumeComponent)ScriptableObject.CreateInstance(type);
                UnityEditor.Editor editor = null;
                try
                {
                    string before = JsonUtility.ToJson(component);
                    editor = UnityEditor.Editor.CreateEditor(component);
                    Require(editor is HoVolumeComponentEditor, type.Name + " must use the shared Volume inspector.");
                    var volumeEditor = (HoVolumeComponentEditor)editor;
                    Require(volumeEditor.GetDisplayTitle().text == menu.menu, "Header/menu mismatch: " + type.Name);
                    Require(!string.IsNullOrEmpty(volumeEditor.GetDisplayTitle().tooltip), "Missing override help on " + type.Name);
                    var unpack = typeof(VolumeComponentEditor).GetMethod("Unpack", BindingFlags.Instance | BindingFlags.NonPublic);
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                    {
                        var hint = field.GetCustomAttribute<TooltipAttribute>();
                        if (hint == null || !typeof(VolumeParameter).IsAssignableFrom(field.FieldType)) continue;
                        var parameter = (SerializedDataParameter)unpack.Invoke(editor, new object[] { editor.serializedObject.FindProperty(field.Name) });
                        GUIContent label = HoVolumeEditorGUI.Label(parameter, new GUIContent("自定义标签", "当前模式说明"));
                        Require(label.tooltip.Contains(hint.tooltip) && label.tooltip.Contains("当前模式说明"), "Tooltip lost on " + type.Name + "." + field.Name);
                        tooltips++;
                    }
                    Require(JsonUtility.ToJson(component) == before, "Opening the inspector changed serialized defaults: " + type.Name);
                    count++;
                }
                finally
                {
                    if (editor != null) Object.DestroyImmediate(editor);
                    Object.DestroyImmediate(component);
                }
            }
            Require(names.Count == 0, "A declared Volume name has no component.");
            Debug.Log($"HO_VOLUME_UI_VALIDATION_PASSED components={count} parameterTooltips={tooltips}");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[Ho Volume UI] " + message);
        }
    }
}
