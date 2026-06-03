using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    public sealed class HoGradientTextureDrawer : MaterialPropertyDrawer
    {
        private readonly HoMaterialGradientPropertyGui gui = new();

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            gui.OnGUI(position, prop, label, editor);
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return HoMaterialGradientPropertyGui.PropertyHeight;
        }
    }
}
