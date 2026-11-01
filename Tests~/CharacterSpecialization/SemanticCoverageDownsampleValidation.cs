// Copy to an isolated project's Assets/Editor and run with graphics enabled:
// -batchmode -executeMethod SemanticCoverageDownsampleValidation.Run -logFile <path>
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class SemanticCoverageDownsampleValidation
{
    const int SourceWidth = 64;
    const int SourceHeight = 32;
    static readonly List<Object> Objects = new();
    static Material material;
    static ComputeBuffer lanes;
    static Texture2D[] selections;
    static RenderTexture low;
    static RenderTexture high;
    static int checks;

    public static void Run()
    {
        bool async = ShaderUtil.allowAsyncCompilation;
        int exit = 0;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            material = Keep(new Material(Shader.Find(
                "Hidden/lilToon-HoCharacterSpecialization/URP/ObjectSemantic")));
            lanes = new ComputeBuffer(8, 16, ComputeBufferType.Structured);
            uint[] laneData = new uint[8 * 4];
            for (uint i = 0; i < 8; i++) laneData[i * 4] = i + 1;
            lanes.SetData(laneData);
            Shader.SetGlobalBuffer("_HoACLanes", lanes);
            Shader.SetGlobalFloat("_HoACLaneCount", 8);
            Shader.SetGlobalVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
            CreateSelections();

            foreach (int scale in new[] { 1, 2, 4 })
            {
                RenderAndCheck(scale);
            }

            foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
            {
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,
                    message.message);
            }

            Debug.Log($"[SemanticCoverageDownsampleValidation] PASS checks={checks}, API={SystemInfo.graphicsDeviceType}");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            exit = 1;
        }
        finally
        {
            lanes?.Release();
            foreach (Object obj in Objects) Object.DestroyImmediate(obj);
            ShaderUtil.allowAsyncCompilation = async;
            EditorApplication.Exit(exit);
        }
    }

    static void CreateSelections()
    {
        selections = new Texture2D[4];
        for (int textureIndex = 0; textureIndex < selections.Length; textureIndex++)
        {
            selections[textureIndex] = Keep(new Texture2D(
                SourceWidth, SourceHeight, TextureFormat.RGBAFloat, false, true));
            selections[textureIndex].filterMode = FilterMode.Point;
            selections[textureIndex].wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[SourceWidth * SourceHeight];
            if (textureIndex == 1)
            {
                for (int y = 0; y < SourceHeight; y++)
                for (int x = 0; x < SourceWidth; x++)
                {
                    // Semantic ID 3 (the front-hair lane) occupies one of every four columns.
                    pixels[y * SourceWidth + x] = x % 4 == 0
                        ? new Color(3 / 255f, 1, 4 / 255f, 0)
                        : new Color(0, 0, 4 / 255f, 0);
                }
            }

            selections[textureIndex].SetPixels(pixels);
            selections[textureIndex].Apply();
            Shader.SetGlobalTexture($"_HoACSelection{textureIndex}Texture", selections[textureIndex]);
        }
    }

    static void RenderAndCheck(int scale)
    {
        int width = SourceWidth / scale;
        int height = SourceHeight / scale;
        low = Target(width, height);
        high = Target(width, height);

        var command = new CommandBuffer { name = "Semantic coverage downsample validation" };
        command.SetRenderTarget(
            new[] { new RenderTargetIdentifier(low), new RenderTargetIdentifier(high) },
            new RenderTargetIdentifier(BuiltinRenderTextureType.None));
        command.ClearRenderTarget(false, true, Color.clear);
        command.SetGlobalTexture("_BlitTexture", selections[0]);
        command.SetGlobalVector("_BlitTexture_TexelSize",
            new Vector4(1f / SourceWidth, 1f / SourceHeight, SourceWidth, SourceHeight));
        command.SetGlobalVector("_HoCharacterSemanticDownsampleParams",
            new Vector4(scale, 1f / SourceWidth, 1f / SourceHeight, 0));
        command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
        Graphics.ExecuteCommandBuffer(command);
        command.Release();

        var pixels = Read(low);
        int yCenter = height / 2;
        for (int x = 0; x < width; x++)
        {
            float expected = scale == 1 ? (x % 4 == 0 ? 1 : 0) :
                scale == 2 ? (x % 2 == 0 ? 0.5f : 0) : 0.25f;
            float actual = pixels[yCenter * width + x].b;
            Require(Mathf.Abs(actual - expected) < 0.002f,
                $"scale={scale}, x={x}: expected {expected}, got {actual}");
        }

        low.Release();
        high.Release();
        Object.DestroyImmediate(low);
        Object.DestroyImmediate(high);
    }

    static RenderTexture Target(int width, int height)
    {
        var target = Keep(new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear));
        target.filterMode = FilterMode.Point;
        target.Create();
        return target;
    }

    static Color[] Read(RenderTexture target)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        texture.Apply();
        Color[] pixels = texture.GetPixels();
        Object.DestroyImmediate(texture);
        RenderTexture.active = previous;
        return pixels;
    }

    static T Keep<T>(T obj) where T : Object
    {
        Objects.Add(obj);
        return obj;
    }

    static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
}
