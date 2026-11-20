using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Nox.CCK.Utils;
using Nox.CCK;
using System;

namespace Nox.ModLoader.Cores.Assets {

	public class KernelAssetAPI : IAssetAPI {
		public KernelAssetAPI(ModLoader.Mods.KernelMod kernelMod)
			=> _mod = kernelMod;

		private readonly ModLoader.Mods.KernelMod _mod;
		private List<AssetBundle> _bundles;

		/// <summary>Assets of the registered bundles, keyed by their formatted path.</summary>
		private Dictionary<string, BundleAsset> _assetIndex;

		/// <summary>Scenes of the registered bundles, keyed by their formatted path.</summary>
		private Dictionary<string, string> _sceneIndex;

		/// <summary>
		/// Kernel <c>assets_path</c>: <c>null</c> until the first lookup (the metadata is assigned after this
		/// instance is constructed), empty when the kernel declares none.
		/// </summary>
		private string _assetsPath;

		public override string ToString()
			=> $"{GetType().Name}[Id={_mod.Metadata.Id}, Version={_mod.Metadata.Version}]";

		private bool IsOtherLoadedMod(ModLoader.Mods.Mod mod, string ns)
			=> mod != null && mod != _mod && mod.IsLoaded && !mod.Metadata.Match(ns);

		public bool HasAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			if (HasInternalAsset<T>(path))
				return true;

			// get on other mods
			var mods = ModManager.Mods;
			for (var i = 0; i < mods.Count; i++) {
				var other = mods[i];
				if (IsOtherLoadedMod(other, path.Namespace) && other.AssetAPI.HasInternalAsset<T>(path))
					return true;
			}

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && mod.AssetAPI.HasInternalAsset<T>(path);
		}

		public T GetAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			// get on Internal mod
			if (HasInternalAsset<T>(path))
				return GetInternalAsset<T>(path);

