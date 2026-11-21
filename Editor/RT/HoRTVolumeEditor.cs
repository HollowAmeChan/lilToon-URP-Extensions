using lilToon.URP.Extensions.RT;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RT
{
    [CustomEditor(typeof(HoRTVolume))]
    internal sealed class HoRTVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter enable;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<HoRTVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.enable));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(enable);
            EditorGUILayout.HelpBox("勾选左侧覆盖并关闭启用，可停止当前相机的公共 RT 场景准备及依赖它的 RTDI / RTGI / RTShadow。未覆盖时沿用 Feature 启用状态，Volume 不能开启已停用的 Feature。", MessageType.Info);
            EditorGUILayout.HelpBox("宿主运行状态见 Ho-RT Feature；图像调试与质量覆盖见各消费者的 Volume。", MessageType.None);
        }
    }
}
