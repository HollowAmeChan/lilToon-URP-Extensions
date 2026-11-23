using System;
using System.Collections;
using System.Reflection;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.CharacterShadow
{
    public static partial class HoCharacterShadowValidation
    {
        // CPU frame preparation only: no native cull/list call or user asset is modified.
        [MenuItem("HoLil/Validation/Validate Ho-CharacterShadow Frame Reuse")]
        public static void ValidateFrameReuse()
        {
            Require(Application.isBatchMode, "Frame reuse validation runs in an isolated batch editor only");
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            GameObject root = null;
            object pass = null;
            MethodInfo dispose = null;
            try
            {
                root = new GameObject("CS Frame Reuse Validation");
                const int fixtureLayer = 31;
                var sunObject = new GameObject("Reuse Sun");
                sunObject.transform.SetParent(root.transform);
                Light sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.cullingMask = 1 << fixtureLayer;
                sun.transform.rotation = Quaternion.identity;

                Camera MakeCamera(string name, Vector3 position)
                {
                    var cameraObject = new GameObject(name);
                    cameraObject.transform.SetParent(root.transform);
                    Camera result = cameraObject.AddComponent<Camera>();
                    result.enabled = false;
                    result.orthographic = true;
                    result.orthographicSize = 3;
                    result.aspect = 1;
                    result.nearClipPlane = 0.1f;
                    result.farClipPlane = 30;
                    result.transform.SetPositionAndRotation(position, Quaternion.identity);
                    return result;
                }

                Camera a = MakeCamera("Reuse Camera A", new Vector3(0, 0, -10));
                Camera b = MakeCamera("Reuse Camera B", new Vector3(1000, 0, -10));
                var receiver = GameObject.CreatePrimitive(PrimitiveType.Quad);
                receiver.name = "Reuse Receiver";
                receiver.transform.SetParent(root.transform);
                receiver.layer = fixtureLayer;
                MeshRenderer receiverRenderer = receiver.GetComponent<MeshRenderer>();
                receiverRenderer.shadowCastingMode = ShadowCastingMode.Off;
                var group = receiver.AddComponent<HoObjectBufferGroup>();
                group.parts.Add(new HoObjectBufferPartEntry
                {
                    name = "Receiver", includeChildren = false, renderers = new Object[] { receiverRenderer }
                });
                group.Apply();
                var subject = receiver.AddComponent<HoCharacterShadow>();
                subject.objectGroup = group;
                subject.center = Vector3.zero;
                subject.size = Vector3.one * 2;

                var caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
                caster.name = "Animated Offscreen Upstream Caster";
                caster.transform.SetParent(root.transform);
                caster.layer = fixtureLayer;
                caster.transform.position = new Vector3(0, 0, -25);
                MeshRenderer casterRenderer = caster.GetComponent<MeshRenderer>();
                casterRenderer.shadowCastingMode = ShadowCastingMode.Off;

                Assembly runtime = typeof(HoCharacterShadow).Assembly;
                Type configType = runtime.GetType("lilToon.URP.Extensions.CharacterShadow.HoCharacterShadowRenderConfig", true);
                Type passType = runtime.GetType("lilToon.URP.Extensions.CharacterShadow.HoCharacterShadowPass", true);
                MethodInfo resolve = configType.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo build = passType.GetMethod("BuildFrame", BindingFlags.Instance | BindingFlags.NonPublic);
                dispose = passType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(resolve != null && build != null && dispose != null, "Frame reuse reflection entry points are missing");
                object config = resolve.Invoke(null, new object[] { new HoCharacterShadowSettings(), null });
                pass = Activator.CreateInstance(passType, true);
                object Rebuild(Camera camera) => build.Invoke(pass,
                    new[] { (object)camera, sun, camera.worldToCameraMatrix, camera.projectionMatrix, config });
                IList Slices(object value) => Field<IList>(value, "slices");
                float GroupSlice(object value) => Field<Vector4[]>(value, "groupSlices")[group.groupId / 4][group.groupId % 4];
                FieldInfo ValidField(object slice) => slice.GetType().GetField("valid", BindingFlags.Instance | BindingFlags.NonPublic);

                object frameA = Rebuild(a);
                Require(Slices(frameA).Count == 1 && GroupSlice(frameA) == 1, "Initial receiver was not prepared");
                object sliceA = Slices(frameA)[0];
                float baselineFar = Field<float>(sliceA, "farPlane");
                ValidField(sliceA).SetValue(sliceA, true); // Simulate the completed previous native cull.
                Vector4[] partMasks = Field<Vector4[]>(frameA, "partMasks");
                partMasks[0].w = 1; // Unused part must not survive the next active rebuild.
                Vector4[] parameters = Field<Vector4[]>(frameA, "parameters");
                parameters[0].z = 1; // Simulate RestoreAndPublish from the previous render.
                subject.receiveWholeGroup = false;
                subject.receiverParts.Clear();
                Require(ReferenceEquals(frameA, Rebuild(a)), "Camera A did not reuse frame storage");
                Require(Slices(frameA).Count == 0 && GroupSlice(frameA) == 0,
                    "An empty receiver selection retained a previous active slice/group mapping");
                foreach (Vector4 mapping in Field<Vector4[]>(frameA, "groupSlices"))
                    Require(mapping == Vector4.zero, "An empty frame retained another stale group mapping");

                subject.receiveWholeGroup = true;
                Require(ReferenceEquals(frameA, Rebuild(a)) && Slices(frameA).Count == 1, "Receiver did not return on the reused frame");
                Require(ReferenceEquals(sliceA, Slices(frameA)[0]), "Slice storage was not reused");
                Require(!Field<bool>(sliceA, "valid") && parameters[0].z == 0,
                    "Reactivated slice inherited the previous cull/published valid flag");
                Require(GroupSlice(frameA) == 1 && partMasks[0].w == 0, "Reactivated receiver inherited a stale part/group mask");

                casterRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                Require(!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(a), casterRenderer.bounds),
                    "Upstream caster fixture is visible to the viewing camera");
                Rebuild(a);
                float firstFar = Field<float>(sliceA, "farPlane");
                float firstNear = Field<float>(sliceA, "nearPlane");
                Vector3 firstOrigin = Field<Vector3>(sliceA, "origin");
                Matrix4x4 firstProjection = Field<Matrix4x4>(sliceA, "projection");
                Require(firstFar > baselineFar + 20, "Off-screen upstream caster was not included");
                caster.transform.position = new Vector3(0, 0, -75);
                Rebuild(a);
                Require(Field<float>(sliceA, "farPlane") > firstFar + 45
                    && Field<Vector3>(sliceA, "origin").z < firstOrigin.z - 45,
                    "Animated upstream caster bounds were cached instead of refreshed per render");
                Require(Mathf.Approximately(Field<float>(sliceA, "nearPlane"), firstNear)
                    && Mathf.Approximately(Field<Matrix4x4>(sliceA, "projection").m00, firstProjection.m00)
                    && Mathf.Approximately(Field<Matrix4x4>(sliceA, "projection").m11, firstProjection.m11),
                    "Updating caster depth changed the near-plane/XY precision contract");
                caster.transform.position = new Vector3(100, 0, -75);
                Rebuild(a);
                Require(Mathf.Abs(Field<float>(sliceA, "farPlane") - baselineFar) < 0.001f,
                    "A moved-out upstream caster left its previous depth range behind");

                ValidField(sliceA).SetValue(sliceA, true);
                object frameB = Rebuild(b);
                Require(!ReferenceEquals(frameA, frameB)
                    && !ReferenceEquals(Field<Vector4[]>(frameA, "groupSlices"), Field<Vector4[]>(frameB, "groupSlices")),
                    "Different cameras share mutable frame storage");
                Require(Slices(frameB).Count == 0 && GroupSlice(frameB) == 0, "Off-target camera B inherited camera A receivers");
                Require(Slices(frameA).Count == 1 && GroupSlice(frameA) == 1 && Field<bool>(sliceA, "valid"),
                    "Building camera B changed camera A's queued frame");
                b.transform.position = a.transform.position;
                Require(ReferenceEquals(frameB, Rebuild(b)) && Slices(frameB).Count == 1, "Camera B did not reuse its own frame");
                Require(!ReferenceEquals(sliceA, Slices(frameB)[0]) && !Field<bool>(Slices(frameB)[0], "valid")
                    && Field<bool>(sliceA, "valid"), "Camera B inherited or reset camera A's slice state");
                Object.DestroyImmediate(b.gameObject);
                Rebuild(a);
                Require(Field<IDictionary>(pass, "cameraResources").Count == 1, "Destroyed camera frame cache was not removed");

                Type frameType = runtime.GetType("lilToon.URP.Extensions.CharacterShadow.HoCharacterShadowFrame", true);
                MethodInfo freshBuild = frameType.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic);
                Require(freshBuild != null, "Fresh frame allocation reference entry point is missing");
                // Both paths receive the same fixture and boxed arguments, including the same
                // reflection invocation overhead. Count managed allocations, not native/GPU memory.
                object[] allocationArguments = { a, sun, a.worldToCameraMatrix, a.projectionMatrix, config };
                const int warmupIterations = 8;
                const int measuredIterations = 64;
                long MeasureAllocations(MethodInfo method, object target)
                {
                    for (int iteration = 0; iteration < warmupIterations; iteration++)
                        method.Invoke(target, allocationArguments);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int iteration = 0; iteration < measuredIterations; iteration++)
                        method.Invoke(target, allocationArguments);
                    return GC.GetAllocatedBytesForCurrentThread() - before;
                }
                long probeBefore = GC.GetAllocatedBytesForCurrentThread();
                var allocationProbe = new byte[65536];
                long probeBytes = GC.GetAllocatedBytesForCurrentThread() - probeBefore;
                GC.KeepAlive(allocationProbe);
                long freshBytes = MeasureAllocations(freshBuild, null);
                long cachedBytes = MeasureAllocations(build, pass);
                if (probeBytes >= allocationProbe.Length)
                {
                    double freshMean = freshBytes / (double)measuredIterations;
                    double cachedMean = cachedBytes / (double)measuredIterations;
                    Debug.Log($"[Ho-CS Frame Reuse] Managed allocation mean: fresh={freshMean:F0} B, cached={cachedMean:F0} B, "
                        + $"saved={freshMean - cachedMean:F0} B per rebuild; warmup={warmupIterations}, samples={measuredIterations}.");
                    Require(freshBytes - cachedBytes >= 8192L * measuredIterations,
                        $"Cached frame preparation saved less than 8 KiB per rebuild: fresh={freshMean:F0} B, cached={cachedMean:F0} B");
                }
                else
                {
                    Debug.LogWarning($"[Ho-CS Frame Reuse] Allocation counter unavailable: a 64 KiB probe reported {probeBytes} B. "
                        + "Skipped byte-reduction assertion; storage identity and repeated rebuild checks remain active.");
                }
                Require(ReferenceEquals(frameA, Rebuild(a)) && ReferenceEquals(sliceA, Slices(frameA)[0])
                    && ReferenceEquals(parameters, Field<Vector4[]>(frameA, "parameters"))
                    && ReferenceEquals(partMasks, Field<Vector4[]>(frameA, "partMasks")),
                    "Repeated rebuilds replaced the cached frame, slice, or shader arrays");

                Debug.Log("[Ho-CS Frame Reuse] PASS: active/empty/active masks and validity, per-render animated upstream bounds, independent camera frames, destroyed-camera cleanup, repeated storage reuse. Allocation assertion runs only with a verified counter.");
            }
            finally
            {
                if (pass != null) dispose?.Invoke(pass, null);
                if (root != null) Object.DestroyImmediate(root);
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                if (SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
