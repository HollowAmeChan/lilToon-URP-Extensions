using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    // The same field traversal measures and draws a reorderable row, including conditional fields.
    internal sealed class HoCharacterEffectLayout
    {
        private readonly Rect bounds;
        private readonly bool draw;
        private float y;
        internal float Height => y - bounds.y;

        internal HoCharacterEffectLayout(Rect bounds, bool draw)
        {
            this.bounds = bounds;
            this.draw = draw;
            y = bounds.y;
        }

        internal void Property(SerializedProperty property, string label)
        {
            if (property == null) return;
            var content = new GUIContent(label, property.tooltip);
            float height = EditorGUI.GetPropertyHeight(property, content, true);
            if (draw) EditorGUI.PropertyField(new Rect(bounds.x, y, bounds.width, height), property, content, true);
            y += height + 2;
        }

        internal void Space(float pixels) { y += pixels; }
    }
}
