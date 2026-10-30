using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Hollow.HoMmdStages
{
    /// <summary>
    /// Scene-owned environment. The active stage may temporarily select its own quality
    /// pipeline; project Graphics Settings and unrelated scene environments stay untouched.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/MMD/Stage Environment")]
    public sealed class HoMmdStageEnvironment : MonoBehaviour
    {
        public UniversalRenderPipelineAsset pipeline;
        public Material skybox;
        public Light sun;
        public AmbientMode ambientMode = AmbientMode.Trilight;
        [ColorUsage(false, true)] public Color ambientSky = new Color(0.25f, 0.29f, 0.38f);
        [ColorUsage(false, true)] public Color ambientEquator = new Color(0.16f, 0.18f, 0.24f);
        [ColorUsage(false, true)] public Color ambientGround = new Color(0.10f, 0.09f, 0.13f);
        [Min(0)] public float ambientIntensity = 1;
        [Min(0)] public float reflectionIntensity = 0.7f;
        public bool fog;
        public Color fogColor = new Color(0.15f, 0.17f, 0.23f);
        public FogMode fogMode = FogMode.ExponentialSquared;
        [Min(0)] public float fogDensity = 0.003f;
        public float fogStartDistance = 15;
        public float fogEndDistance = 150;

        private static readonly HashSet<HoMmdStageEnvironment> Instances = new HashSet<HoMmdStageEnvironment>();
        private static HoMmdStageEnvironment owner;
        private static RenderPipelineAsset previousQualityPipeline;
        private static UniversalRenderPipelineAsset appliedPipeline;
        private static bool hasPipelineSnapshot;
        private static bool hooksInstalled;
        private static bool reconciling;
        private Snapshot previousEnvironment;
        private int appliedHash;
        private bool forceApply = true;

        private sealed class Snapshot
        {
            internal int sceneHandle;
            internal Material skybox;
            internal Light sun;
            internal AmbientMode ambientMode;
            internal Color ambientSky, ambientEquator, ambientGround, ambientLight, fogColor;
            internal float ambientIntensity, reflectionIntensity, fogDensity, fogStartDistance, fogEndDistance;
            internal bool fog;
            internal FogMode fogMode;

            internal static Snapshot Capture(int handle)
            {
                return new Snapshot
                {
                    sceneHandle = handle, skybox = RenderSettings.skybox, sun = RenderSettings.sun,
                    ambientMode = RenderSettings.ambientMode, ambientSky = RenderSettings.ambientSkyColor,
                    ambientEquator = RenderSettings.ambientEquatorColor, ambientGround = RenderSettings.ambientGroundColor,
                    ambientLight = RenderSettings.ambientLight, ambientIntensity = RenderSettings.ambientIntensity,
                    reflectionIntensity = RenderSettings.reflectionIntensity, fog = RenderSettings.fog,
                    fogColor = RenderSettings.fogColor, fogMode = RenderSettings.fogMode,
                    fogDensity = RenderSettings.fogDensity, fogStartDistance = RenderSettings.fogStartDistance,
                    fogEndDistance = RenderSettings.fogEndDistance
                };
            }

            internal void Restore()
            {
                // RenderSettings addresses the active scene. Never write the outgoing
                // scene's snapshot into a newly activated scene.
                if (SceneManager.GetActiveScene().handle != sceneHandle) return;
                RenderSettings.skybox = skybox; RenderSettings.sun = sun;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.fog = fog; RenderSettings.fogColor = fogColor;
                RenderSettings.fogMode = fogMode; RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStartDistance;
                RenderSettings.fogEndDistance = fogEndDistance;
            }
        }

        private void OnEnable()
        {
            InstallHooks();
            Instances.Add(this);
            forceApply = true;
            Reconcile();
        }

        private void OnDisable()
        {
            Instances.Remove(this);
            Reconcile();
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
            Reconcile();
        }

        private void OnValidate() { forceApply = true; }
        private void Update() { Reconcile(); }

        /// <summary>Apply serialized settings immediately after a builder changes fields.</summary>
        public void ApplyNow()
        {
            InstallHooks();
            if (isActiveAndEnabled) Instances.Add(this);
            forceApply = true;
            Reconcile();
        }

        private static bool Eligible(HoMmdStageEnvironment candidate, Scene active)
        {
            if (candidate == null || !candidate.isActiveAndEnabled || candidate.pipeline == null ||
                !active.IsValid() || !active.isLoaded || candidate.gameObject.scene != active) return false;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(candidate) ||
                UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(active)) return false;
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.scene == active) return false;
#endif
            return true;
        }

        private static void Reconcile()
        {
            if (reconciling) return;
            reconciling = true;
            try
            {
                Scene active = SceneManager.GetActiveScene();
                HoMmdStageEnvironment next = null;
                foreach (var candidate in Instances)
                    if (Eligible(candidate, active) &&
                        (next == null || candidate.GetInstanceID() < next.GetInstanceID())) next = candidate;

                if (!ReferenceEquals(owner, next))
                {
                    if (!ReferenceEquals(owner, null))
                    {
                        owner.previousEnvironment?.Restore();
                        owner.previousEnvironment = null;
                    }
                    owner = next;
                    if (owner == null)
                    {
                        RestorePipeline();
                        return;
                    }
                    if (!hasPipelineSnapshot)
                    {
                        previousQualityPipeline = QualitySettings.renderPipeline;
                        hasPipelineSnapshot = true;
                    }
                    owner.previousEnvironment = Snapshot.Capture(active.handle);
                    owner.forceApply = true;
                }
                if (owner == null) return;
                int hash = owner.SettingsHash();
                if (!owner.forceApply && owner.appliedHash == hash) return;
                if (QualitySettings.renderPipeline != owner.pipeline)
                    QualitySettings.renderPipeline = owner.pipeline;
                appliedPipeline = owner.pipeline;
                RenderSettings.skybox = owner.skybox;
                RenderSettings.sun = owner.sun;
                RenderSettings.ambientMode = owner.ambientMode;
                RenderSettings.ambientSkyColor = owner.ambientSky;
                RenderSettings.ambientEquatorColor = owner.ambientEquator;
                RenderSettings.ambientGroundColor = owner.ambientGround;
                if (owner.ambientMode == AmbientMode.Flat) RenderSettings.ambientLight = owner.ambientSky;
                RenderSettings.ambientIntensity = Mathf.Max(0, owner.ambientIntensity);
                RenderSettings.reflectionIntensity = Mathf.Max(0, owner.reflectionIntensity);
                RenderSettings.fog = owner.fog;
                RenderSettings.fogColor = owner.fogColor;
                RenderSettings.fogMode = owner.fogMode;
                RenderSettings.fogDensity = Mathf.Max(0, owner.fogDensity);
                RenderSettings.fogStartDistance = owner.fogStartDistance;
                RenderSettings.fogEndDistance = Mathf.Max(owner.fogStartDistance, owner.fogEndDistance);
                owner.appliedHash = hash;
                owner.forceApply = false;
            }
            finally { reconciling = false; }
        }

        private int SettingsHash()
        {
            unchecked
            {
                int hash = pipeline != null ? pipeline.GetInstanceID() : 0;
                hash = hash * 397 ^ (skybox != null ? skybox.GetInstanceID() : 0);
                hash = hash * 397 ^ (sun != null ? sun.GetInstanceID() : 0);
                hash = hash * 397 ^ ambientMode.GetHashCode();
                hash = hash * 397 ^ ambientSky.GetHashCode(); hash = hash * 397 ^ ambientEquator.GetHashCode();
                hash = hash * 397 ^ ambientGround.GetHashCode(); hash = hash * 397 ^ ambientIntensity.GetHashCode();
                hash = hash * 397 ^ reflectionIntensity.GetHashCode(); hash = hash * 397 ^ fog.GetHashCode();
                hash = hash * 397 ^ fogColor.GetHashCode(); hash = hash * 397 ^ fogMode.GetHashCode();
                hash = hash * 397 ^ fogDensity.GetHashCode(); hash = hash * 397 ^ fogStartDistance.GetHashCode();
                return hash * 397 ^ fogEndDistance.GetHashCode();
            }
        }

        private static void InstallHooks()
        {
            if (hooksInstalled) return;
            hooksInstalled = true;
            SceneManager.activeSceneChanged += ActiveSceneChanged;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
            Application.quitting += RestoreAll;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += RestoreAll;
            UnityEditor.EditorApplication.quitting += RestoreAll;
            UnityEditor.EditorApplication.playModeStateChanged += PlayModeChanged;
            UnityEditor.SceneManagement.EditorSceneManager.activeSceneChangedInEditMode += ActiveSceneChanged;
#endif
        }

        private static void ActiveSceneChanged(Scene previous, Scene current) { Reconcile(); }
        private static void SceneLoaded(Scene scene, LoadSceneMode mode) { Reconcile(); }
        private static void SceneUnloaded(Scene scene) { Reconcile(); }
        private static void RestorePipeline()
        {
            if (hasPipelineSnapshot && QualitySettings.renderPipeline == appliedPipeline)
                QualitySettings.renderPipeline = previousQualityPipeline;
            previousQualityPipeline = null;
            appliedPipeline = null;
            hasPipelineSnapshot = false;
        }
        private static void RestoreAll()
        {
            if (!ReferenceEquals(owner, null)) { owner.previousEnvironment?.Restore(); owner.previousEnvironment = null; }
            owner = null;
            RestorePipeline();
            foreach (var instance in Instances) if (instance != null) instance.forceApply = true;
        }
#if UNITY_EDITOR
        private static void PlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode ||
                state == UnityEditor.PlayModeStateChange.ExitingPlayMode) RestoreAll();
            else Reconcile();
        }
#endif
    }
}
