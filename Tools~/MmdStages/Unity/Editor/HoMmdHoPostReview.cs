using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Explicit, folder-scoped review of existing stage post-processing assets.</summary>
public static class HoMmdHoPostReview
{
    [Serializable] public sealed class Report
    {
        public string schema = "ho-post-review-1", createdUtc, action, folder;
        public ProfileRow[] profiles;
        public int profilesChanged, removedNativeComponents;
    }
    [Serializable] public sealed class ProfileRow
    {
        public string path, guid, backup, hoBefore, hoAfter;
        public bool changed, toneAdded, hoParametersPreserved, hoOnly, hoOnlyOverride, imageEnabled, imageActive, scenePreview;
        public int missingComponents;
        public string[] removed;
        public LayerRow[] image, screen;
        public NativeRow[] native;
    }
    [Serializable] public sealed class LayerRow
    {
        public string name, effect;
        public bool enabled;
        public float intensity;
        public Vector4 p0, p1, p2, p3;
        public Color color;
    }
    [Serializable] public sealed class NativeRow
    {
        public string type, serialized;
        public bool active, effective;
    }
    [Serializable] public sealed class TuneFile { public LayerPatch[] changes; }
    [Serializable] public sealed class LayerPatch
    {
        public string profile, stack, effect;
        public bool setIntensity, setParameters0, setParameters1;
        public float intensity;
        public Vector4 parameters0, parameters1;
    }
    [Serializable] public sealed class TuneResult { public string profile, stack, effect, before, after; }
    [Serializable] public sealed class TuneReport { public TuneResult[] changes; }

    public static string Execute(HoMmdStageWork.Request request)
    {
        string folder = (request.stageFolder ?? "").Replace('\\', '/').TrimEnd('/');
        if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Split('/').Contains("..") ||
            !AssetDatabase.IsValidFolder(folder))
            throw new ArgumentException("Provide an explicit valid Assets folder.");
        if (request.action == "post-tune") return Tune(request, folder);
        bool migrate = request.action == "post-migrate";
        bool tone = request.action == "post-tonemap";
        if (!migrate && !tone && request.action != "post-inventory") throw new ArgumentException(request.action);
        string output = string.IsNullOrEmpty(request.outputPath) ?
            Path.GetFullPath("Library/HoMmdPostReview/" + request.action + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json") :
            Path.GetFullPath(request.outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var profiles = AssetDatabase.FindAssets("t:VolumeProfile", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var rows = new List<ProfileRow>();
        foreach (string path in profiles)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) continue;
            var row = Read(profile, path);
            var native = profile.components.Where(IsNativeImageEffect).ToArray();
            row.hoBefore = HoFingerprint(profile);
            row.removed = new string[0];
            bool hasImage = profile.TryGet(out ImageProcessStackVolume image);
            bool needsFlag = !hasImage || !image.ReplaceBuiltinPostProcessing.value ||
                !image.ReplaceBuiltinPostProcessing.overrideState;
            if (tone && hasImage && image.active && image.Enable.value &&
                !image.layers.value.Any(l=>l!=null&&l.effect==ImageProcessEffect.ToonMap))
            {
                row.backup = Path.GetFullPath("Library/HoMmdPostReview/Backup/" + row.guid + ".asset");
                Directory.CreateDirectory(Path.GetDirectoryName(row.backup));
                if (!File.Exists(row.backup)) File.Copy(path, row.backup);
                Undo.RegisterCompleteObjectUndo(image, "添加 Ho 色调映射");
                int overlay=image.layers.value.FindIndex(l=>l!=null&&
                    (l.effect==ImageProcessEffect.CinematicBars||l.effect==ImageProcessEffect.LogoOverlay));
                if(overlay<0)overlay=image.layers.value.Count;
                image.layers.value.Insert(overlay,new ImageProcessLayer {
                    name="Ho 色调映射 · ACES",effect=ImageProcessEffect.ToonMap,
                    intensity=1,parameters0=new Vector4(2,0,0,0),blendMode=ImageProcessBlendMode.Normal
                });
                image.layers.overrideState=true;
                EditorUtility.SetDirty(image);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                row.changed=row.toneAdded=true;
            }
            if (migrate && (needsFlag || native.Length > 0))
            {
                row.backup = Path.GetFullPath("Library/HoMmdPostReview/Backup/" + row.guid + ".asset");
                Directory.CreateDirectory(Path.GetDirectoryName(row.backup));
                if (!File.Exists(row.backup)) File.Copy(path, row.backup);
                Undo.RegisterCompleteObjectUndo(profile, "Ho 场景后处理清理");
                if (!hasImage)
                {
                    image = profile.Add<ImageProcessStackVolume>(true);
                    AssetDatabase.AddObjectToAsset(image, profile);
                    image.Enable.Override(true);
                }
                image.ReplaceBuiltinPostProcessing.Override(true);
                row.removed = native.Select(c => c.GetType().Name).ToArray();
                ReleaseEditors(native);
                foreach (var component in native)
                {
                    profile.components.Remove(component);
                    Undo.DestroyObjectImmediate(component);
                }
                profile.isDirty = true;
                EditorUtility.SetDirty(image);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                row.changed = true;
            }
            row.hoAfter = HoFingerprint(profile);
            row.hoParametersPreserved = row.hoBefore == row.hoAfter;
            if (hasImage && !tone && !row.hoParametersPreserved)
                throw new InvalidOperationException("Existing Ho artistic parameters changed: " + path);
            if (migrate && (profile.components.Any(IsNativeImageEffect) ||
                image == null || !image.ReplaceBuiltinPostProcessing.value || !image.ReplaceBuiltinPostProcessing.overrideState))
                throw new InvalidOperationException("Ho-only migration incomplete: " + path);
            if (image != null)
            {
                row.hoOnly = image.ReplaceBuiltinPostProcessing.value;
                row.hoOnlyOverride = image.ReplaceBuiltinPostProcessing.overrideState;
            }
            rows.Add(row);
        }
        if (migrate) VolumeManager.instance.ResetMainStack();
        File.WriteAllText(output, JsonUtility.ToJson(new Report {
            createdUtc=DateTime.UtcNow.ToString("O"), action=request.action, folder=folder,
            profiles=rows.ToArray(), profilesChanged=rows.Count(r=>r.changed),
            removedNativeComponents=rows.Sum(r=>r.removed.Length)
        }, true));
        return output;
    }

