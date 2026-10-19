using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    // Request a bounded set of actual editor renders. An idle Scene View does
    // not otherwise provide the samples needed by a temporal denoiser.
    [InitializeOnLoad]
    internal static class HoRTDISceneViewRepaint
    {
        private static SceneView pending;
        private static HoRTDIRendererFeature producer;
        private static double next;
        static HoRTDISceneViewRepaint()
        {
            if(Application.isBatchMode)return;
            RenderPipelineManager.endCameraRendering+=Rendered;
            EditorApplication.update+=Update;
        }
        private static void Rendered(ScriptableRenderContext rendering,Camera camera)
        {
            if(Application.isPlaying||camera.cameraType!=CameraType.SceneView)return;
            foreach(var feature in Resources.FindObjectsOfTypeAll<HoRTDIRendererFeature>())
            {
                var context=feature.ContextForCamera(camera);
                if(context?.nrd==null||!context.nrdHistoryValid||!context.nrd.SceneViewPreview)continue;
                if(context.nrd.SceneViewAccumulatedFrames>=HoRTDINrdContext.SceneViewSettledSamples){if(producer==feature)pending=null;return;}
                foreach(SceneView view in SceneView.sceneViews)
                    if(view!=null&&view.camera==camera){pending=view;producer=feature;return;}
            }
        }
        private static void Update()
        {
            if(pending==null||Application.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating||!InternalEditorUtility.isApplicationActive)return;
            var context=producer!=null?producer.ContextForCamera(pending.camera):null;
            if(producer==null||!producer.isActive||!producer.Settings.enabled||!producer.Settings.inSceneView
                ||context?.nrd==null||!context.nrdHistoryValid||context.nrd.SceneViewAccumulatedFrames>=HoRTDINrdContext.SceneViewSettledSamples)
            {pending=null;return;}
            double now=EditorApplication.timeSinceStartup;if(now<next)return;next=now+1.0/60;
            pending.Repaint();
        }
    }
}
