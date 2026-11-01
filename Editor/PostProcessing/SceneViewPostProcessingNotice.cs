using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal static class SceneViewPostProcessingNotice
    {
        internal static void Draw(bool showInSceneView)
        {
            var view = SceneView.lastActiveSceneView;
            if (!showInSceneView || view == null || view.camera == null ||
                CoreUtils.ArePostProcessesEnabled(view.camera))
                return;

            EditorGUILayout.HelpBox(
                "Scene View 的 URP 后处理已关闭，但 Ho 栈仍在运行。缺少曝光、色调映射和 Bloom，" +
                "预览与 Game View 的亮度、发光效果可能不同。",
                MessageType.Warning);
            if (view.cameraMode.drawMode == DrawCameraMode.Wireframe)
            {
                EditorGUILayout.HelpBox("当前为线框模式，请切回着色模式后比较后处理。", MessageType.Info);
                return;
            }
            if (GUILayout.Button("开启此 Scene View 的完整后处理"))
            {
                view.sceneViewState.fxEnabled = true;
                view.sceneViewState.showImageEffects = true;
                view.Repaint();
            }
        }
    }
}
