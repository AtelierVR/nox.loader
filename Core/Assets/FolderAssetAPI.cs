using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Nox.ModLoader.Mods;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Cores.Assets
{
    /// <summary>
    /// Asset API implementation for folder-based mods.
    /// Handles loading assets from asset bundles in the mod's folder.
    /// </summary>
    public class FolderAssetAPI : IAssetAPI
    {
        private readonly FolderMod _mod;
        private bool _isLoaded;
        private readonly Dictionary<string, UnityEngine.Object> _loadedAssets = new();
        private readonly List<AssetBundle> _loadedBundles = new();
        private readonly Dictionary<string, Scene> _loadedScenes = new();

        public FolderAssetAPI(FolderMod mod)
        {
            _mod = mod;
        }

        public bool IsLoaded() => _isLoaded;

        public async UniTask<bool> RegisterAssets()
        {
            var folderPath = _mod.FolderPath;
            if (string.IsNullOrEmpty(folderPath))
                return _isLoaded = true;

            try
            {
                var assetsPath = Path.Combine(folderPath, "assets");
                if (Directory.Exists(assetsPath))
                {
                    // Load all asset bundles from the assets folder
                    var bundles = Directory.GetFiles(assetsPath, "*.bundle");
                    foreach (var bundle in bundles)
                    {
                        var request = AssetBundle.LoadFromFileAsync(bundle);
                        await UniTask.WaitUntil(() => request.isDone);

                        if (request.assetBundle != null)
                        {
                            _loadedBundles.Add(request.assetBundle);
                            
                            // Pre-load all assets from the bundle
                            var assetNames = request.assetBundle.GetAllAssetNames();
                            foreach (var assetName in assetNames)
                            {
                                var asset = request.assetBundle.LoadAsset(assetName);
                                if (asset != null)
                                {
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
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to register assets for mod {_mod.Metadata?.GetId()}: {ex.Message}");
                return false;
            }
        }

        public async UniTask<bool> UnRegisterAssets()
        {
            try
            {
                // Unload all scenes
                foreach (var kvp in _loadedScenes)
                {
                    if (kvp.Value.isLoaded)
                        await SceneManager.UnloadSceneAsync(kvp.Value);
                }
                _loadedScenes.Clear();

                // Clear loaded assets
                _loadedAssets.Clear();

                // Unload all bundles
                foreach (var bundle in _loadedBundles)
                {
                    if (bundle != null)
                        bundle.Unload(true);
                }
                _loadedBundles.Clear();

                _isLoaded = false;
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to unregister assets for mod {_mod.Metadata?.GetId()}: {ex.Message}");
                return false;
            }
        }

        #region Asset Names

        public KeyValuePair<string, string>[] GetAssetNames()
        {
            var names = new List<KeyValuePair<string, string>>();
            var modId = _mod.Metadata?.GetId() ?? "";
            foreach (var key in _loadedAssets.Keys)
                names.Add(new KeyValuePair<string, string>(modId, key));
            return names.ToArray();
        }

        public KeyValuePair<string, string>[] GetAssetNames(string ns)
        {
            if (ns != _mod.Metadata?.GetId())
                return Array.Empty<KeyValuePair<string, string>>();
            return GetAssetNames();
        }

        public KeyValuePair<string, string>[] GetLocalAssetNames()
            => GetAssetNames();

        public KeyValuePair<string, string>[] GetOverrideAssetNames(string ns)
            => Array.Empty<KeyValuePair<string, string>>();

        #endregion

        #region Sync Asset Methods

        public bool HasAsset<T>(string ns, string name) where T : UnityEngine.Object
        {
            if (ns != _mod.Metadata?.GetId())
                return false;
            return HasAsset<T>(name);
        }

        public T GetAsset<T>(string ns, string name) where T : UnityEngine.Object
        {
            if (ns != _mod.Metadata?.GetId())
                return null;
            return GetAsset<T>(name);
        }

        public bool HasAsset<T>(string name) where T : UnityEngine.Object
        {
            if (!_loadedAssets.TryGetValue(name, out var asset))
                return false;
            return asset is T;
        }

        public T GetAsset<T>(string name) where T : UnityEngine.Object
        {
            if (_loadedAssets.TryGetValue(name, out var asset))
                return asset as T;
            return null;
        }

        public bool HasLocalAsset<T>(string name) where T : UnityEngine.Object
            => HasAsset<T>(name);

        public T GetLocalAsset<T>(string name) where T : UnityEngine.Object
            => GetAsset<T>(name);

        public bool HasOverrideAsset<T>(string ns, string name) where T : UnityEngine.Object
            => false;

        public T GetOverrideAsset<T>(string ns, string name) where T : UnityEngine.Object
            => null;

        #endregion

        #region Async Asset Methods

        public UniTask<bool> HasAssetAsync<T>(string ns, string name) where T : UnityEngine.Object
            => UniTask.FromResult(HasAsset<T>(ns, name));

        public UniTask<T> GetAssetAsync<T>(string ns, string name) where T : UnityEngine.Object
            => UniTask.FromResult(GetAsset<T>(ns, name));

        public UniTask<bool> HasAssetAsync<T>(string name) where T : UnityEngine.Object
            => UniTask.FromResult(HasAsset<T>(name));

        public UniTask<T> GetAssetAsync<T>(string name) where T : UnityEngine.Object
            => UniTask.FromResult(GetAsset<T>(name));

        public UniTask<bool> HasLocalAssetAsync<T>(string name) where T : UnityEngine.Object
            => UniTask.FromResult(HasLocalAsset<T>(name));

        public UniTask<T> GetLocalAssetAsync<T>(string name) where T : UnityEngine.Object
            => UniTask.FromResult(GetLocalAsset<T>(name));

        public UniTask<bool> HasOverrideAssetAsync<T>(string ns, string name) where T : UnityEngine.Object
            => UniTask.FromResult(false);

        public UniTask<T> GetOverrideAssetAsync<T>(string ns, string name) where T : UnityEngine.Object
            => UniTask.FromResult<T>(null);

        #endregion

        #region World Methods (with namespace)

        public bool HasWorld(string ns, string name)
        {
            if (ns != _mod.Metadata?.GetId())
                return false;
            return HasWorld(name);
        }

        public bool IsLoadedWorld(string ns, string name)
        {
            if (ns != _mod.Metadata?.GetId())
                return false;
            return IsLoadedWorld(name);
        }

        public Scene GetWorld(string ns, string name)
        {
            if (ns != _mod.Metadata?.GetId())
                return default;
            return GetWorld(name);
        }

        public async UniTask<Scene> LoadWorld(string ns, string name, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (ns != _mod.Metadata?.GetId())
                return default;
            return await LoadWorld(name, mode);
        }

        public async UniTask UnloadWorld(string ns, string name)
        {
            if (ns != _mod.Metadata?.GetId())
                return;
            await UnloadWorld(name);
        }

        #endregion

        #region World Methods (without namespace)

        public bool HasWorld(string name)
        {
            // Check if any loaded bundle contains the scene
            foreach (var bundle in _loadedBundles)
            {
                var scenePaths = bundle.GetAllScenePaths();
                foreach (var scenePath in scenePaths)
                {
                    if (Path.GetFileNameWithoutExtension(scenePath) == name)
                        return true;
                }
            }
            return false;
        }

        public bool IsLoadedWorld(string name)
            => _loadedScenes.ContainsKey(name) && _loadedScenes[name].isLoaded;

        public Scene GetWorld(string name)
            => _loadedScenes.TryGetValue(name, out var scene) ? scene : default;

        public async UniTask<Scene> LoadWorld(string name, LoadSceneMode mode = LoadSceneMode.Single)
        {
            // Find the scene in loaded bundles
            foreach (var bundle in _loadedBundles)
            {
                var scenePaths = bundle.GetAllScenePaths();
                foreach (var scenePath in scenePaths)
                {
                    if (Path.GetFileNameWithoutExtension(scenePath) != name)
                        continue;

                    var op = SceneManager.LoadSceneAsync(scenePath, mode);
                    await UniTask.WaitUntil(() => op.isDone);

                    var scene = SceneManager.GetSceneByPath(scenePath);
                    _loadedScenes[name] = scene;
                    return scene;
                }
            }
            return default;
        }

        public async UniTask UnloadWorld(string name)
        {
            if (!_loadedScenes.TryGetValue(name, out var scene))
                return;

            if (scene.isLoaded)
                await SceneManager.UnloadSceneAsync(scene);

            _loadedScenes.Remove(name);
        }

        #endregion

        #region Local World Methods

        public bool HasLocalWorld(string name)
            => HasWorld(name);

        public bool IsLoadedLocalWorld(string name)
            => IsLoadedWorld(name);

        public Scene GetLocalWorld(string name)
            => GetWorld(name);

        public UniTask<Scene> LoadLocalWorld(string name, LoadSceneMode mode = LoadSceneMode.Single)
            => LoadWorld(name, mode);

        public UniTask UnloadLocalWorld(string name)
            => UnloadWorld(name);

        #endregion

        #region Override World Methods

        public bool HasOverrideWorld(string ns, string name)
            => false;

        public bool IsLoadedOverrideWorld(string ns, string name)
            => false;

        public Scene GetOverrideWorld(string ns, string name)
            => default;

        public UniTask<Scene> LoadOverrideWorld(string ns, string name, LoadSceneMode mode = LoadSceneMode.Single)
            => UniTask.FromResult(default(Scene));

        public UniTask UnloadOverrideWorld(string ns, string name)
            => UniTask.CompletedTask;

        #endregion
    }
}
