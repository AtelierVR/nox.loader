using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Logger = Nox.CCK.Utils.Logger;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Nox.ModLoader.Cores.Assets {
	public class KernelAssetAPI : IAssetAPI {
		public KernelAssetAPI(ModLoader.Mods.KernelMod kernelMod)
			=> _kernelMod = kernelMod;

		private readonly ModLoader.Mods.KernelMod _kernelMod;
		private          List<AssetBundle>        _assetBundles;

		public bool HasAsset<T>(string name) where T : Object
			=> HasAsset<T>(_kernelMod.Metadata.GetId(), name);

		public KeyValuePair<string, string>[] GetAssetNames(string ns) {
			var list = new List<KeyValuePair<string, string>>();
			// get on override mod
			list.AddRange(GetOverrideAssetNames(ns));

			// get on other mods
			foreach (var m in ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns)))
				list.AddRange(m.AssetAPI.GetOverrideAssetNames(ns));

			// get from the initial mod
			var mod = ModManager.GetMod(ns);
			if (mod != null) list.AddRange(mod.AssetAPI.GetLocalAssetNames());

			return list.ToArray();
		}

		public KeyValuePair<string, string>[] GetAssetNames()
			=> GetOverrideAssetNames(_kernelMod.Metadata.GetId());

		public KeyValuePair<string, string>[] GetLocalAssetNames()
			=> GetOverrideAssetNames(_kernelMod.Metadata.GetId());

		public virtual KeyValuePair<string, string>[] GetOverrideAssetNames(string ns)
			=> GetAssetNamesFromBundle(ns);

		public bool HasAsset<T>(string ns, string name) where T : Object {
			if (HasOverrideAsset<T>(ns, name))
				return true;

			// get on other mods
			if (ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))
			    .Any(m => m.AssetAPI.HasOverrideAsset<T>(ns, name)))
				return true;

			// get from the initial mod
			var mod = ModManager.GetMod(ns);
			return mod != null && mod.AssetAPI.HasLocalAsset<T>(name);
		}

		public T GetAsset<T>(string name) where T : Object
			=> GetAsset<T>(_kernelMod.Metadata.GetId(), name);

		public T GetAsset<T>(string ns, string name) where T : Object {
			// get on override mod
			if (HasOverrideAsset<T>(ns, name))
				return GetOverrideAsset<T>(ns, name);

			// get on other mods
			foreach (var m in ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))
				         .Where(m => m.AssetAPI.HasOverrideAsset<T>(ns, name)))
				return m.AssetAPI.GetOverrideAsset<T>(ns, name);

			// get from the initial mod
			var mod = ModManager.GetMod(ns);
			return mod?.AssetAPI.GetLocalAsset<T>(name);
		}

		public bool HasLocalAsset<T>(string name) where T : Object
			=> HasOverrideAsset<T>(_kernelMod.Metadata.GetId(), name);

		public T GetLocalAsset<T>(string name) where T : Object
			=> GetOverrideAsset<T>(_kernelMod.Metadata.GetId(), name);

		public bool HasOverrideAsset<T>(string ns, string name) where T : Object {
			List<string> namespaces = new() { ns };
			var          mod        = ModManager.GetMod(ns);

			if (mod != null) {
				var meta = mod.GetMetadata();
				namespaces.Add(meta.GetId());
				namespaces.AddRange(meta.GetProvides());
			}

			foreach (var n in namespaces) {
				if (!string.IsNullOrEmpty(HasAssetFromBundle(n, name)))
					return true;
			}

			return false;
		}

		public T GetOverrideAsset<T>(string ns, string name) where T : Object {
			List<string> namespaces = new() { ns };
			var          mod        = ModManager.GetMod(ns);

			if (mod != null) {
				var meta = mod.GetMetadata();
				namespaces.Add(meta.GetId());
				namespaces.AddRange(meta.GetProvides());
			}

			foreach (var n in namespaces) {
				var asset = GetAssetFromBundle<T>(n, name);
				if (asset != null)
					return asset;
			}

			return null;
		}

		// Async versions for assets
		public async UniTask<bool> HasAssetAsync<T>(string name) where T : Object
			=> await HasAssetAsync<T>(_kernelMod.Metadata.GetId(), name);

		public async UniTask<bool> HasAssetAsync<T>(string ns, string name) where T : Object {
			if (await HasOverrideAssetAsync<T>(ns, name))
				return true;

			// get on other mods
			foreach (var m in ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))) {
				if (await m.AssetAPI.HasOverrideAssetAsync<T>(ns, name))
					return true;
			}

			// get from the initial mod
			var mod = ModManager.GetMod(ns);
			return mod != null && await mod.AssetAPI.HasLocalAssetAsync<T>(name);
		}

		public async UniTask<T> GetAssetAsync<T>(string name) where T : Object
			=> await GetAssetAsync<T>(_kernelMod.Metadata.GetId(), name);

		public async UniTask<T> GetAssetAsync<T>(string ns, string name) where T : Object {
			// get on override mod
			if (await HasOverrideAssetAsync<T>(ns, name))
				return await GetOverrideAssetAsync<T>(ns, name);

			// get on other mods
			foreach (var m in ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))) {
				if (await m.AssetAPI.HasOverrideAssetAsync<T>(ns, name))
					return await m.AssetAPI.GetOverrideAssetAsync<T>(ns, name);
			}

			// get from the initial mod
			var mod = ModManager.GetMod(ns);
			return mod != null ? await mod.AssetAPI.GetLocalAssetAsync<T>(name) : null;
		}

		public async UniTask<bool> HasLocalAssetAsync<T>(string name) where T : Object
			=> await HasOverrideAssetAsync<T>(_kernelMod.Metadata.GetId(), name);

		public async UniTask<T> GetLocalAssetAsync<T>(string name) where T : Object
			=> await GetOverrideAssetAsync<T>(_kernelMod.Metadata.GetId(), name);

		public async UniTask<bool> HasOverrideAssetAsync<T>(string ns, string name) where T : Object {
			await UniTask.Yield(); // Make it properly async

			List<string> namespaces = new() { ns };
			var          mod        = ModManager.GetMod(ns);

			if (mod != null) {
				var meta = mod.GetMetadata();
				namespaces.Add(meta.GetId());
				namespaces.AddRange(meta.GetProvides());
			}

			foreach (var n in namespaces) {
				if (!string.IsNullOrEmpty(HasAssetFromBundle(n, name)))
					return true;
			}

			return false;
		}

		public async UniTask<T> GetOverrideAssetAsync<T>(string ns, string name) where T : Object {
			await UniTask.Yield(); // Make it properly async

			List<string> namespaces = new() { ns };
			var          mod        = ModManager.GetMod(ns);

			if (mod != null) {
				var meta = mod.GetMetadata();
				namespaces.Add(meta.GetId());
				namespaces.AddRange(meta.GetProvides());
			}

			foreach (var n in namespaces) {
				var asset = GetAssetFromBundle<T>(n, name);
				if (asset != null)
					return asset;
			}

			return null;
		}

		// World methods
		public async UniTask<Scene> LoadWorld(string name, LoadSceneMode mode = LoadSceneMode.Single)
			=> await LoadWorld(_kernelMod.Metadata.GetId(), name, mode);

		public async UniTask<Scene> LoadWorld(string ns, string name, LoadSceneMode mode = LoadSceneMode.Single) {
			if (IsLoadedWorld(ns, name)) {
				Logger.LogDebug($"Scene {ns}/{name} is already loaded.");
				return GetWorld(ns, name);
			}

			if (HasOverrideWorld(ns, name)) {
				Logger.LogDebug($"Loading override scene {ns}/{name}.");
				return await LoadOverrideWorld(ns, name, mode);
			}

			// load on other mods
			foreach (var m in ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))
				         .Where(m => m.AssetAPI.HasOverrideWorld(ns, name))) {
				Logger.LogDebug($"Loading override scene {ns}/{name} from mod {m.Metadata.GetId()}.");
				return await m.AssetAPI.LoadOverrideWorld(ns, name, mode);
			}

			// load from the initial mod
			var mod = ModManager.GetMod(ns);
			if (mod != null) {
				Logger.LogDebug($"Loading local scene {ns}/{name}.");
				return await mod.AssetAPI.LoadLocalWorld(name, mode);
			}

			Logger.LogWarning($"Scene {ns}/{name} not found.");
			return default;
		}

		public bool HasWorld(string name)
			=> HasWorld(_kernelMod.Metadata.GetId(), name);

		public bool HasWorld(string ns, string name) {
			if (HasOverrideWorld(ns, name))
				return true;

			// get on other mods
			if (ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))
			    .Any(m => m.AssetAPI.HasOverrideWorld(ns, name)))
				return true;

			// get from the initial mod
			var mod = ModManager.GetMod(ns);
			return mod != null && mod.AssetAPI.HasLocalAsset<Object>(name);
		}

		public Scene GetWorld(string name)
			=> GetWorld(_kernelMod.Metadata.GetId(), name);

		public Scene GetWorld(string ns, string name) {
			// get on override mod
			if (IsLoadedOverrideWorld(ns, name))
				return GetOverrideWorld(ns, name);

			// get on other mods
			foreach (var m in ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))
				         .Where(m => m.AssetAPI.IsLoadedOverrideWorld(ns, name)))
				return m.AssetAPI.GetOverrideWorld(ns, name);

			// get from the initial mod
			var mod = ModManager.GetMod(ns);
			return mod != null ? mod.AssetAPI.GetLocalWorld(name) : default;
		}

		public async UniTask UnloadWorld(string name)
			=> await UnloadWorld(_kernelMod.Metadata.GetId(), name);

		public async UniTask UnloadWorld(string ns, string name) {
			if (IsLoadedOverrideWorld(ns, name)) {
				await UnloadOverrideWorld(ns, name);
				return;
			}

			foreach (var m in ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))
				         .Where(m => m.AssetAPI.IsLoadedOverrideWorld(ns, name))) {
				await m.AssetAPI.UnloadOverrideWorld(ns, name);
				return;
			}

			var mod = ModManager.GetMod(ns);
			if (mod != null)
				await mod.AssetAPI.UnloadLocalWorld(name);
		}

		public bool IsLoadedWorld(string name)
			=> IsLoadedWorld(_kernelMod.Metadata.GetId(), name);

		public bool IsLoadedWorld(string ns, string name) {
			if (IsLoadedOverrideWorld(ns, name))
				return true;

			// get on other mods
			if (ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))
			    .Any(m => m.AssetAPI.IsLoadedOverrideWorld(ns, name)))
				return true;

			// get from the initial mod
			var mod = ModManager.GetMod(ns);
			return mod != null && mod.AssetAPI.IsLoadedLocalWorld(name);
		}

		public async UniTask<Scene> LoadLocalWorld(string name, LoadSceneMode mode = LoadSceneMode.Single)
			=> await LoadOverrideWorld(_kernelMod.Metadata.GetId(), name, mode);

		public async UniTask<Scene> LoadOverrideWorld(string ns, string name, LoadSceneMode mode = LoadSceneMode.Single) {
			List<string> namespaces = new() { ns };
			var          mod        = ModManager.GetMod(ns);

			if (mod != null) {
				var meta = mod.GetMetadata();
				namespaces.Add(meta.GetId());
				namespaces.AddRange(meta.GetProvides());
			}

			foreach (var n in namespaces) {
				var scene = await LoadWorldFromBundle(n, name, mode);
				if (scene.IsValid())
					return scene;
			}

			return default;
		}

		public async UniTask UnloadLocalWorld(string name)
			=> await UnloadOverrideWorld(_kernelMod.Metadata.GetId(), name);

		public async UniTask UnloadOverrideWorld(string ns, string name) {
			List<string> namespaces = new() { ns };
			var          mod        = ModManager.GetMod(ns);

			if (mod != null) {
				var meta = mod.GetMetadata();
				namespaces.Add(meta.GetId());
				namespaces.AddRange(meta.GetProvides());
			}

			foreach (var n in namespaces)
				await UnloadWorldFromBundle(n, name);
		}

		public bool HasOverrideWorld(string ns, string name) {
			List<string> namespaces = new() { ns };
			var          mod        = ModManager.GetMod(ns);

			if (mod != null) {
				var meta = mod.GetMetadata();
				namespaces.Add(meta.GetId());
				namespaces.AddRange(meta.GetProvides());
			}

			foreach (var n in namespaces) {
				if (!string.IsNullOrEmpty(HasWorldFromBundle(n, name)))
					return true;
			}

			return false;
		}

		public bool IsLoadedOverrideWorld(string ns, string name) {
			List<string> namespaces = new() { ns };
			var          mod        = ModManager.GetMod(ns);

			if (mod != null) {
				var meta = mod.GetMetadata();
				namespaces.Add(meta.GetId());
				namespaces.AddRange(meta.GetProvides());
			}

			foreach (var n in namespaces) {
				if (IsLoadedWorldFromBundle(n, name))
					return true;
			}

			return false;
		}

		public Scene GetOverrideWorld(string ns, string name) {
			List<string> namespaces = new() { ns };
			var          mod        = ModManager.GetMod(ns);

			if (mod != null) {
				var meta = mod.GetMetadata();
				namespaces.Add(meta.GetId());
				namespaces.AddRange(meta.GetProvides());
			}

			foreach (var n in namespaces) {
				var scene = GetWorldFromBundle(n, name);
				if (scene.IsValid())
					return scene;
			}

			return default;
		}

		public bool HasLocalWorld(string name)
			=> HasOverrideWorld(_kernelMod.Metadata.GetId(), name);

		public bool IsLoadedLocalWorld(string name)
			=> IsLoadedOverrideWorld(_kernelMod.Metadata.GetId(), name);

		public Scene GetLocalWorld(string name)
			=> GetOverrideWorld(_kernelMod.Metadata.GetId(), name);

		// Bundle helper methods
		public string GetAssetPathFromBundle(string ns, string name) {
			var basepath = _kernelMod.Metadata.GetCustom<JObject>("kernel")?.GetValue("assets_path")?.ToObject<string>();
			if (string.IsNullOrEmpty(basepath))
				return null;

			return FormatPath(Path.Combine(basepath, ns, name));
		}

		public string FormatPath(string path)
			=> path.Replace('\\', '/').ToLower();

		public T GetAssetFromBundle<T>(string ns, string name) where T : Object {
			if (_assetBundles == null)
				return null;

			var v = HasAssetFromBundle(ns, name);
			if (v == null)
				return null;

			foreach (var n in _assetBundles)
				if (n.Contains(v))
					return n.LoadAsset<T>(v);

			return null;
		}

		public KeyValuePair<string, string>[] GetAssetNamesFromBundle(string ns) {
			if (_assetBundles == null)
				return null;

			var v = GetAssetPathFromBundle(ns, "");
			List<KeyValuePair<string, string>> assets = new();
			foreach (var n in _assetBundles)
			foreach (var a in n.GetAllAssetNames())
				if (FormatPath(a).StartsWith(v))
					assets.Add(new KeyValuePair<string, string>(ns, a[(a.IndexOf('/') + 1)..]));

			return assets.ToArray();
		}

		public string HasAssetFromBundle(string ns, string name) {
			if (_assetBundles == null)
				return null;

			var v = GetAssetPathFromBundle(ns, name);
			foreach (var n in _assetBundles)
			foreach (var a in n.GetAllAssetNames())
				if (FormatPath(a) == v)
					return a;

			return null;
		}

		public async UniTask<bool> RegisterAssets() {
			if (_assetBundles != null)
				return true;

			var kernel = _kernelMod.Metadata.GetCustom<JObject>("kernel");
			if (kernel == null)
				return false;

			var assets = kernel.GetValue("assets")?.ToObject<JObject[]>();
			if (assets == null)
				return false;

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

			return true;
		}

		public async UniTask<bool> UnRegisterAssets() {
			if (_assetBundles == null)
				return true;
			foreach (var bundle in _assetBundles)
				await bundle.UnloadAsync(true);
			_assetBundles = null;
			return true;
		}

		public bool IsLoaded()
			=> _assetBundles != null;

		public async UniTask<Scene> LoadWorldFromBundle(string ns, string name, LoadSceneMode mode = LoadSceneMode.Single) {
			var path = GetAssetPathFromBundle(ns, name);
			if (string.IsNullOrEmpty(path)) return default;

			foreach (var n in _assetBundles)
			foreach (var a in n.GetAllScenePaths()) {
				if (FormatPath(a) != path) continue;
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

			if (string.IsNullOrEmpty(path))
				return;

			foreach (var n in _assetBundles)
			foreach (var a in n.GetAllScenePaths())
				if (FormatPath(a) == path) {
					var scene = SceneManager.GetSceneByPath(a);
					if (scene.isLoaded) await SceneManager.UnloadSceneAsync(scene);
					return;
				}
		}

		public string HasWorldFromBundle(string ns, string name) {
			var path = GetAssetPathFromBundle(ns, name);
			if (string.IsNullOrEmpty(path))
				return null;

			foreach (var n in _assetBundles)
			foreach (var a in n.GetAllScenePaths()) {
				if (FormatPath(a) == path)
					return a;
			}

			return null;
		}

		public bool IsLoadedWorldFromBundle(string ns, string name) {
			var path = GetAssetPathFromBundle(ns, name);
			if (string.IsNullOrEmpty(path))
				return false;

			foreach (var n in _assetBundles)
			foreach (var a in n.GetAllScenePaths())
				if (FormatPath(a) == path) {
					var scene = SceneManager.GetSceneByPath(a);
					return scene.isLoaded;
				}

			return false;
		}

		public Scene GetWorldFromBundle(string ns, string name) {
			var path = GetAssetPathFromBundle(ns, name);
			if (string.IsNullOrEmpty(path))
				return default;

			foreach (var n in _assetBundles)
			foreach (var a in n.GetAllScenePaths())
				if (FormatPath(a) == path)
					return SceneManager.GetSceneByPath(a);

			return default;
		}
	}
}