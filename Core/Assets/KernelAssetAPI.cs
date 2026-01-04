using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Logger = Nox.CCK.Utils.Logger;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Cores.Assets {

    public class KernelAssetAPI : IAssetAPI {
        public KernelAssetAPI(ModLoader.Mods.KernelMod kernelMod)
            => _kernelMod = kernelMod;

        private readonly ModLoader.Mods.KernelMod _kernelMod;
        private List<AssetBundle> _assetBundles;
        private bool _loaded;

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
                if (!string.IsNullOrEmpty(HasAssetFromBundle(n, path.Path))) return true;
            }

            return false;
        }

        public T GetInternalAsset<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var asset = GetAssetFromBundle<T>(n, path.Path);
                if (asset != null) return asset;
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
                if (!string.IsNullOrEmpty(HasAssetFromBundle(n, path.Path))) return true;
            }

            return false;
        }

        public async UniTask<T> GetInternalAssetAsync<T>(ResourceIdentifier path)
            where T : Object {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            await UniTask.Yield(); // Make it properly async

            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var asset = GetAssetFromBundle<T>(n, path.Path);
                if (asset != null) return asset;
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
                var scene = await LoadWorldFromBundle(n, path.Path, mode);
                if (scene.IsValid()) return scene;
            }

            return default;
        }

        public async UniTask UnloadInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) await UnloadWorldFromBundle(n, path.Path);
        }

        public bool HasInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                if (!string.IsNullOrEmpty(HasWorldFromBundle(n, path.Path))) return true;
            }

            return false;
        }

        public bool IsLoadedInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                if (IsLoadedWorldFromBundle(n, path.Path)) return true;
            }

            return false;
        }

        public Scene GetInternalWorld(ResourceIdentifier path) {
            path = AssetAPIExtension.Resolve(path, _kernelMod);
            var namespaces = AssetAPIExtension.GetNamespaces(path);

            foreach (var n in namespaces) {
                var scene = GetWorldFromBundle(n, path.Path);
                if (scene.IsValid()) return scene;
            }

            return default;
        }


        // Bundle helper methods
        public string GetAssetPathFromBundle(string ns, string name) {
            var basepath = _kernelMod.Metadata.GetCustom<JObject>("kernel")?.GetValue("assets_path")?.ToObject<string>();
            if (string.IsNullOrEmpty(basepath)) return null;

            return AssetAPIExtension.FormatPath(Path.Combine(basepath, ns, name));
        }

        public T GetAssetFromBundle<T>(string ns, string name)
            where T : Object {
            if (_assetBundles == null) return null;

            var v = HasAssetFromBundle(ns, name);
            if (v == null) return null;

            foreach (var n in _assetBundles)
                if (n.Contains(v))
                    return n.LoadAsset<T>(v);

            return null;
        }

        public KeyValuePair<string, string>[] GetAssetNamesFromBundle(string ns) {
            if (_assetBundles == null) return null;

            var v = GetAssetPathFromBundle(ns, "");
            List<KeyValuePair<string, string>> assets = new();
            foreach (var n in _assetBundles)
            foreach (var a in n.GetAllAssetNames())
                if (AssetAPIExtension.FormatPath(a).StartsWith(v))
                    assets.Add(new KeyValuePair<string, string>(ns, a[(a.IndexOf('/') + 1)..]));

            return assets.ToArray();
        }

        public string HasAssetFromBundle(string ns, string name) {
            if (_assetBundles == null) return null;

            var v = GetAssetPathFromBundle(ns, name);
            foreach (var n in _assetBundles)
            foreach (var a in n.GetAllAssetNames())
                if (AssetAPIExtension.FormatPath(a) == v)
                    return a;

            return null;
        }

        public async UniTask<bool> RegisterAssets() {
            if (_assetBundles != null) return true;

            var kernel = _kernelMod.Metadata.GetCustom<JObject>("kernel");
            if (kernel == null) return false;

            var assets = kernel.GetValue("assets")?.ToObject<JObject[]>();
            if (assets == null) return false;

            _assetBundles = new List<AssetBundle>();

            foreach (var asset in assets) {
                var path = asset.GetValue("file")?.ToObject<string>();
                if (string.IsNullOrEmpty(path)) continue;

                path = Path.Combine(Application.dataPath, "Nox", path);
                if (!File.Exists(path)) continue;

                var bundle = await AssetBundle.LoadFromFileAsync(path);
                if (!bundle) continue;

                _assetBundles.Add(bundle);
            }

            _loaded = true;
            return true;
        }

        public async UniTask<bool> UnRegisterAssets() {
            if (_assetBundles == null) return true;
            foreach (var bundle in _assetBundles) await bundle.UnloadAsync(true);
            _assetBundles = null;
            _loaded = false;
            return true;
        }

        public bool IsLoaded()
            => _loaded;

        public async UniTask<Scene> LoadWorldFromBundle(string ns, string name, LoadSceneMode mode = LoadSceneMode.Single) {
            var path = GetAssetPathFromBundle(ns, name);
            if (string.IsNullOrEmpty(path)) return default;

            foreach (var n in _assetBundles)
            foreach (var a in n.GetAllScenePaths()) {
                if (AssetAPIExtension.FormatPath(a) != path) continue;
                var scene = SceneManager.GetSceneByPath(a);
                if (scene.isLoaded && scene.IsValid()) return scene;
                await SceneManager.LoadSceneAsync(a, mode);
                scene = SceneManager.GetSceneByPath(a);
                return scene;
            }

            return default;
        }

        public async UniTask UnloadWorldFromBundle(string ns, string name) {
            var path = GetAssetPathFromBundle(ns, name);

            if (string.IsNullOrEmpty(path)) return;

            foreach (var n in _assetBundles)
            foreach (var a in n.GetAllScenePaths())
                if (AssetAPIExtension.FormatPath(a) == path) {
                    var scene = SceneManager.GetSceneByPath(a);
                    if (scene.isLoaded) await SceneManager.UnloadSceneAsync(scene);
                    return;
                }
        }

        public string HasWorldFromBundle(string ns, string name) {
            var path = GetAssetPathFromBundle(ns, name);
            if (string.IsNullOrEmpty(path)) return null;

            foreach (var n in _assetBundles)
            foreach (var a in n.GetAllScenePaths()) {
                if (AssetAPIExtension.FormatPath(a) == path) return a;
            }

            return null;
        }

        public bool IsLoadedWorldFromBundle(string ns, string name) {
            var path = GetAssetPathFromBundle(ns, name);
            if (string.IsNullOrEmpty(path)) return false;

            foreach (var n in _assetBundles)
            foreach (var a in n.GetAllScenePaths())
                if (AssetAPIExtension.FormatPath(a) == path) {
                    var scene = SceneManager.GetSceneByPath(a);
                    return scene.isLoaded;
                }

            return false;
        }

        public Scene GetWorldFromBundle(string ns, string name) {
            var path = GetAssetPathFromBundle(ns, name);
            if (string.IsNullOrEmpty(path)) return default;

            foreach (var n in _assetBundles)
            foreach (var a in n.GetAllScenePaths())
                if (AssetAPIExtension.FormatPath(a) == path)
                    return SceneManager.GetSceneByPath(a);

            return default;
        }
    }

}