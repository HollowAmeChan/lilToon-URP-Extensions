using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public sealed class HoVolumeUIPreview : EditorWindow
{
    private VolumeProfile profile;
    private VolumeProfileEditor profileEditor;
    private readonly List<bool> oldExpansion = new List<bool>();
    private Vector2 scroll;
    private int page;
    private double captureAt;
    private readonly string[] tabs = { "全部名称", "GTAO", "CharacterShadow", "DebugHUD", "PlanarReflection", "ImageProcess", "SSGI" };
    private readonly string[] types = { null, "HoGTAOVolume", "HoCharacterShadowVolume", "HoDebugTileVolume", "HoPlanarReflectionVolume", "ImageProcessStackVolume", "HoSSGIVolume" };

    [MenuItem("HoLil/Validation/Open Volume UI Preview")]
    public static void Run()
    {
        EditorApplication.delayCall += () =>
        {
            foreach (var existing in Resources.FindObjectsOfTypeAll<HoVolumeUIPreview>()) existing.Close();
            var window = CreateInstance<HoVolumeUIPreview>();
            window.titleContent = new GUIContent("Ho Volume UI Preview");
            window.minSize = new Vector2(360, 450);
            window.position = new Rect(60, 60, 720, 900);
            window.ShowUtility();
        };
    }

    private void OnEnable()
    {
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (profileEditor == null) Build();
            page = 0;
            Build();
            Focus();
            captureAt = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += CapturePage;
        };
    }

    private void CapturePage()
    {
        if (this == null) { EditorApplication.update -= CapturePage; return; }
        Repaint();
        if (EditorApplication.timeSinceStartup < captureAt) return;
        var rect = position;
        var pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(new Vector2(rect.x, rect.y), (int)rect.width, (int)rect.height);
        var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false);
        texture.SetPixels(pixels); texture.Apply();
        Directory.CreateDirectory(HoVolumeUIBaseline.Output);
        File.WriteAllBytes(HoVolumeUIBaseline.Output + "/preview-" + page + ".png", texture.EncodeToPNG());
        DestroyImmediate(texture);
        if (++page >= tabs.Length)
        {
            EditorApplication.update -= CapturePage;
            Debug.Log("HO_VOLUME_UI_PREVIEW_COMPLETED");
            Close();
            EditorApplication.Exit(0);
            return;
        }
        Build(); Focus(); captureAt = EditorApplication.timeSinceStartup + 2;
    }

    private void Build()
    {
        Clear();
        profile = CreateInstance<VolumeProfile>();
        foreach (Type type in TypeCache.GetTypesDerivedFrom<VolumeComponent>()
            .Where(t => !t.IsAbstract && t.Assembly.GetName().Name == "jp.lilxyzw.liltoon.urp.extensions.Runtime").OrderBy(t => t.Name))
        {
            if (page != 0 && type.Name != types[page]) continue;
            var component = (VolumeComponent)CreateInstance(type);
            component.SetAllOverridesTo(true);
            profile.components.Add(component);
        }
        profileEditor = (VolumeProfileEditor)UnityEditor.Editor.CreateEditor(profile, typeof(VolumeProfileEditor));
        if (profileEditor.componentList.asset == null) profileEditor.componentList.Init(profile, profileEditor.serializedObject);
        foreach (var editor in profileEditor.componentList.editors)
        {
            // Inspector-layout fixture only: advertise URP support without changing project settings.
            typeof(VolumeComponentEditor).GetMethod("DetermineVisibility", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(editor, new object[] { typeof(UniversalRenderPipelineAsset), typeof(UniversalRenderPipeline) });
            oldExpansion.Add(editor.expanded);
            editor.expanded = page != 0;
        }
        scroll = Vector2.zero;
        Repaint();
        Debug.Log("HO_VOLUME_UI_PREVIEW_PAGE " + tabs[page]);
    }

    private void OnGUI()
    {
        int next = GUILayout.Toolbar(page, tabs, GUILayout.Height(24));
        if (next != page) { page = next; Build(); GUIUtility.ExitGUI(); }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (profileEditor != null)
            foreach (var editor in profileEditor.componentList.editors)
            {
                EditorGUILayout.LabelField(editor.GetDisplayTitle(), EditorStyles.boldLabel);
                if (page == 0) continue;
                editor.serializedObject.Update();
                editor.OnInspectorGUI();
                editor.serializedObject.ApplyModifiedProperties();
            }
        EditorGUILayout.EndScrollView();
    }

    private void OnDisable()
    {
        EditorApplication.update -= CapturePage; Clear();
    }

    private void Clear()
    {
        if (profileEditor != null)
        {
            var editors = profileEditor.componentList.editors;
            for (int i = 0; i < Math.Min(editors.Count, oldExpansion.Count); i++) editors[i].expanded = oldExpansion[i];
            DestroyImmediate(profileEditor);
        }
        oldExpansion.Clear();
        if (profile != null)
        {
            foreach (var component in profile.components) DestroyImmediate(component);
            DestroyImmediate(profile);
        }
    }
}