			// get on other mods
			var mods = ModManager.Mods;
			for (var i = 0; i < mods.Count; i++) {
				var other = mods[i];
				if (IsOtherLoadedMod(other, path.Namespace) && other.AssetAPI.HasInternalAsset<T>(path))
					return other.AssetAPI.GetInternalAsset<T>(path);
			}

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod?.AssetAPI.GetInternalAsset<T>(path);
		}

		public bool HasInternalAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				if (!TryGetBundleAsset(n, path.Path, out var found)) continue;

				if (found.Bundle.LoadAsset<T>(found.Name)) return true;
				if (found.Bundle.LoadAsset<SymbolicAsset>(found.Name) is { } symbolic)
					return HasAsset<T>(symbolic.Target);
				return false;
			}

			return false;
		}

		public T GetInternalAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				if (!TryGetBundleAsset(n, path.Path, out var found))
					continue;

				if (found.Bundle.LoadAsset<T>(found.Name) is { } asset)
					return asset;
				if (found.Bundle.LoadAsset<SymbolicAsset>(found.Name) is { } symbolic)
					return GetAsset<T>(symbolic.Target);
				return default;
			}

			return default;
		}

		public async UniTask<bool> HasAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			if (await HasInternalAssetAsync<T>(path, cancellationToken))
				return true;

			// get on other mods
			var mods = ModManager.Mods;
			for (var i = 0; i < mods.Count; i++) {
				var other = mods[i];
				if (IsOtherLoadedMod(other, path.Namespace) && await other.AssetAPI.HasInternalAssetAsync<T>(path, cancellationToken))
					return true;
			}

			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && await mod.AssetAPI.HasInternalAssetAsync<T>(path, cancellationToken);
		}

		public async UniTask<T> GetAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			// get on Internal mod
			if (await HasInternalAssetAsync<T>(path, cancellationToken))
				return await GetInternalAssetAsync<T>(path, cancellationToken);

			// get from the mod that owns the namespace
			var owner = ModManager.GetMod(path.Namespace);

			// get on other mods
			var mods = ModManager.Mods;
			for (var i = 0; i < mods.Count; i++) {
				var other = mods[i];
				if (IsOtherLoadedMod(other, path.Namespace) && await other.AssetAPI.HasInternalAssetAsync<T>(path, cancellationToken))
					return await other.AssetAPI.GetInternalAssetAsync<T>(path, cancellationToken);
			}

			return owner != null
				? await owner.AssetAPI.GetInternalAssetAsync<T>(path, cancellationToken)
				: null;
		}

		public async UniTask<bool> HasInternalAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			await UniTask.Yield(cancellationToken); // Make it properly async

			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				if (!string.IsNullOrEmpty(HasAssetFromBundle(n, path.Path))) return true;
			}

			return false;
		}

		public async UniTask<T> GetInternalAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				if (!TryGetBundleAsset(n, path.Path, out var found)) continue;

				var request = found.Bundle.LoadAssetAsync<T>(found.Name);
				await request;
				if (request.asset is T asset)
					return asset;

				var symbolicRequest = found.Bundle.LoadAssetAsync<SymbolicAsset>(found.Name);
				await symbolicRequest;
				if (symbolicRequest.asset is SymbolicAsset symbolic)
					return await GetAssetAsync<T>(symbolic.Target);

				return default;
			}

			return default;
		}

		public async UniTask<Scene> LoadWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
			path = AssetAPIExtension.Resolve(path, _mod);
			// load on Internal mod
			if (IsLoadedWorld(path)) return GetWorld(path);

			if (HasInternalWorld(path)) return await LoadInternalWorld(path, mode);

			// load on other mods
			var mods = ModManager.Mods;
			for (var i = 0; i < mods.Count; i++) {
				var other = mods[i];
				if (IsOtherLoadedMod(other, path.Namespace) && other.AssetAPI.HasInternalWorld(path))
					return await other.AssetAPI.LoadInternalWorld(path, mode);
			}

			// load from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			if (mod != null) return await mod.AssetAPI.LoadInternalWorld(path, mode);

			return default;
		}

		public bool HasWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			if (HasInternalWorld(path)) return true;

			// get on other mods
			var mods = ModManager.Mods;
			for (var i = 0; i < mods.Count; i++) {
				var other = mods[i];
				if (IsOtherLoadedMod(other, path.Namespace) && other.AssetAPI.HasInternalWorld(path)) return true;
			}

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && mod.AssetAPI.HasInternalWorld(path);
		}

		public Scene GetWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			// get on Internal mod
			if (IsLoadedInternalWorld(path)) return GetInternalWorld(path);

			// get on other mods
			var mods = ModManager.Mods;
			for (var i = 0; i < mods.Count; i++) {
				var other = mods[i];
				if (IsOtherLoadedMod(other, path.Namespace) && other.AssetAPI.IsLoadedInternalWorld(path))
					return other.AssetAPI.GetInternalWorld(path);
			}

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
			var mods = ModManager.Mods;
			for (var i = 0; i < mods.Count; i++) {
				var other = mods[i];
				if (!IsOtherLoadedMod(other, path.Namespace) || !other.AssetAPI.IsLoadedInternalWorld(path)) continue;

				await other.AssetAPI.UnloadInternalWorld(path);
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
			var mods = ModManager.Mods;
			for (var i = 0; i < mods.Count; i++) {
				var other = mods[i];
				if (IsOtherLoadedMod(other, path.Namespace) && other.AssetAPI.IsLoadedInternalWorld(path))
					return true;
			}

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && mod.AssetAPI.IsLoadedInternalWorld(path);
		}

		public async UniTask<Scene> LoadInternalWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var scene = await LoadWorldFromBundle(n, path.Path, mode);
				if (scene.IsValid()) return scene;
			}

			return default;
		}

		public async UniTask UnloadInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) await UnloadWorldFromBundle(n, path.Path);
		}

		public bool HasInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				if (!string.IsNullOrEmpty(HasWorldFromBundle(n, path.Path))) return true;
			}

			return false;
		}

		public bool IsLoadedInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				if (IsLoadedWorldFromBundle(n, path.Path)) return true;
			}

			return false;
		}

		public Scene GetInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var scene = GetWorldFromBundle(n, path.Path);
				if (scene.IsValid()) return scene;
			}

			return default;
		}


		// Bundle helper methods
		public string GetAssetPathFromBundle(string ns, string name) {
			var root = _assetsPath ??= ResolveAssetsPath();
			if (root.Length == 0) return null;

			return AssetAPIExtension.FormatPath(Path.Combine(root, ns, name));
		}

		private string ResolveAssetsPath()
			=> _mod.Metadata
				?.GetCustom<JObject>("kernel")
				?.GetValue("assets_path")
				?.ToObject<string>() ?? string.Empty;

		public T GetAssetFromBundle<T>(string ns, string name)
			where T : Object {
			if (_bundles == null) return null;

			return TryGetBundleAsset(ns, name, out var found)
				? found.Bundle.LoadAsset<T>(found.Name)
				: null;
		}

		public KeyValuePair<string, string>[] GetAssetNamesFromBundle(string ns) {
			if (_bundles == null) return null;

			var prefix = GetAssetPathFromBundle(ns, "");
			if (string.IsNullOrEmpty(prefix))
				return Array.Empty<KeyValuePair<string, string>>();

			List<KeyValuePair<string, string>> assets = new();
			foreach (var kv in AssetIndex)
				if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
					assets.Add(new KeyValuePair<string, string>(ns, kv.Value.Name[(kv.Value.Name.IndexOf('/') + 1)..]));

			return assets.ToArray();
		}

		public string HasAssetFromBundle(string ns, string name)
			=> TryGetBundleAsset(ns, name, out var found) ? found.Name : null;

		/// <summary>Where a bundle keeps a given asset, so a lookup does not have to scan the bundles.</summary>
		private struct BundleAsset {
			public AssetBundle Bundle;
			public string Name;
		}

		/// <summary>
		/// Built once per registration. <see cref="AssetBundle.GetAllAssetNames"/> and
		/// <see cref="AssetBundle.GetAllScenePaths"/> marshal a brand new array of every name per call, so they
		/// are read once here instead of once per request.
		/// </summary>
		private Dictionary<string, BundleAsset> AssetIndex {
			get {
				if (_assetIndex != null) return _assetIndex;

				var assets = new Dictionary<string, BundleAsset>(StringComparer.Ordinal);
				var scenes = new Dictionary<string, string>(StringComparer.Ordinal);

				if (_bundles != null)
					foreach (var bundle in _bundles) {
						foreach (var name in bundle.GetAllAssetNames()) {
							var key = AssetAPIExtension.FormatPath(name);
							if (!assets.ContainsKey(key))
								assets[key] = new BundleAsset { Bundle = bundle, Name = name };
						}

						foreach (var scene in bundle.GetAllScenePaths()) {
							var key = AssetAPIExtension.FormatPath(scene);
							if (!scenes.ContainsKey(key))
								scenes[key] = scene;
						}
					}

				_sceneIndex = scenes;
				return _assetIndex = assets;
			}
		}

		private Dictionary<string, string> SceneIndex {
			get {
				if (_assetIndex == null)
					_ = AssetIndex;

				return _sceneIndex;
			}
		}

		private bool TryGetBundleAsset(string ns, string name, out BundleAsset asset) {
			asset = default;

			if (_bundles == null) return false;

			var path = GetAssetPathFromBundle(ns, name);
			if (path == null)
				return false;

			if (AssetIndex.TryGetValue(path, out asset))
				return true;

			path = GetAssetPathFromBundle(ns, name + ".asset");

			return path != null
				&& AssetIndex.TryGetValue(path, out asset);
		}

		private string GetScenePathFromBundle(string ns, string name) {
			if (_bundles == null) return null;

			var path = GetAssetPathFromBundle(ns, name);
			if (path == null) return null;

			return SceneIndex.TryGetValue(path, out var scene)
				? scene
				: null;
		}

		public async UniTask<bool> RegisterAssets() {
			if (_bundles != null) return true;

			var kernel = _mod.Metadata.GetCustom<JObject>("kernel");
			if (kernel == null) return false;

			var assets = kernel.GetValue("assets")?.ToObject<JObject[]>();
			if (assets == null) return false;

			_bundles = new List<AssetBundle>();

			foreach (var asset in assets) {
				var path = asset.GetValue("file")?.ToObject<string>();
				if (string.IsNullOrEmpty(path)) continue;

				path = Path.Combine(Application.dataPath, "Nox", path);
				if (!File.Exists(path)) continue;

				var bundle = await AssetBundle.LoadFromFileAsync(path);
				if (!bundle) continue;

				_bundles.Add(bundle);
			}

			_assetIndex = null;
			return true;
		}

		public async UniTask<bool> UnRegisterAssets() {
			if (_bundles == null) return true;
			foreach (var bundle in _bundles) await bundle.UnloadAsync(true);
			_bundles = null;
			_assetIndex = null;
			return true;
		}

		public bool IsLoaded()
			=> _bundles != null;

		public async UniTask<Scene> LoadWorldFromBundle(string ns, string name, LoadSceneMode mode = LoadSceneMode.Single) {
			var path = GetScenePathFromBundle(ns, name);
			if (string.IsNullOrEmpty(path)) return default;

			var scene = SceneManager.GetSceneByPath(path);
			if (scene.isLoaded && scene.IsValid()) return scene;

			await SceneManager.LoadSceneAsync(path, mode);
			return SceneManager.GetSceneByPath(path);
		}

		public async UniTask UnloadWorldFromBundle(string ns, string name) {
			var path = GetScenePathFromBundle(ns, name);
			if (string.IsNullOrEmpty(path)) return;

			var scene = SceneManager.GetSceneByPath(path);
			if (scene.isLoaded) await SceneManager.UnloadSceneAsync(scene);
		}

		public string HasWorldFromBundle(string ns, string name)
			=> GetScenePathFromBundle(ns, name);

		public bool IsLoadedWorldFromBundle(string ns, string name) {
			var path = GetScenePathFromBundle(ns, name);
			return path != null && SceneManager.GetSceneByPath(path).isLoaded;
		}

		public Scene GetWorldFromBundle(string ns, string name) {
			var path = GetScenePathFromBundle(ns, name);
			return string.IsNullOrEmpty(path) ? default : SceneManager.GetSceneByPath(path);
		}
	}

}
