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
using UnityEditor.SceneManagement;
using Nox.CCK;

namespace Nox.ModLoader.Cores.Assets {

	public class EditorKernelAssetAPI : IAssetAPI {
		public EditorKernelAssetAPI(ModLoader.Mods.KernelMod kernelMod)
			=> _kernelMod = kernelMod;

		private readonly ModLoader.Mods.KernelMod _kernelMod;
		private bool _loaded;

		private static (string abs, string rel)[] _folders;
		public override string ToString() 
			=> $"{GetType().Name}[Id={_kernelMod.Metadata.GetId()}, Version={_kernelMod.Metadata.GetVersion()}]";

		private static (string abs, string rel)[] GetFolders() {
			if (_folders != null)
				return _folders;
			return _folders = new[] {
				(Application.dataPath, "Assets"),
				(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages")), "Packages"),
				(Application.streamingAssetsPath, "StreamingAssets"),
				(Application.persistentDataPath, "PersistentDataPath")
			};
		}

		public static string ToRelative(string path) {
			path = Path.GetFullPath(path);

			foreach (var folder in GetFolders()) {
				if (!path.StartsWith(folder.abs, StringComparison.OrdinalIgnoreCase))
					continue;
				var relativePath = path[folder.abs.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				return Path.Combine(folder.rel, relativePath);
			}

			Logger.LogWarning($"Path '{path}' is not in Assets, Packages, StreamingAssets or PersistentDataPath.");
			return path;
		}
		public static string FormatPath(string path)
			=> AssetAPIExtension.FormatPath(path);

		/// <summary>
		/// Resolves the asset path of the first scene matching <paramref name="dirpath"/>.
		/// Returns <c>null</c> when no scene asset matches.
		/// </summary>
		private static string FindScenePath(string dirpath) {
			var formatted = FormatPath(dirpath);
			return AssetDatabase.FindAssets("t:Scene")
				.Select(AssetDatabase.GUIDToAssetPath)
				.FirstOrDefault(p => FormatPath(p) == formatted);
		}

		/// <summary>
		/// Maps a runtime <see cref="LoadSceneMode"/> to its editor equivalent.
		/// </summary>
		private static OpenSceneMode ToOpenSceneMode(LoadSceneMode mode)
			=> mode == LoadSceneMode.Additive ? OpenSceneMode.Additive : OpenSceneMode.Single;


		public bool HasAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			if (HasInternalAsset<T>(path))
				return true;

			// get on other mods
			if (AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
				.Any(m => m.AssetAPI.HasInternalAsset<T>(path)))
				return true;

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && mod.AssetAPI.HasInternalAsset<T>(path);
		}

		public T GetAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			// get on Internal mod
			var result = GetInternalAsset<T>(path);
			if (result)
				return result;

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)) {
				result = m.AssetAPI.GetInternalAsset<T>(path);
				if (result)
					return result;
			}

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
				var symbolic = AssetDatabase.LoadAssetAtPath<SymbolicAsset>(dirpath + ".asset");
				if (symbolic) return HasAsset<T>(symbolic.Target);
				if (AssetDatabase.LoadAssetAtPath<T>(dirpath))
					return true;
			}

			return false;
		}

		public T GetInternalAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
				var symbolic = AssetDatabase.LoadAssetAtPath<SymbolicAsset>(dirpath + ".asset");
				if (symbolic) return GetAsset<T>(symbolic.Target);
				var asset = AssetDatabase.LoadAssetAtPath<T>(dirpath);
				if (asset)
					return asset;
			}

			return default;
		}

		public async UniTask<bool> HasAssetAsync<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			if (await HasInternalAssetAsync<T>(path))
				return true;

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)) {
				if (await m.AssetAPI.HasInternalAssetAsync<T>(path))
					return true;
			}

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && await mod.AssetAPI.HasInternalAssetAsync<T>(path);
		}

		public async UniTask<T> GetAssetAsync<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			// get on Internal mod
			var result = await GetInternalAssetAsync<T>(path);
			if (result)
				return result;

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)) {
				result = await m.AssetAPI.GetInternalAssetAsync<T>(path);
				if (result)
					return result;
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
				if (AssetDatabase.LoadAssetAtPath<SymbolicAsset>(dirpath + ".asset")) return true;
				if (AssetDatabase.LoadAssetAtPath<T>(dirpath))
					return true;
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
				var symbolic = AssetDatabase.LoadAssetAtPath<SymbolicAsset>(dirpath + ".asset");
				if (symbolic) return await GetAssetAsync<T>(symbolic.Target);
				var asset = AssetDatabase.LoadAssetAtPath<T>(dirpath);
				if (asset)
					return asset;
			}

			return default;
		}

		public async UniTask<Scene> LoadWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			// load on Internal mod
			if (IsLoadedWorld(path))
				return GetWorld(path);

			if (HasInternalWorld(path))
				return await LoadInternalWorld(path, mode);

			// load on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
				.Where(m => m.AssetAPI.HasInternalWorld(path)))
				return await m.AssetAPI.LoadInternalWorld(path, mode);

			// load from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			if (mod != null)
				return await mod.AssetAPI.LoadInternalWorld(path, mode);

			return default;
		}

		public bool HasWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			if (HasInternalWorld(path))
				return true;

			// get on other mods
			if (AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
				.Any(m => m.AssetAPI.HasInternalWorld(path)))
				return true;

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && mod.AssetAPI.HasInternalWorld(path);
		}

		public Scene GetWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			// get on Internal mod
			if (IsLoadedInternalWorld(path))
				return GetInternalWorld(path);

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
				.Where(m => m.AssetAPI.IsLoadedInternalWorld(path)))
				return m.AssetAPI.GetInternalWorld(path);

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
			if (mod != null)
				await mod.AssetAPI.UnloadInternalWorld(path);
		}

		public bool IsLoadedWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			if (IsLoadedInternalWorld(path))
				return true;

			// get on other mods
			if (AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)
				.Any(m => m.AssetAPI.IsLoadedInternalWorld(path)))
				return true;

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && mod.AssetAPI.IsLoadedInternalWorld(path);
		}

		public async UniTask<Scene> LoadInternalWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath   = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
				var scenePath = FindScenePath(dirpath);
				if (scenePath == null)
					continue;

				var scene = SceneManager.GetSceneByPath(scenePath);
				if (scene.IsValid() && scene.isLoaded)
					return scene;

				await UniTask.Yield(); // Make it properly async

				// SceneManager.LoadSceneAsync is unusable here: it requires the scene to be
				// registered in the build settings, which package/mod scenes never are. The
				// editor APIs below load a scene straight from its asset path instead.
				if (Application.isPlaying) {
					// OpenScene is rejected during play mode; LoadSceneInPlayMode is its
					// play-mode counterpart and also ignores the build settings.
					scene = EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(mode));

					// The load is deferred to the next frame, so wait for it to complete
					// before handing the scene back to the caller.
					if (scene.IsValid() && !scene.isLoaded)
						await UniTask.WaitUntil(() => !scene.IsValid() || scene.isLoaded);
				} else {
					scene = EditorSceneManager.OpenScene(scenePath, ToOpenSceneMode(mode));
				}

				// Guard against an invalid handle so callers never dereference a broken scene.
				return scene.IsValid() ? scene : default;
			}

			return default;
		}

		public async UniTask UnloadInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath   = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
				var scenePath = FindScenePath(dirpath);
				if (scenePath == null)
					continue;

				await UniTask.Yield(); // Make it properly async

				var scene = SceneManager.GetSceneByPath(scenePath);
				if (!scene.IsValid() || !scene.isLoaded)
					return;

				// Mirrors LoadInternalWorld: CloseScene is rejected during play mode.
				if (Application.isPlaying)
					await SceneManager.UnloadSceneAsync(scene);
				else
					EditorSceneManager.CloseScene(scene, true);
				return;
			}
		}

		public bool HasInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
				if (FindScenePath(dirpath) != null)
					return true;
			}

			return false;
		}

		public bool IsLoadedInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath   = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
				var scenePath = FindScenePath(dirpath);
				if (scenePath == null)
					continue;
				return SceneManager.GetSceneByPath(scenePath).isLoaded;
			}

			return false;
		}

		public Scene GetInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath   = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
				var scenePath = FindScenePath(dirpath);
				if (scenePath == null)
					continue;
				var scene = SceneManager.GetSceneByPath(scenePath);
				return scene.isLoaded ? scene : default;
			}

			return default;
		}

		// Missing interface methods
		public bool IsLoaded()
			=> _loaded;

		public UniTask<bool> RegisterAssets() {
			_loaded = true;
			return UniTask.FromResult(true);
		}

		public UniTask<bool> UnRegisterAssets() {
			_loaded = false;
			return UniTask.FromResult(true);
		}
	}

}
#endif