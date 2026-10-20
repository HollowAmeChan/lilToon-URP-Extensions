using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Atmosphere
{
    internal static class HoAtmosphereDustPresets
    {
        private readonly struct Preset
        {
            internal readonly string label, description;
            internal readonly float density, size, tiling, brightness, flutter, distance;
            internal readonly int budget;
            internal Preset(string label, string description, float density, float size, float tiling, float brightness, float flutter, float distance, int budget)
            {
                this.label = label; this.description = description; this.density = density; this.size = size;
                this.tiling = tiling; this.brightness = brightness; this.flutter = flutter; this.distance = distance; this.budget = budget;
            }
            internal bool Matches(SerializedProperty dust)
            {
                var texture = dust.FindPropertyRelative("texture");
                var maxClusters = dust.FindPropertyRelative("maxClusters");
                return !texture.hasMultipleDifferentValues && texture.objectReferenceValue == null &&
                    !maxClusters.hasMultipleDifferentValues && maxClusters.intValue == budget &&
                    Equal(dust, "grainDensity", density) && Equal(dust, "clusterSize", size) && Equal(dust, "textureWorldSize", tiling) &&
                    Equal(dust, "brightness", brightness) && Equal(dust, "flutter", flutter) && Equal(dust, "maxDistance", distance);
            }
            private static bool Equal(SerializedProperty dust, string name, float expected)
            {
                var property = dust.FindPropertyRelative(name);
                return !property.hasMultipleDifferentValues && Mathf.Approximately(property.floatValue, expected);
            }
        }
        private static readonly Preset[] All =
        {
            new("轻微浮尘", "稀疏、轻柔的小尘点，适合安静的室内。", .4f, 8, 8, 12, .08f, 20, 24),
            new("细密尘埃（默认）", "细密而清晰的灰尘，作为日常调节的起点。", 1, 8, 8, 20, .12f, 30, 64),
            new("浓密光束尘", "更细、更密、更亮，适合强调光束内的尘埃；绘制开销较高。", 1, 8, 5, 30, .15f, 30, 96),
            new("大颗粒漂浮", "较大的尘点和较明显的漂浮，适合近景观察。", .7f, 8, 16, 18, .25f, 20, 32),
            new("低开销", "16 张簇片、18 米显示距离，适合先控制开销再调效果。", .75f, 8, 8, 20, .1f, 18, 16)
        };
        private static void Apply(Object[] targets, Preset preset)
        {
            var alive = System.Array.FindAll(targets, value => value != null);
            if (alive.Length == 0) return;
            var serialized = new SerializedObject(alive);
            var dust = serialized.FindProperty("dust");
            dust.FindPropertyRelative("enabled").boolValue = true;
            dust.FindPropertyRelative("texture").objectReferenceValue = null;
            dust.FindPropertyRelative("grainDensity").floatValue = preset.density;
            dust.FindPropertyRelative("clusterSize").floatValue = preset.size;
            dust.FindPropertyRelative("textureWorldSize").floatValue = preset.tiling;
            dust.FindPropertyRelative("brightness").floatValue = preset.brightness;
            dust.FindPropertyRelative("flutter").floatValue = preset.flutter;
            dust.FindPropertyRelative("maxDistance").floatValue = preset.distance;
            dust.FindPropertyRelative("maxClusters").intValue = preset.budget;
            Undo.SetCurrentGroupName("应用灰尘预设：" + preset.label);
            serialized.ApplyModifiedProperties();
        }
        internal static void Draw(SerializedObject serialized)
        {
            var dust = serialized.FindProperty("dust");
            int current = System.Array.FindIndex(All, preset => preset.Matches(dust));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("灰尘效果预设");
                var content = new GUIContent(current >= 0 ? All[current].label : "自定义",
                    "套用内置尘点贴图与灰尘参数，并启用灰尘。保留随机种子、风速、介质和散射光学设置。支持多选与撤销。");
                if (GUILayout.Button(content, EditorStyles.popup))
                {
                    var menu = new GenericMenu();
                    var targets = serialized.targetObjects;
                    for (int i = 0; i < All.Length; ++i)
                    {
                        var preset = All[i];
                        menu.AddItem(new GUIContent(preset.label, preset.description), i == current, () => Apply(targets, preset));
                    }
                    menu.ShowAsContext();
                }
            }
            if (current >= 0) EditorGUILayout.LabelField(All[current].description, EditorStyles.wordWrappedMiniLabel);
        }
    }
}
