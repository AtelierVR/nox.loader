#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Logger = Nox.CCK.Utils.Logger;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using UnityEditor;

namespace Nox.ModLoader.Cores.Assets {

    public class EditorKernelAssetAPI : IAssetAPI {
        public EditorKernelAssetAPI(ModLoader.Mods.KernelMod kernelMod)
            => _kernelMod = kernelMod;

        private readonly ModLoader.Mods.KernelMod _kernelMod;
        private bool _loaded;

        public static string ToRelative(string path) {
            path = Path.GetFullPath(path);

            // Absolute path
            var folders = new[] { (Application.dataPath, "Assets"), (Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages")), "Packages"), (Application.streamingAssetsPath, "StreamingAssets"), (Application.persistentDataPath, "PersistentDataPath") };

            foreach (var folder in folders) {
                if (!path.StartsWith(folder.Item1, StringComparison.OrdinalIgnoreCase)) continue;
                var relativePath = path[folder.Item1.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.Combine(folder.Item2, relativePath);
            }

            // Not found
            Logger.LogWarning($"Path '{path}' is not in Assets, Packages, StreamingAssets or PersistentDataPath.");
            return path;
        }

        public static string FormatPath(string path)
            => AssetAPIExtension.FormatPath(path);


        public bool HasAsset<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            if (HasInternalAsset<T>(path)) return true;

            // get on other mods
            if (AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
                                 .Any(m => m.AssetAPI.HasInternalAsset<T>(path))) return true;

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null && mod.AssetAPI.HasInternalAsset<T>(path);
        }

        public T GetAsset<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            // get on Internal mod
            if (HasInternalAsset<T>(path)) return GetInternalAsset<T>(path);

            // get on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
                                               .Where(m => m.AssetAPI.HasInternalAsset<T>(path))) return m.AssetAPI.GetInternalAsset<T>(path);

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod?.AssetAPI.GetInternalAsset<T>(path);
        }

        public bool HasInternalAsset<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
                if (File.Exists(dirpath)) return true;
            }

