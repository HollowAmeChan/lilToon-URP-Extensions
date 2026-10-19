using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Atmosphere
{
    internal static class HoAtmosphereDensityPresets
    {
        internal const string AssetFolder = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Atmosphere/Textures/";
        internal readonly struct Preset
        {
            internal readonly string file, label, description;
            internal Preset(string file, string label, string description) { this.file = file; this.label = label; this.description = description; }
        }
        internal static readonly Preset[] All =
        {
            new("HoAtmosphereFineDust64", "细密灰尘", "细密的连续密度扰动；不是独立灰尘颗粒。"),
            new("HoAtmosphereSoftFog64", "柔和雾团", "平缓的大块密度起伏，适合室内薄雾。"),
            new("HoAtmosphereWisps64", "丝状流雾", "沿世界 X 方向拉长的卷曲纹理，适合缓慢流动的雾丝。"),
            new("HoAtmospherePatchyFog64", "斑驳浓雾", "对比更强的疏密块状分布，适合强调局部明暗。")
        };
        internal static Texture3D Load(int index) => AssetDatabase.LoadAssetAtPath<Texture3D>(AssetFolder + All[index].file + ".asset");
        internal static void Apply(Object[] targets, Texture3D texture)
        {
            var alive = System.Array.FindAll(targets, value => value != null);
            if (alive.Length == 0) return;
            var serialized = new SerializedObject(alive);
            serialized.FindProperty("densityTexture").objectReferenceValue = texture;
            serialized.ApplyModifiedProperties();
        }
        internal static void Draw(Object[] targets)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("密度纹理预设");
                if (!GUILayout.Button(new GUIContent("选择…", "只替换空间密度纹理；保留密度、颜色、强度、尺度和风速。"), EditorStyles.popup)) return;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("均匀（无纹理）"), false, () => Apply(targets, null));
                for (int i = 0; i < All.Length; ++i)
                {
                    var preset = All[i]; var texture = Load(i);
                    var content = new GUIContent(preset.label, preset.description);
                    if (texture == null) menu.AddDisabledItem(content);
                    else menu.AddItem(content, false, () => Apply(targets, texture));
                }
                menu.ShowAsContext();
            }
        }
    }
}
