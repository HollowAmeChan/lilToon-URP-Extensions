using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.Editor.CharacterShadow;

public static class HoCSOptimizationValidation
{
    public static void Run()
    {
        try
        {
            HoCharacterShadowValidation.Validate();
            HoCharacterShadowValidation.ValidateFrameReuse();
            HoCharacterShadowValidation.ValidateRendering();
            HoCharacterShadowValidation.ValidateReceiverSelectionRendering();
            HoCharacterShadowValidation.ValidateDistanceRendering();
            HoCharacterShadowValidation.ValidateSceneShadows();
            HoCharacterShadowValidation.ValidatePcss();
            HoCharacterShadowValidation.ValidateFiltering();
            ValidateImages();
            Debug.Log("HO_CS_OPTIMIZATION_VALIDATION_PASSED");
            EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }

    private static void ValidateImages()
    {
        ShaderUtil.allowAsyncCompilation=false;
        var material=new Material(Shader.Find("Hidden/Ho-CS/FilteringAB"));
        var atlas=new RenderTexture(4096,4096,0,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear);atlas.Create();
        var target=new RenderTexture(256,256,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
        var image=new Texture2D(256,256,TextureFormat.RGBAFloat,false,true);
        string output="D:/Unity_Fork/lilToon-URP-Extensions/Temp~/CharacterShadowOptimization-20261123";
        Directory.CreateDirectory(output);var lines=new List<string>();float maximum=0;int count=0;
        Shader.SetGlobalVector("_HoCSAtlasSize",new Vector4(1f/4096,1f/4096,4096,4096));
        Shader.SetGlobalVector("_HoCSPcssParams",new Vector4(1,Mathf.Tan(Mathf.Deg2Rad),.072f,.04f));
        Shader.SetGlobalVector("_HoCSPcssParams2",new Vector4(.0005f,32,64,.005f));
        HoCharacterShadowShaderContract.BindSamplingPattern(new Vector4(.0005f,32,64,.005f));
        try
        {
            foreach(bool fallback in new[]{false,true})
            foreach(int pattern in new[]{0,1,2,3,4,5,6,7,9})
            foreach(float radius in new[]{0f,.5f,1.6f,2f,2.25f,2.49f,2.5f,4f,16f})
            {
                if(fallback)material.EnableKeyword("HO_CS_FILTER_FORCE_FALLBACK");else material.DisableKeyword("HO_CS_FILTER_FORCE_FALLBACK");
                material.SetFloat("_Pattern",pattern);material.SetFloat("_Radius",radius);material.SetFloat("_PlaneSlope",pattern==5||pattern==6?200:10);
                material.SetFloat("_CropTexels",32);material.SetFloat("_ShiftTexels",.37f);
                material.SetFloat("_Mode",-1);Graphics.Blit(null,atlas,material);Shader.SetGlobalTexture("_HoCSAtlas",atlas);
                foreach(int mode in new[]{1,2})
                {
                    material.SetFloat("_Mode",mode);material.EnableKeyword("HO_CS_REFERENCE_IMPLEMENTATION");
                    Color[] baseline=Read();material.DisableKeyword("HO_CS_REFERENCE_IMPLEMENTATION");Color[] optimized=Read();
                    float error=0;for(int i=0;i<baseline.Length;i++)error=Mathf.Max(error,Mathf.Abs(baseline[i].r-optimized[i].r));
                    maximum=Mathf.Max(maximum,error);count++;
                    if(error>0.0004f)throw new Exception($"image changed: fallback={fallback}, pattern={pattern}, radius={radius}, mode={mode}, error={error}");
                    lines.Add($"fallback={fallback}, pattern={pattern}, radius={radius}, mode={mode}, maxError={error:R}");
                }
            }
            lines.Add($"PASS cases={count}, maximumError={maximum:R}");File.WriteAllLines(output+"/image-equivalence.txt",lines);
            Debug.Log($"HO_CS_IMAGE_EQUIVALENCE_PASSED cases={count} maximumError={maximum:R}");
        }
        finally
        {
            RenderTexture.active=null;
            atlas.Release();target.Release();UnityEngine.Object.DestroyImmediate(atlas);UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(material);
        }
        Color[] Read()
        {
            Graphics.Blit(null,target,material);RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();return image.GetPixels();
        }
    }
}