    static string Tune(HoMmdStageWork.Request request, string folder)
    {
        if (string.IsNullOrEmpty(request.settingsPath) || !File.Exists(request.settingsPath))
            throw new ArgumentException("Provide --settings with explicit profile/layer patches.");
        var config=JsonUtility.FromJson<TuneFile>(File.ReadAllText(request.settingsPath));
        var results=new List<TuneResult>();
        foreach(var patch in config.changes)
        {
            if(!patch.profile.StartsWith(folder+"/",StringComparison.Ordinal)||patch.profile.Split('/').Contains(".."))
                throw new InvalidOperationException("Profile patch is outside the selected scope: "+patch.profile);
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(patch.profile);
            if(profile==null)throw new InvalidOperationException("Profile missing: "+patch.profile);
            var result=new TuneResult{profile=patch.profile,stack=patch.stack,effect=patch.effect};
            if(patch.stack=="screen")
            {
                if(!profile.TryGet(out ScreenProcessStackVolume stack))throw new InvalidOperationException("No ScreenProcess stack.");
                var layer=stack.layers.value.Single(l=>l!=null&&l.effect.ToString()==patch.effect);
                result.before=JsonUtility.ToJson(layer);
                Undo.RegisterCompleteObjectUndo(stack,"调整 Ho 场景景深");
                if(patch.setIntensity)layer.intensity=patch.intensity;
                if(patch.setParameters0)layer.parameters0=patch.parameters0;
                if(patch.setParameters1)layer.parameters1=patch.parameters1;
                result.after=JsonUtility.ToJson(layer);
                EditorUtility.SetDirty(stack);
            }
            else throw new ArgumentException("Unsupported patch stack: "+patch.stack);
            profile.isDirty=true;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            results.Add(result);
        }
        VolumeManager.instance.ResetMainStack();
        string output=Path.GetFullPath(request.outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output,JsonUtility.ToJson(new TuneReport{changes=results.ToArray()},true));
        return output;
    }

    static bool IsNativeImageEffect(VolumeComponent c) => c != null &&
        c.GetType().Namespace == "UnityEngine.Rendering.Universal" && c is IPostProcessComponent;

    static ProfileRow Read(VolumeProfile p, string path)
    {
        p.TryGet(out ImageProcessStackVolume image);
        p.TryGet(out ScreenProcessStackVolume screen);
        return new ProfileRow {
            path=path, guid=AssetDatabase.AssetPathToGUID(path),
            missingComponents=p.components.Count(c=>c==null),
            hoOnly=image!=null && image.ReplaceBuiltinPostProcessing.value,
            hoOnlyOverride=image!=null && image.ReplaceBuiltinPostProcessing.overrideState,
            imageEnabled=image!=null && image.Enable.value, imageActive=image!=null && image.active,
            scenePreview=image!=null && image.ShowInSceneView.value,
            image=image==null?new LayerRow[0]:image.layers.value.Where(x=>x!=null).Select(x=>new LayerRow {
                name=x.name,effect=x.effect.ToString(),enabled=x.enabled,intensity=x.intensity,
                p0=x.parameters0,p1=x.parameters1,p2=x.parameters2,p3=x.parameters3,color=x.color
            }).ToArray(),
            screen=screen==null?new LayerRow[0]:screen.layers.value.Where(x=>x!=null).Select(x=>new LayerRow {
                name=x.name,effect=x.effect.ToString(),enabled=x.enabled,intensity=x.intensity,
                p0=x.parameters0,p1=x.parameters1,p2=x.parameters2,p3=x.parameters3,color=x.color
            }).ToArray(),
            native=p.components.Where(IsNativeImageEffect).Select(x=>new NativeRow {
                type=x.GetType().Name,active=x.active,effective=x.active&&((IPostProcessComponent)x).IsActive(),
                serialized=EditorJsonUtility.ToJson(x)
            }).ToArray()
        };
    }
    static string HoFingerprint(VolumeProfile profile)
    {
        // Ownership is the only Ho parameter this migration changes.
        var values = profile.components.Where(c=>c!=null &&
            (c.GetType().Namespace??"").StartsWith("lilToon.URP.Extensions",StringComparison.Ordinal))
            .Select(c=> {
                string json=EditorJsonUtility.ToJson(c);
                if(c is ImageProcessStackVolume)
                    json=System.Text.RegularExpressions.Regex.Replace(json,
                        "\"ReplaceBuiltinPostProcessing\":\\{[^}]*\\},?", "");
                return c.GetType().FullName+":"+json;
            }).ToArray();
        using(var hash=System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",values)))).Replace("-","");
    }
    static void ReleaseEditors(IEnumerable<VolumeComponent> components)
    {
        var targets=new HashSet<Object>(components);
        if(targets.Count==0)return;
        foreach(var editor in Resources.FindObjectsOfTypeAll<UnityEditor.Editor>())
            if(editor!=null && editor.targets!=null && editor.targets.Any(t=>t!=null&&targets.Contains(t)))
                Object.DestroyImmediate(editor);
    }
}
