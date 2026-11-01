#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Hollow.HoUnityTools.MmdMotion;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, file-queued asset migration; no Inspector or menu entry.</summary>
[InitializeOnLoad]
public static class HoAnimationWorkspaceMigration
{
    [Serializable] public sealed class Move { public string from, to, guid; }
    [Serializable] public sealed class Film { public string name, scene, config; public float audioOffset; }
    [Serializable] public sealed class Plan
    {
        public string commonFolder, stagePrefab, rolePrefab, restoreScene;
        public Move[] folders, files, archives;
        public string[] duplicateFolders;
        public Film[] films;
    }
    [Serializable] public sealed class Request { public string id, action, plan; }
    [Serializable] public sealed class Response { public string id, status, result; }
    [Serializable] public sealed class Inventory
    {
        public string[] roleDependencies, filmDependencies;
        public int stageAssets;
        public string activeScene, prefabStage;
    }
    [Serializable] public sealed class FilmReport
    {
        public string name, scene, stagePrefab, profile, headPath, character;
        public float audioOffset;
        public int playerCount, stageInstances;
    }
    [Serializable] public sealed class FinalReport { public FilmReport[] films; public string[] blockedDeletion; }
    static string Work => Path.GetFullPath("Library/HoAnimationWorkspace");
    static HoAnimationWorkspaceMigration() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists("Library/HoMmdStages/running.json") || File.Exists("Library/HoMmdStages/request.json") ||
            File.Exists("Library/HoMmdVideo/request.json")) return;
        string path = Path.Combine(Work, "request.json");
        if (!File.Exists(path)) return;
        Request request = null;
        bool reloadLocked = false;
        try
        {
            request = JsonUtility.FromJson<Request>(File.ReadAllText(path));
            File.Delete(path);
            if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save open scene edits before migrating assets.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab Mode before migrating assets.");
            Plan plan = JsonUtility.FromJson<Plan>(File.ReadAllText(request.plan));
            if (request.action == "relocate")
            {
                EditorApplication.LockReloadAssemblies();
                reloadLocked = true;
                AssetDatabase.ReleaseCachedFileHandles();
            }
            string result;
            switch (request.action)
            {
                case "inventory": result = InventoryOf(plan); break;
                case "state": result = StateOf(plan); break;
                case "relocate": result = Relocate(plan); break;
                case "consolidate": result = Consolidate(plan); break;
                case "cleanup": result = Cleanup(plan); break;
                case "verify": result = Verify(plan); break;
                default: throw new ArgumentException("Unknown migration action: " + request.action);
            }
            Write(request, "ok", result);
        }
        catch (Exception error) { Write(request, "error", error.ToString()); Debug.LogException(error); }
        finally { if (reloadLocked) EditorApplication.UnlockReloadAssemblies(); }
    }

    static void Write(Request request, string status, string result)
    {
        Directory.CreateDirectory(Work);
        File.WriteAllText(Path.Combine(Work, "result.json"),
            JsonUtility.ToJson(new Response { id = request?.id, status = status, result = result }, true));
    }

    static string InventoryOf(Plan plan)
    {
        var data = new Inventory
        {
            roleDependencies = AssetDatabase.GetDependencies("Assets/00\u52A8\u753B/\u89D2\u8272/WTD/WTD-BUILD.prefab", true),
            filmDependencies = AssetDatabase.GetDependencies(new[] {
                "Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/EX-HAPPY.unity",
                "Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/NEMONEMO.unity",
                "Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/SWEET-STEP.unity",
                "Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/しょ食事タイム.unity",
                "Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/頑張りたい人へ.unity" }, true),
            stageAssets = AssetDatabase.FindAssets("", new[] { "Assets/00动画/MMD场景" }).Length,
            activeScene = SceneManager.GetActiveScene().path,
            prefabStage = PrefabStageUtility.GetCurrentPrefabStage()?.assetPath
        };
        string output = Path.Combine(Work, "inventory.json");
        File.WriteAllText(output, JsonUtility.ToJson(data, true));
        return output;
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Split('/').Contains(".."))
            throw new ArgumentException("Invalid Assets destination: " + path);
        Folder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
            throw new IOException("Cannot create asset folder: " + path);
    }

    static string StateOf(Plan plan)
    {
        string project = Path.GetDirectoryName(Application.dataPath);
        return "migration=paired-meta-empty-locks\ncwd=" + Directory.GetCurrentDirectory() + "\nproject=" + project + "\n" +
            string.Join("\n", plan.folders.Select(move =>
                move.from + " -> " + move.to + "; source=" + AssetDatabase.AssetPathToGUID(move.from) +
                "; target=" + AssetDatabase.AssetPathToGUID(move.to) +
                "; parentDB=" + AssetDatabase.IsValidFolder(Path.GetDirectoryName(move.to).Replace('\\', '/')) +
                "; parentDisk=" + Directory.Exists(Path.Combine(project, Path.GetDirectoryName(move.to))) +
                "; validation=" + AssetDatabase.ValidateMoveAsset(move.from, move.to) +
                "; asciiValidation=" + AssetDatabase.ValidateMoveAsset(move.from, "Assets/__HoAnimationMove")));
    }

    static void MoveAsset(Move move)
    {
        bool destinationExists = File.Exists(move.to) || AssetDatabase.IsValidFolder(move.to);
        if (destinationExists && !string.IsNullOrEmpty(move.guid) &&
            AssetDatabase.AssetPathToGUID(move.to) == move.guid) return;
        if (!File.Exists(move.from) && !AssetDatabase.IsValidFolder(move.from))
        {
            if (File.Exists(move.to) || AssetDatabase.IsValidFolder(move.to)) return;
            throw new FileNotFoundException("Migration source missing", move.from);
        }
        if (!string.IsNullOrEmpty(move.guid) && AssetDatabase.AssetPathToGUID(move.from) != move.guid)
            throw new IOException("Source GUID differs from the migration plan: " + move.from);
        if (destinationExists)
            throw new IOException("Migration destination already exists: " + move.to);
        string parent = Path.GetDirectoryName(move.to).Replace('\\', '/');
        Folder(parent);
        string absoluteParent = Path.Combine(Path.GetDirectoryName(Application.dataPath), parent);
        if (!Directory.Exists(absoluteParent))
            throw new DirectoryNotFoundException("Destination exists in the AssetDatabase but not on disk: " + absoluteParent);
        File.WriteAllText(Path.Combine(Work, "progress.json"), JsonUtility.ToJson(move, true));
        // This migration uses paired disk renames because Unity 6000.3's native
        // MoveAsset can enter an interactive "Moving file failed" retry dialog
        // for these imported folders. Suspend imports until the original meta
        // has followed its asset, then refresh/check GUIDs at the batch boundary.
        string source = Path.GetFullPath(move.from);
        string destination = Path.GetFullPath(move.to);
        string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
        if (!source.StartsWith(assets, StringComparison.OrdinalIgnoreCase) ||
            !destination.StartsWith(assets, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(source + ".meta") || File.Exists(destination + ".meta"))
            throw new IOException("Unsafe asset/meta move: " + move.from);
        bool directory = Directory.Exists(source);
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            File.Move(source + ".meta", destination + ".meta");
            try
            {
                if (directory)
                {
                    try { Directory.Move(source, destination); }
                    catch (IOException)
                    {
                        if (!Directory.Exists(source) || Directory.Exists(destination)) throw;
                        MoveContentsAroundEmptyFolderLocks(source, destination);
                    }
                }
                else File.Move(source, destination);
            }
            catch
            {
                File.Move(destination + ".meta", source + ".meta");
                throw;
            }
        }
        finally { AssetDatabase.AllowAutoRefresh(); }
    }

    static void VerifyMove(Move move)
    {
        if (!string.IsNullOrEmpty(move.guid) && AssetDatabase.AssetPathToGUID(move.to) != move.guid)
            throw new IOException("Moved asset GUID differs: " + move.to);
    }

    static void MoveContentsAroundEmptyFolderLocks(string source, string destination)
    {
        // Explorer can hold an empty Renders directory open after all videos
        // were moved. Move real assets normally, recreate only empty locked
        // folders, and leave their empty shells for a later cleanup. A locked
        // non-empty folder/file is an error; roll back the partial move.
        var rollback = new List<Action>();
        var created = new List<string>();
        Directory.CreateDirectory(destination);
        created.Add(destination);
        try
        {
            foreach (string old in Directory.GetDirectories(source))
            {
                string target = Path.Combine(destination, Path.GetFileName(old));
                try
                {
                    Directory.Move(old, target);
                    rollback.Add(() => Directory.Move(target, old));
                }
                catch (IOException)
                {
                    if (!Directory.Exists(old) || Directory.Exists(target) ||
                        Directory.EnumerateFileSystemEntries(old).Any()) throw;
                    Directory.CreateDirectory(target);
                    created.Add(target);
                }
            }
            foreach (string old in Directory.GetFiles(source))
            {
                string target = Path.Combine(destination, Path.GetFileName(old));
                File.Move(old, target);
                rollback.Add(() => File.Move(target, old));
            }
        }
        catch
        {
            for (int i = rollback.Count - 1; i >= 0; i--) rollback[i]();
            for (int i = created.Count - 1; i >= 0; i--) Directory.Delete(created[i]);
            throw;
        }
    }

    static string Relocate(Plan plan)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Folder creation must be imported before batched file moves can
        // address it. Root moves also establish parents used by later steps.
        foreach (Move move in plan.folders)
        {
            Folder(Path.GetDirectoryName(move.to).Replace('\\', '/'));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            MoveAsset(move);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            VerifyMove(move);
        }
        foreach (string parent in plan.files.Concat(plan.archives)
                     .Select(move => Path.GetDirectoryName(move.to).Replace('\\', '/')).Distinct())
            Folder(parent);
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (Move move in plan.files) MoveAsset(move);
            foreach (Move move in plan.archives) MoveAsset(move);
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (Move move in plan.files.Concat(plan.archives)) VerifyMove(move);
        const string referencePreference = "HoVmdSequence.ReferenceRig";
        string reference = EditorPrefs.GetString(referencePreference, "");
        foreach (Move move in plan.folders.Concat(plan.files).Concat(plan.archives))
            if (reference == move.from || reference.StartsWith(move.from + "/", StringComparison.Ordinal))
                reference = move.to + reference.Substring(move.from.Length);
        if (!string.IsNullOrEmpty(reference)) EditorPrefs.SetString(referencePreference, reference);
        AssetDatabase.SaveAssets();
        return "GUID-preserving asset moves completed.";
    }

    static Dictionary<Object, Object> SharedAssets(Plan plan)
    {
        var map = new Dictionary<Object, Object>();
        foreach (string folder in plan.duplicateFolders)
            foreach (string section in new[] { "Rendering", "Materials" })
            {
                string source = folder + "/" + section;
                if (!AssetDatabase.IsValidFolder(source)) continue;
                foreach (string old in AssetDatabase.FindAssets("", new[] { source })
                             .Select(AssetDatabase.GUIDToAssetPath).Where(x => !AssetDatabase.IsValidFolder(x)))
                {
                    string target = plan.commonFolder + "/" + section + old.Substring(source.Length);
                    if (!File.Exists(target)) throw new FileNotFoundException("Canonical asset missing", target);
                    Object[] targets = AssetDatabase.LoadAllAssetsAtPath(target);
                    foreach (Object value in AssetDatabase.LoadAllAssetsAtPath(old))
                    {
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string ignored, out long id);
                        Object match = targets.FirstOrDefault(x => x != null && x.GetType() == value.GetType() &&
                            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(x, out string guid, out long localId) && id == localId);
                        if (match == null) throw new InvalidOperationException("Subasset cannot be matched: " + old);
                        map[value] = match;
                    }
                }
            }
        return map;
    }

    static void Remap(Object value, Dictionary<Object, Object> map)
    {
        var serialized = new SerializedObject(value);
        var property = serialized.GetIterator();
        bool changed = false;
        while (property.Next(true))
            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue != null && map.TryGetValue(property.objectReferenceValue, out Object replacement))
            { property.objectReferenceValue = replacement; changed = true; }
        if (!changed) return;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(value);
        if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
    }

    static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    static string HeadPath(HoVmdSequencePlayer player)
    {
        var names = new Stack<string>();
        for (Transform bone = player.TargetAnimator.GetBoneTransform(HumanBodyBones.Head); bone != null; bone = bone.parent)
            names.Push(bone.name);
        return "/" + string.Join("/", names);
    }

    static void ApplyEnvironment(Scene scene)
    {
        foreach (MonoBehaviour behaviour in All<MonoBehaviour>(scene).Where(x => x != null && x.GetType().Name == "HoMmdStageEnvironment"))
            behaviour.SendMessage("ApplyNow", SendMessageOptions.DontRequireReceiver);
    }

    static string Consolidate(Plan plan)
    {
        if (File.Exists(plan.stagePrefab))
            throw new InvalidOperationException("Shared stage already exists; verify it instead of rebuilding.");
        Dictionary<Object, Object> map = SharedAssets(plan);
        string focusPath = null;
        GameObject sharedStage = null;
        for (int index = 0; index < plan.films.Length; index++)
        {
            Film film = plan.films[index];
            Scene scene = EditorSceneManager.OpenScene(film.scene, OpenSceneMode.Single);
            HoVmdSequencePlayer player = All<HoVmdSequencePlayer>(scene).Single();
            GameObject performance = player.transform.root.gameObject;
            performance.name = "20 演出";
            player.SoundtrackTimeOffset = film.audioOffset;
            foreach (Component component in All<Component>(scene).Where(x => x != null)) Remap(component, map);
            if (RenderSettings.skybox != null && map.TryGetValue(RenderSettings.skybox, out Object sky))
                RenderSettings.skybox = (Material)sky;
            string head = HeadPath(player);
            if (focusPath == null) focusPath = head;
            if (head != focusPath) throw new InvalidOperationException("Shared head-focus path differs: " + film.name);
            GameObject[] environment = scene.GetRootGameObjects().Where(root => root != performance).ToArray();
            if (index == 0)
            {
                var root = new GameObject("10 花吐电気 · 通用舞台");
                SceneManager.MoveGameObjectToScene(root, scene);
                foreach (GameObject child in environment) child.transform.SetParent(root.transform, true);
                Folder(Path.GetDirectoryName(plan.stagePrefab).Replace('\\', '/'));
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, plan.stagePrefab, InteractionMode.AutomatedAction);
                sharedStage = AssetDatabase.LoadAssetAtPath<GameObject>(plan.stagePrefab);
                if (sharedStage == null) throw new IOException("Cannot save shared stage prefab.");
            }
            else
            {
                foreach (GameObject root in environment) Object.DestroyImmediate(root);
                if (PrefabUtility.InstantiatePrefab(sharedStage, scene) == null)
                    throw new IOException("Cannot instantiate shared stage.");
            }
            ApplyEnvironment(scene);
            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save " + film.scene);
        }
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(plan.commonFolder + "/Rendering/后处理.asset");
        foreach (ScreenProcessStackVolume screen in profile.components.OfType<ScreenProcessStackVolume>())
        {
            foreach (var layer in screen.layers.value.Where(x => x.effect == ScreenProcessEffect.DepthOfField))
            { layer.depthOfFieldFocusTarget = null; layer.depthOfFieldFocusTargetPath = focusPath; }
            EditorUtility.SetDirty(screen);
        }
        AssetDatabase.SaveAssets();
        return Verify(plan);
    }

    static string Cleanup(Plan plan)
    {
        var retire = new HashSet<string>(plan.duplicateFolders.Where(AssetDatabase.IsValidFolder));
        Func<string, bool> retiring = path => retire.Any(folder => path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal));
        string[] activeDependencies = AssetDatabase.GetDependencies(plan.films.Select(f => f.scene).Append(plan.stagePrefab).ToArray(), true);
        string[] activeBlocked = activeDependencies.Where(retiring).ToArray();
        if (activeBlocked.Length > 0) throw new InvalidOperationException("Production references duplicates: " + string.Join("\n", activeBlocked));
        var types = new HashSet<string> { ".unity", ".prefab", ".asset", ".mat", ".controller", ".overrideController", ".anim", ".playable" };
        string[] outside = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
            !retiring(path) && types.Contains(Path.GetExtension(path).ToLowerInvariant())).ToArray();
        string[] blocked = AssetDatabase.GetDependencies(outside, true).Where(retiring).Distinct().ToArray();
        if (blocked.Length > 0) throw new InvalidOperationException("Other project assets still reference duplicates:\n" + string.Join("\n", blocked));
        foreach (string folder in retire)
        {
            string backup = Path.GetFullPath("Temp~/00动画整理备份/" + Path.GetFileName(folder));
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!Directory.Exists(backup)) FileUtil.CopyFileOrDirectory(folder, backup);
            if (File.Exists(folder + ".meta") && !File.Exists(backup + ".meta")) File.Copy(folder + ".meta", backup + ".meta");
            if (!AssetDatabase.DeleteAsset(folder)) throw new IOException("Cannot retire " + folder);
        }
        var lockedEmpty = new List<string>();
        foreach (Move move in plan.folders.Concat(plan.archives))
        {
            if (!Directory.Exists(move.from) || AssetDatabase.AssetPathToGUID(move.to) != move.guid) continue;
            string source = Path.GetFullPath(move.from);
            if (!source.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
                throw new IOException("Residual cleanup is outside Assets: " + source);
            string[] files = Directory.GetFiles(move.from, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).ToArray();
            // The first native folder move left some identical, unreferenced
            // binary copies behind. Never remove a unique or changed file.
            foreach (string path in files)
            {
                string target = move.to + path.Replace('\\', '/').Substring(move.from.Length);
                if (!File.Exists(target) || new FileInfo(path).Length != new FileInfo(target).Length ||
                    !SameFile(path, target))
                    throw new IOException("Residual source has unique content; preserve it: " + path);
            }
            if (files.Length > 0)
            {
                string prefix = move.from + "/";
                string[] references = AssetDatabase.GetDependencies(outside.Where(path =>
                    !path.StartsWith(prefix, StringComparison.Ordinal)).ToArray(), true)
                    .Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                if (references.Length > 0)
                    throw new IOException("Residual source is still referenced: " + string.Join("\n", references));
            }
            try
            {
                Directory.Delete(source, true);
                if (File.Exists(move.from + ".meta")) File.Delete(move.from + ".meta");
            }
            catch (IOException) { lockedEmpty.Add(move.from); }
            catch (UnauthorizedAccessException) { lockedEmpty.Add(move.from); }
        }
        File.WriteAllLines(Path.Combine(Work, "locked-empty-folders.txt"), lockedEmpty);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        const string oldRoot = "Assets/Hollow/MMD影片";
        if (AssetDatabase.IsValidFolder(oldRoot) && !AssetDatabase.FindAssets("", new[] { oldRoot })
                .Select(AssetDatabase.GUIDToAssetPath).Any(path => !AssetDatabase.IsValidFolder(path)) &&
            lockedEmpty.Count == 0)
        {
            Directory.Delete(oldRoot, true);
            if (File.Exists(oldRoot + ".meta")) File.Delete(oldRoot + ".meta");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        AssetDatabase.SaveAssets();
        return Verify(plan);
    }

    static bool SameFile(string first, string second)
    {
        using (var sha = SHA256.Create())
        using (var a = File.OpenRead(first))
        using (var b = File.OpenRead(second))
            return sha.ComputeHash(a).SequenceEqual(sha.ComputeHash(b));
    }

    static string Verify(Plan plan)
    {
        var rows = new List<FilmReport>();
        foreach (Film film in plan.films)
        {
            Scene scene = EditorSceneManager.OpenScene(film.scene, OpenSceneMode.Single);
            HoVmdSequencePlayer player = All<HoVmdSequencePlayer>(scene).Single();
            GameObject[] stage = scene.GetRootGameObjects().Where(root =>
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == plan.stagePrefab).ToArray();
            string[] profiles = All<Volume>(scene).Where(v => v.sharedProfile != null)
                .Select(v => AssetDatabase.GetAssetPath(v.sharedProfile)).Distinct().ToArray();
            if (stage.Length != 1 || profiles.Any(path => !path.StartsWith(plan.commonFolder + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Shared-stage/profile binding invalid: " + film.name);
            if (Mathf.Abs(player.SoundtrackTimeOffset - film.audioOffset) > .00001f)
                throw new InvalidOperationException("Audio offset not persisted: " + film.name);
            string[] duplicateDependencies = AssetDatabase.GetDependencies(film.scene, true)
                .Where(path => plan.duplicateFolders.Any(folder => path.StartsWith(folder + "/", StringComparison.Ordinal))).ToArray();
            if (duplicateDependencies.Length > 0) throw new InvalidOperationException("Duplicate reference remains: " + film.name);
            GameObject character = PrefabUtility.GetOutermostPrefabInstanceRoot(player.TargetAnimator.gameObject);
            string role = character != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(character) : "";
            if (role != plan.rolePrefab) throw new InvalidOperationException("Character prefab differs: " + role);
            rows.Add(new FilmReport
            {
                name = film.name, scene = film.scene, stagePrefab = plan.stagePrefab,
                profile = string.Join(";", profiles), headPath = HeadPath(player), character = role,
                audioOffset = player.SoundtrackTimeOffset, playerCount = All<HoVmdSequencePlayer>(scene).Count(),
                stageInstances = stage.Length
            });
        }
        EditorSceneManager.OpenScene(plan.restoreScene, OpenSceneMode.Single);
        string output = Path.Combine(Work, "verification.json");
        File.WriteAllText(output, JsonUtility.ToJson(new FinalReport { films = rows.ToArray(), blockedDeletion = Array.Empty<string>() }, true));
        return output;
    }
}
#endif
