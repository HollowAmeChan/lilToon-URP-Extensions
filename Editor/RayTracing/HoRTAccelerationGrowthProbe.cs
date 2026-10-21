using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RayTracing
{
    // Editor-only reproduction using Unity's API directly, independent of HoRTScene.
    public static class HoRTAccelerationGrowthProbe
    {
        [Serializable]
        private sealed class Report
        {
            public bool pass, growth;
            public string error, unity, device;
            public int initialInstances, finalInstances, builds;
        }

        public static void RunInitialFive() => Run(false);
        public static void RunGrowth() => Run(true);

        private static void Run(bool growth)
        {
            var report = new Report { growth = growth, unity = Application.unityVersion,
                device = SystemInfo.graphicsDeviceName, initialInstances = growth ? 3 : 5, finalInstances = 5 };
            Mesh mesh = null;
            RayTracingAccelerationStructure acceleration = null;
            RenderTexture target = null;
            Texture2D readback = null;
            string output = null;
            try
            {
                if (!Application.dataPath.Replace('\\', '/').Contains("/.codex-research/RTGIValidation/"))
                    throw new InvalidOperationException("Dedicated GI project required.");
                output = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../ho-rt-native-as-" + (growth ? "growth" : "initial-five") + ".json"));
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12 || !SystemInfo.supportsRayTracing)
                    throw new NotSupportedException("D3D12 ray tracing required.");
                mesh = new Mesh { name = "Native AS growth control" };
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up,
                    new Vector3(2, 0, 0), new Vector3(3, 0, 0), new Vector3(2, 1, 0) };
                mesh.subMeshCount = 2;
                mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
                mesh.RecalculateBounds();
                acceleration = new RayTracingAccelerationStructure(new RayTracingAccelerationStructure.Settings
                {
                    managementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                    rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                    buildFlagsStaticGeometries = RayTracingAccelerationStructureBuildFlags.PreferFastTrace
                });
                target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBFloat);
                target.Create();
                readback = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
                for (int i = 0; i < report.initialInstances; i++) Add(i);
                BuildAndWait();
                if (growth)
                {
                    Add(3); Add(4);
                    BuildAndWait();
                }
                report.pass = true;

                void Add(int index)
                {
                    var config = new RayTracingMeshInstanceConfig(mesh, (uint)(index % 2), null)
                    {
                        mask = 1, enableTriangleCulling = false,
                        rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.DynamicGeometryManualUpdate,
                        subMeshFlags = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly
                    };
                    int handle = acceleration.AddInstance(config, Matrix4x4.Translate(Vector3.right * index * 3), null, (uint)index + 1);
                    acceleration.UpdateInstanceGeometry(handle);
                }

                void BuildAndWait()
                {
                    using var cmd = new CommandBuffer { name = "Native AS growth probe" };
                    cmd.BuildRayTracingAccelerationStructure(acceleration);
                    cmd.SetRenderTarget(target);
                    cmd.ClearRenderTarget(false, true, Color.white);
                    Graphics.ExecuteCommandBuffer(cmd);
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                        readback.Apply();
                        if (readback.GetPixel(0, 0).r != 1) throw new InvalidOperationException("GPU readback did not complete.");
                    }
                    finally { RenderTexture.active = previous; }
                    report.builds++;
                    Debug.Log("[HoRTNativeASProbe] completed build=" + report.builds);
                }
            }
            catch (Exception error) { report.error = error.ToString(); Debug.LogException(error); }
            finally
            {
                acceleration?.Dispose();
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                if (output != null) File.WriteAllText(output, JsonUtility.ToJson(report, true));
            }
            // A successful report only means the API calls completed; the runner
            // independently rejects any D3D12 debug-layer diagnostics.
            Debug.Log("[HoRTNativeASProbe] END pass=" + report.pass);
            EditorApplication.Exit(report.pass ? 0 : 1);
        }
    }
}
