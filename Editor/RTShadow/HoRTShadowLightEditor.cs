using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTShadow
{
    [CustomEditor(typeof(HoRTShadowLight)), CanEditMultipleObjects]
    internal sealed class HoRTShadowLightEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("供 Feature 的「场景声明灯」请求此灯。方向光的本组件角度优先于 Feature / Volume；停用组件后使用默认角度，是否继续生产取决于 Feature 请求范围。", MessageType.Info);
            if (targets.Length == 1)
            {
                var light = ((HoRTShadowLight)target).Light;
                if (light != null)
                {
                    EditorGUILayout.LabelField("运行期 LightId", HoRTLightIdentity.Get(light).ToString());
                    EditorGUILayout.LabelField("灯类型", light.type.ToString());
                    if (light.type != LightType.Directional)
                        EditorGUILayout.HelpBox("当前点 / 聚光只支持 delta 硬影，角直径不参与。未验证的其他类型会显式拒绝，详见 Feature 运行状态。", MessageType.Info);
                    if (light.cookie != null || light.cullingMask != -1 || light.lightmapBakeType == UnityEngine.LightmapBakeType.Baked)
                        EditorGUILayout.HelpBox("当前不支持 cookie、受限 caster linking 或纯烘焙灯；本灯将没有 Cast 产品。", MessageType.Warning);
                    using (new EditorGUI.DisabledScope(light.type != LightType.Directional))
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("angularDiameter"));
                }
            }
            else EditorGUILayout.PropertyField(serializedObject.FindProperty("angularDiameter"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
