using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor
{
    // Scope presentation changes to Volume inspectors, not renderer-feature or scene inspectors.
    internal abstract class HoVolumeComponentEditor : VolumeComponentEditor
    {
        public override GUIContent GetDisplayTitle()
        {
            var title = new GUIContent(base.GetDisplayTitle());
            title.tooltip = "参数左侧的勾选控制是否参与 Volume 覆盖；未覆盖时保留其它 Volume 或该功能的默认设置。";
            return title;
        }

        protected new bool PropertyField(SerializedDataParameter parameter)
        {
            return PropertyField(parameter, new GUIContent(parameter.displayName));
        }

        protected new bool PropertyField(SerializedDataParameter parameter, GUIContent label)
        {
            return base.PropertyField(parameter, HoVolumeEditorGUI.Label(parameter, label));
        }
    }

    internal static class HoVolumeEditorGUI
    {
        internal static GUIContent Label(SerializedDataParameter parameter, GUIContent label)
        {
            var content = new GUIContent(label);
            string declared = parameter.GetAttribute<TooltipAttribute>()?.tooltip;
            if (string.IsNullOrEmpty(content.tooltip)) content.tooltip = declared;
            else if (!string.IsNullOrEmpty(declared) && !content.tooltip.Contains(declared))
                content.tooltip = declared.Contains(content.tooltip) ? declared : declared + "\n\n" + content.tooltip;
            return content;
        }

        internal static bool Section(ref bool expanded, string title, string summary = null)
        {
            EditorGUILayout.Space(3);
            expanded = EditorGUILayout.Foldout(expanded, new GUIContent(title, summary), true);
            return expanded;
        }
    }
}