            return false;
        }

        public T GetInternalAsset<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
                var asset = AssetDatabase.LoadAssetAtPath<T>(dirpath);
                if (asset) return asset;
            }

            return default;
        }

        public async UniTask<bool> HasAssetAsync<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            if (await HasInternalAssetAsync<T>(path)) return true;

            // get on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)) {
                if (await m.AssetAPI.HasInternalAssetAsync<T>(path)) return true;
            }

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null && await mod.AssetAPI.HasInternalAssetAsync<T>(path);
        }

        public async UniTask<T> GetAssetAsync<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            // get on Internal mod
            if (await HasInternalAssetAsync<T>(path)) return await GetInternalAssetAsync<T>(path);

            // get on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)) {
                if (await m.AssetAPI.HasInternalAssetAsync<T>(path)) return await m.AssetAPI.GetInternalAssetAsync<T>(path);
            }

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null ? await mod.AssetAPI.GetInternalAssetAsync<T>(path) : null;
        }

        public async UniTask<bool> HasInternalAssetAsync<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            await UniTask.Yield(); // Make it properly async

            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
                if (File.Exists(dirpath)) return true;
            }

            return false;
        }

        public async UniTask<T> GetInternalAssetAsync<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            await UniTask.Yield(); // Make it properly async

            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
                var asset = AssetDatabase.LoadAssetAtPath<T>(dirpath);
                if (asset) return asset;
            }

            return default;
        }

        public async UniTask<Scene> LoadWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            // load on Internal mod
            if (IsLoadedWorld(path)) return GetWorld(path);

            if (HasInternalWorld(path)) return await LoadInternalWorld(path, mode);

            // load on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
                                               .Where(m => m.AssetAPI.HasInternalWorld(path))) return await m.AssetAPI.LoadInternalWorld(path, mode);

            // load from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            if (mod != null) return await mod.AssetAPI.LoadInternalWorld(path, mode);

            return default;
        }

        public bool HasWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            if (HasInternalWorld(path)) return true;

            // get on other mods
            if (AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
                                 .Any(m => m.AssetAPI.HasInternalWorld(path))) return true;

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null && mod.AssetAPI.HasInternalWorld(path);
        }

        public Scene GetWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            // get on Internal mod
            if (IsLoadedInternalWorld(path)) return GetInternalWorld(path);

            // get on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
                                               .Where(m => m.AssetAPI.IsLoadedInternalWorld(path))) return m.AssetAPI.GetInternalWorld(path);

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null
                ? mod.AssetAPI.GetInternalWorld(path)
                : default;
        }

        public async UniTask UnloadWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            // unload on Internal mod
            if (IsLoadedInternalWorld(path)) {
                await UnloadInternalWorld(path);
                return;
            }

            // unload on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
                                               .Where(m => m.AssetAPI.IsLoadedInternalWorld(path))) {
                await m.AssetAPI.UnloadInternalWorld(path);
                return;
            }

            // unload from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            if (mod != null) await mod.AssetAPI.UnloadInternalWorld(path);
        }

        public bool IsLoadedWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            if (IsLoadedInternalWorld(path)) return true;

            // get on other mods
            if (AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
                                 .Any(m => m.AssetAPI.IsLoadedInternalWorld(path))) return true;

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null && mod.AssetAPI.IsLoadedInternalWorld(path);
        }

        public async UniTask<Scene> LoadInternalWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
                var scenes = AssetDatabase.FindAssets("t:Scene")
                                          .Select(AssetDatabase.GUIDToAssetPath)
                                          .Where(p => FormatPath(p) == FormatPath(dirpath))
                                          .ToArray();
                if (scenes.Length == 0) continue;
                var scenePath = scenes[0];
                var scene = SceneManager.GetSceneByPath(scenePath);
                if (!scene.isLoaded) await SceneManager.LoadSceneAsync(scenePath, mode);
                scene = SceneManager.GetSceneByPath(scenePath);
                return scene;
            }

            return default;
        }

        public async UniTask UnloadInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
                var scenes = AssetDatabase.FindAssets("t:Scene")
                                          .Select(AssetDatabase.GUIDToAssetPath)
                                          .Where(p => FormatPath(p) == FormatPath(dirpath))
                                          .ToArray();
                if (scenes.Length == 0) continue;
                var scenePath = scenes[0];
                var scene = SceneManager.GetSceneByPath(scenePath);
                if (scene.isLoaded) await SceneManager.UnloadSceneAsync(scene);
                return;
            }
        }

        public bool HasInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));

                var scenes = AssetDatabase.FindAssets("t:Scene")
                                          .Select(AssetDatabase.GUIDToAssetPath)
                                          .Where(p => FormatPath(p) == FormatPath(dirpath))
                                          .ToArray();
                if (scenes.Length != 0) return true;
            }

            return false;
        }

        public bool IsLoadedInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));

                var scenes = AssetDatabase.FindAssets("t:Scene")
                                          .Select(AssetDatabase.GUIDToAssetPath)
                                          .Where(p => FormatPath(p) == FormatPath(dirpath))
                                          .ToArray();
                if (scenes.Length == 0) continue;
                var scenePath = scenes[0];
                var scene = SceneManager.GetSceneByPath(scenePath);
                return scene.isLoaded;
            }

            return false;
        }

        public Scene GetInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));

                var scenes = AssetDatabase.FindAssets("t:Scene")
                                          .Select(AssetDatabase.GUIDToAssetPath)
                                          .Where(p => FormatPath(p) == FormatPath(dirpath))
                                          .ToArray();
                if (scenes.Length == 0) continue;
                var scenePath = scenes[0];
                var scene = SceneManager.GetSceneByPath(scenePath);
                return scene.isLoaded ? scene : default;
            }

            return default;
        }

        // Missing interface methods
        public bool IsLoaded()
            => _loaded;

        public async UniTask<bool> RegisterAssets() {
            await UniTask.Yield();
            _loaded = true;
            return true;
        }

        public async UniTask<bool> UnRegisterAssets() {
            await UniTask.Yield();
            _loaded = false;
            return true;
        }
    }

}
#endif