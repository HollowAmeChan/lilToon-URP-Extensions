using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Atmosphere
{
    internal sealed class HoAtmosphereCache
    {
        internal RTHandle capturedCube;
        private readonly Camera[] cameras = new Camera[6];
        private RTHandle inputWrapper, blackWrapper;
        private Cubemap wrappedInput;
        private Material inspectedMaterial;
        private Shader inspectedShader;
        private string[] textureNames;
        private int completedSignature, completedFrame = -1;
        private uint epoch = 1, captureCount;
        private bool completed;

        internal uint CaptureCount => captureCount;
        internal void Refresh() { unchecked { epoch++; } completed = false; }
        internal static int Hash(int a, int b) { unchecked { return a * 397 ^ b; } }
        internal static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        internal static Vector4 RGB(Color color) => new Vector4(Mathf.Max(0, Finite(color.r, 0)), Mathf.Max(0, Finite(color.g, 0)), Mathf.Max(0, Finite(color.b, 0)), 0);

        internal readonly struct Input
        {
            internal readonly RTHandle texture;
            internal readonly Material material;
            internal readonly bool valid, capture;
            internal readonly int signature, resolution, frame;
            internal readonly uint content;
            internal Input(RTHandle texture, Material material, bool valid, bool capture, int signature, int resolution, int frame, uint content)
            {
                this.texture = texture; this.material = material; this.valid = valid; this.capture = capture;
                this.signature = signature; this.resolution = resolution; this.frame = frame; this.content = content;
            }
        }

        internal Input Prepare(HoAtmosphereSettings settings, Light sun)
        {
            if (blackWrapper == null || blackWrapper.externalTexture == null)
            {
                blackWrapper?.Release();
                blackWrapper = RTHandles.Alloc(CoreUtils.blackCubeTexture);
            }
            if (settings.source == HoAtmosphereSource.Constant)
                return new Input(blackWrapper, null, true, false, 0, 1, Time.frameCount, (uint)Hash((int)epoch, settings.constantColor.GetHashCode()));
            if (settings.source == HoAtmosphereSource.RadianceCubemap)
            {
                var cube = settings.radianceCubemap;
                if (cube == null) return Missing();
                if (wrappedInput != cube || inputWrapper == null)
                {
                    inputWrapper?.Release();
                    inputWrapper = RTHandles.Alloc(cube);
                    wrappedInput = cube;
                }
                int content = Hash(Hash(cube.GetInstanceID(), unchecked((int)cube.updateCount)), (int)epoch);
                return new Input(inputWrapper, null, true, false, 0, cube.width, Time.frameCount, unchecked((uint)content));
            }

            Material material = settings.source == HoAtmosphereSource.SceneSkybox ? RenderSettings.skybox : settings.skyboxMaterial;
            if (material == null || material.shader == null || !material.shader.isSupported || material.passCount == 0) return Missing();
            GraphicsFormat format = SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.Render)
                && SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.Sample)
                ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R32G32B32A32_SFloat;
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render) || !SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample)) return Missing();

            int resolution = Mathf.NextPowerOfTwo(Mathf.Clamp(settings.resolution, 16, Mathf.Min(1024, SystemInfo.maxTextureSize)));
            // RTHandle can outlive its Unity object when a scene unloads or the texture is destroyed.
            var captureTexture = capturedCube != null ? capturedCube.rt : null;
            if (captureTexture == null || captureTexture.width != resolution || captureTexture.graphicsFormat != format)
            {
                capturedCube?.Release();
                var desc = new RenderTextureDescriptor(resolution, resolution)
                {
                    graphicsFormat = format, depthStencilFormat = GraphicsFormat.None,
                    dimension = TextureDimension.Cube, volumeDepth = 1, msaaSamples = 1,
                    useMipMap = false, autoGenerateMips = false, sRGB = false
                };
                captureTexture = new RenderTexture(desc)
                {
                    name = "HoAtmosphere Raw Radiance", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                capturedCube = RTHandles.Alloc(captureTexture, transferOwnership: true);
                completed = false;
            }
            if (!captureTexture.IsCreated())
            {
                if (!captureTexture.Create()) return Missing();
                completed = false;
            }

            int signature = Hash(Hash(material.GetInstanceID(), material.shader.GetInstanceID()), Hash(resolution, (int)epoch));
            if (settings.update != HoAtmosphereUpdate.Manual)
            {
                signature = Hash(signature, material.ComputeCRC());
                if (inspectedMaterial != material || inspectedShader != material.shader)
                {
                    inspectedMaterial = material; inspectedShader = material.shader;
                    textureNames = material.GetTexturePropertyNames();
                }
                foreach (string name in textureNames)
                {
                    Texture texture = material.GetTexture(name);
                    if (texture != null) signature = Hash(signature, unchecked((int)texture.updateCount));
                }
                if (sun != null)
                {
                    signature = Hash(signature, sun.transform.forward.GetHashCode());
                    signature = Hash(signature, sun.color.GetHashCode());
                    signature = Hash(signature, sun.intensity.GetHashCode());
                    signature = Hash(signature, sun.colorTemperature.GetHashCode());
                    signature = Hash(signature, sun.useColorTemperature ? 1 : 0);
                }
            }
            bool capture = !completed || signature != completedSignature ||
                (settings.update == HoAtmosphereUpdate.EveryFrame && completedFrame != Time.frameCount);
            EnsureCameras(material);
            return new Input(capturedCube, material, true, capture, signature, resolution, Time.frameCount, capture ? captureCount + 1 : captureCount);
        }

        private Input Missing() => new Input(blackWrapper, null, false, false, 0, 1, Time.frameCount, 0);

        private void EnsureCameras(Material material)
        {
            for (int face = 0; face < 6; ++face)
            {
                if (cameras[face] == null)
                {
                    var go = new GameObject("HoAtmosphere Sky Capture " + (CubemapFace)face) { hideFlags = HideFlags.HideAndDontSave };
                    var camera = go.AddComponent<Camera>();
                    camera.enabled = false; camera.cullingMask = 0; camera.clearFlags = CameraClearFlags.Skybox;
                    camera.fieldOfView = 90; camera.aspect = 1; camera.nearClipPlane = 0.1f; camera.farClipPlane = 1000;
                    // Cube texel orientation differs from a regular camera RT on top-origin APIs.
                    if (SystemInfo.graphicsUVStartsAtTop)
                        camera.projectionMatrix = Matrix4x4.Scale(new Vector3(1, -1, 1)) * camera.projectionMatrix;
                    camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(CoreUtils.lookAtList[face], CoreUtils.upVectorList[face]));
                    go.AddComponent<Skybox>();
                    cameras[face] = camera;
                }
                cameras[face].GetComponent<Skybox>().material = material;
            }
        }

        // The explicit-matrix skybox overload enables Unity's XR path. Use six stable mono cameras instead.
        internal Camera FaceCamera(int face) => cameras[face];

        internal void Complete(Input input)
        {
            completedSignature = input.signature; completedFrame = input.frame;
            completed = true; captureCount++;
        }

        internal void Dispose()
        {
            capturedCube?.Release(); capturedCube = null;
            inputWrapper?.Release(); inputWrapper = null; wrappedInput = null;
            blackWrapper?.Release(); blackWrapper = null;
            for (int face = 0; face < 6; ++face)
            {
                if (cameras[face] == null) continue;
                var go = cameras[face].gameObject;
                #if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += () => { if (go != null) Object.DestroyImmediate(go); };
                else Object.Destroy(go);
                #else
                Object.Destroy(go);
                #endif
                cameras[face] = null;
            }
            completed = false;
        }
    }
}
