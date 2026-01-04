using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.ModLoader.Mods;
using Nox.CCK.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Cores.Assets {

    /// <summary>
    /// Asset API implementation for folder-based mods.
    /// Handles loading assets from asset bundles in the mod's folder.
    /// </summary>
    public class FolderAssetAPI : IAssetAPI {
        private readonly FolderMod _mod;
        private bool _isLoaded;
        private readonly Dictionary<string, UnityEngine.Object> _loadedAssets = new();
        private readonly List<AssetBundle> _loadedBundles = new();
        private readonly Dictionary<string, Scene> _loadedScenes = new();

        public FolderAssetAPI(FolderMod mod) {
            _mod = mod;
        }

        public bool IsLoaded() => _isLoaded;

        public async UniTask<bool> RegisterAssets() {
            var folderPath = _mod.FolderPath;
            if (string.IsNullOrEmpty(folderPath)) return _isLoaded = true;

            try {
                var assetsPath = Path.Combine(folderPath, "assets");
                if (Directory.Exists(assetsPath)) {
                    // Load all asset bundles from the assets folder
                    var bundles = Directory.GetFiles(assetsPath, "*.bundle");
                    foreach (var bundle in bundles) {
                        var request = AssetBundle.LoadFromFileAsync(bundle);
                        await UniTask.WaitUntil(() => request.isDone);

                        if (request.assetBundle != null) {
                            _loadedBundles.Add(request.assetBundle);

                            // Pre-load all assets from the bundle
                            var assetNames = request.assetBundle.GetAllAssetNames();
                            foreach (var assetName in assetNames) {
                                var asset = request.assetBundle.LoadAsset(assetName);
                                if (asset != null) {
                                    var key = Path.GetFileNameWithoutExtension(assetName);
                                    _loadedAssets[key] = asset;
                                }
                            }

                            Logger.LogDebug($"Loaded asset bundle: {bundle} ({assetNames.Length} assets)");
                        }
                    }
                }

                _isLoaded = true;
                return true;
            } catch (Exception ex) {
                Logger.LogError($"Failed to register assets for mod {_mod.Metadata?.GetId()}: {ex.Message}");
                return false;
            }
        }

        public async UniTask<bool> UnRegisterAssets() {
            try {
                // Unload all scenes
                foreach (var kvp in _loadedScenes) {
                    if (kvp.Value.isLoaded) await SceneManager.UnloadSceneAsync(kvp.Value);
                }

                _loadedScenes.Clear();

                // Clear loaded assets
                _loadedAssets.Clear();

                // Unload all bundles
                foreach (var bundle in _loadedBundles) {
                    if (bundle != null) bundle.Unload(true);
                }

                _loadedBundles.Clear();

                _isLoaded = false;
                return true;
            } catch (Exception ex) {
                Logger.LogError($"Failed to unregister assets for mod {_mod.Metadata?.GetId()}: {ex.Message}");
                return false;
            }
        }


        #region Asset Methods

        public bool HasAsset<T>(ResourceIdentifier path)
            where T : UnityEngine.Object {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (HasInternalAsset<T>(path)) return true;

            // get on other mods
            if (AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
                                 .Any(m => m.AssetAPI.HasInternalAsset<T>(path))) return true;

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null && mod.AssetAPI.HasInternalAsset<T>(path);
        }

        public T GetAsset<T>(ResourceIdentifier path)
            where T : UnityEngine.Object {
            path = AssetAPIExtension.Resolve(path, _mod);
            // get on Internal mod
            if (HasInternalAsset<T>(path)) return GetInternalAsset<T>(path);

            // get on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
                                               .Where(m => m.AssetAPI.HasInternalAsset<T>(path))) return m.AssetAPI.GetInternalAsset<T>(path);

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod?.AssetAPI.GetInternalAsset<T>(path);
        }

        public bool HasInternalAsset<T>(ResourceIdentifier path)
            where T : UnityEngine.Object {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (_mod.Metadata?.GetId() != path.Namespace) return false;

            if (!_loadedAssets.TryGetValue(path.Path, out var asset)) return false;
            return asset is T;
        }

        public T GetInternalAsset<T>(ResourceIdentifier path)
            where T : UnityEngine.Object {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (_mod.Metadata?.GetId() != path.Namespace) return null;

            if (_loadedAssets.TryGetValue(path.Path, out var asset)) return asset as T;
            return null;
        }

        #endregion


        #region Async Asset Methods

        public async UniTask<bool> HasAssetAsync<T>(ResourceIdentifier path)
            where T : UnityEngine.Object {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (await HasInternalAssetAsync<T>(path)) return true;

            // get on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)) {
                if (await m.AssetAPI.HasInternalAssetAsync<T>(path)) return true;
            }

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null && await mod.AssetAPI.HasInternalAssetAsync<T>(path);
        }

        public async UniTask<T> GetAssetAsync<T>(ResourceIdentifier path)
            where T : UnityEngine.Object {
            path = AssetAPIExtension.Resolve(path, _mod);
            // get on Internal mod
            if (await HasInternalAssetAsync<T>(path)) return await GetInternalAssetAsync<T>(path);

            // get on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)) {
                if (await m.AssetAPI.HasInternalAssetAsync<T>(path)) return await m.AssetAPI.GetInternalAssetAsync<T>(path);
            }

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null ? await mod.AssetAPI.GetInternalAssetAsync<T>(path) : null;
        }

        public async UniTask<bool> HasInternalAssetAsync<T>(ResourceIdentifier path)
            where T : UnityEngine.Object {
            path = AssetAPIExtension.Resolve(path, _mod);
            await UniTask.Yield(); // Make it properly async
            return HasInternalAsset<T>(path);
        }

        public async UniTask<T> GetInternalAssetAsync<T>(ResourceIdentifier path)
            where T : UnityEngine.Object {
            path = AssetAPIExtension.Resolve(path, _mod);
            await UniTask.Yield(); // Make it properly async
            return GetInternalAsset<T>(path);
        }

        #endregion


        #region World Methods

        public async UniTask<Scene> LoadWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
            path = AssetAPIExtension.Resolve(path, _mod);
            // load on Internal mod
            if (IsLoadedWorld(path)) return GetWorld(path);

            if (HasInternalWorld(path)) return await LoadInternalWorld(path, mode);

            // load on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
                                               .Where(m => m.AssetAPI.HasInternalWorld(path))) return await m.AssetAPI.LoadInternalWorld(path, mode);

            // load from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            if (mod != null) return await mod.AssetAPI.LoadInternalWorld(path, mode);

            return default;
        }

        public bool HasWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (HasInternalWorld(path)) return true;

            // get on other mods
            if (AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
                                 .Any(m => m.AssetAPI.HasInternalWorld(path))) return true;

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null && mod.AssetAPI.HasInternalWorld(path);
        }

        public Scene GetWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _mod);
            // get on Internal mod
            if (IsLoadedInternalWorld(path)) return GetInternalWorld(path);

            // get on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
                                               .Where(m => m.AssetAPI.IsLoadedInternalWorld(path))) return m.AssetAPI.GetInternalWorld(path);

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null
                ? mod.AssetAPI.GetInternalWorld(path)
                : default;
        }

        public async UniTask UnloadWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _mod);
            // unload on Internal mod
            if (IsLoadedInternalWorld(path)) {
                await UnloadInternalWorld(path);
                return;
            }

            // unload on other mods
            foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
                                               .Where(m => m.AssetAPI.IsLoadedInternalWorld(path))) {
                await m.AssetAPI.UnloadInternalWorld(path);
                return;
            }

            // unload from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            if (mod != null) await mod.AssetAPI.UnloadInternalWorld(path);
        }

        public bool IsLoadedWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (IsLoadedInternalWorld(path)) return true;

            // get on other mods
            if (AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
                                 .Any(m => m.AssetAPI.IsLoadedInternalWorld(path))) return true;

            // get from the initial mod
            var mod = ModManager.GetMod(path.Namespace);
            return mod != null && mod.AssetAPI.IsLoadedInternalWorld(path);
        }

        public async UniTask<Scene> LoadInternalWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (_mod.Metadata?.GetId() != path.Namespace) return default;

            var name = path.Path;
            // Find the scene in loaded bundles
            foreach (var bundle in _loadedBundles) {
                var scenePaths = bundle.GetAllScenePaths();
                foreach (var scenePath in scenePaths) {
                    if (Path.GetFileNameWithoutExtension(scenePath) != name) continue;

                    var op = SceneManager.LoadSceneAsync(scenePath, mode);
                    await UniTask.WaitUntil(() => op.isDone);

                    var scene = SceneManager.GetSceneByPath(scenePath);
                    _loadedScenes[name] = scene;
                    return scene;
                }
            }

            return default;
        }

        public async UniTask UnloadInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (_mod.Metadata?.GetId() != path.Namespace) return;

            var name = path.Path;
            if (!_loadedScenes.TryGetValue(name, out var scene)) return;

            if (scene.isLoaded) await SceneManager.UnloadSceneAsync(scene);

            _loadedScenes.Remove(name);
        }

        public bool HasInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (_mod.Metadata?.GetId() != path.Namespace) return false;

            var name = path.Path;
            // Check if any loaded bundle contains the scene
            foreach (var bundle in _loadedBundles) {
                var scenePaths = bundle.GetAllScenePaths();
                foreach (var scenePath in scenePaths) {
                    if (Path.GetFileNameWithoutExtension(scenePath) == name) return true;
                }
            }

            return false;
        }

        public bool IsLoadedInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (_mod.Metadata?.GetId() != path.Namespace) return false;

            var name = path.Path;
            return _loadedScenes.ContainsKey(name) && _loadedScenes[name].isLoaded;
        }

        public Scene GetInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _mod);
            if (_mod.Metadata?.GetId() != path.Namespace) return default;

            var name = path.Path;
            return _loadedScenes.TryGetValue(name, out var scene) ? scene : default;
        }

        #endregion


    }

}