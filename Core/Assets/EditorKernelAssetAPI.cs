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
using UnityEditor;

namespace Nox.ModLoader.Cores.Assets {
	public class EditorKernelAssetAPI : IAssetAPI {
		public EditorKernelAssetAPI(ModLoader.Mods.KernelMod kernelMod)
			=> _kernelMod = kernelMod;

		private readonly ModLoader.Mods.KernelMod _kernelMod;
		private          bool                     _loaded;

		public bool HasAsset<T>(string name) where T : Object
			=> HasAsset<T>(_kernelMod.Metadata.GetId(), name);

		public static string ToRelative(string path) {
			path = Path.GetFullPath(path);

			// Absolute path
			var folders = new[] {
				(Application.dataPath, "Assets"),
				(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages")), "Packages"),
				(Application.streamingAssetsPath, "StreamingAssets"),
				(Application.persistentDataPath, "PersistentDataPath")
			};

			foreach (var folder in folders) {
				if (!path.StartsWith(folder.Item1, StringComparison.OrdinalIgnoreCase)) continue;
				var relativePath = path[folder.Item1.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				return Path.Combine(folder.Item2, relativePath);
			}

			// Not found
			Logger.LogWarning($"Path '{path}' is not in Assets, Packages, StreamingAssets or PersistentDataPath.");
			return path;
		}

		/// <summary>
		/// Get the list of assets for the current mod
		/// </summary>
		/// <returns></returns>
		public KeyValuePair<string, string>[] GetAssetNames()
			=> GetOverrideAssetNames(_kernelMod.Metadata.GetId());

		/// <summary>
		/// Get the list of assets for a mod
		/// </summary>
		/// <param name="ns">id or provides of the mod</param>
		/// <returns>list of entries[namespace, path] of the assets</returns>
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

		/// <summary>
		/// Get the list of assets of the current mod (including assets FOR other mods)
		/// </summary>
		/// <remarks>If the mod is Mod[id=api.nox.world], the assets will be in the format "[Asset folder of api.nox.world]/[namespaces]/[path]"</remarks>
		/// <returns>list of entries[namespace, path] of the assets</returns>
		public KeyValuePair<string, string>[] GetLocalAssetNames()
			=> GetOverrideAssetNames(_kernelMod.Metadata.GetId());

		/// <summary>
		/// Get the list of assets in a specific mod (including assets FOR other mods)
		/// <remarks>e.g. "api.nox.world" -> "[Asset folder of api.nox.world]/[namespaces]/[path]"</remarks>
		/// </summary>
		/// <param name="ns">id or provides of the mod</param>
		/// <returns>list of entries[namespace, path] of the assets</returns>
		public KeyValuePair<string, string>[] GetOverrideAssetNames(string ns) {
			List<KeyValuePair<string, string>> assets = new();

			Logger.LogDebug($"{_kernelMod.GetMetadata().GetId()}:");
			var dirpath = ToRelative(_kernelMod.GetData<string>("assets"));

			var namespaces = Directory.GetDirectories(dirpath);

			foreach (var n in namespaces) {
				var space = Path.GetFileName(n);
				if (string.IsNullOrEmpty(space))
					continue;

				var files = Directory.GetFiles(n, "*.*", SearchOption.AllDirectories);
				foreach (var file in files) {
					var path = Path.GetRelativePath(n, file);
					path = FormatPath(Path.Combine(space, path));
					if (path.EndsWith(".meta")) continue;
					assets.Add(new KeyValuePair<string, string>(space, path[(path.IndexOf('/') + 1)..]));
				}
			}

			return assets.ToArray();
		}

		public string FormatPath(string path)
			=> path.Replace('\\', '/').ToLower();


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
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, name));
				if (File.Exists(dirpath))
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
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, name));
				var asset   = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(dirpath);
				if (asset) return asset;
			}

			return default;
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
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, name));
				if (File.Exists(dirpath))
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
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, name));
				var asset   = AssetDatabase.LoadAssetAtPath<T>(dirpath);
				if (asset) return asset;
			}

			return default;
		}

		// World methods (existing async implementations)
		public async UniTask<Scene> LoadWorld(string name, LoadSceneMode mode = LoadSceneMode.Single)
			=> await LoadWorld(_kernelMod.Metadata.GetId(), name, mode);

		public async UniTask<Scene> LoadWorld(string ns, string name, LoadSceneMode mode = LoadSceneMode.Single) {
			// load on override mod
			if (IsLoadedWorld(ns, name))
				return GetWorld(ns, name);

			if (HasOverrideWorld(ns, name))
				return await LoadOverrideWorld(ns, name, mode);

			// load on other mods
			foreach (var m in ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))
				         .Where(m => m.AssetAPI.HasOverrideWorld(ns, name)))
				return await m.AssetAPI.LoadOverrideWorld(ns, name, mode);

			// load from the initial mod
			var mod = ModManager.GetMod(ns);
			if (mod != null)
				return await mod.AssetAPI.LoadLocalWorld(name, mode);

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
			// unload on override mod
			if (IsLoadedOverrideWorld(ns, name)) {
				await UnloadOverrideWorld(ns, name);
				return;
			}

			// unload on other mods
			foreach (var m in ModManager.Mods.Where(m => m != _kernelMod && m.IsLoaded() && !m.GetMetadata().Match(ns))
				         .Where(m => m.AssetAPI.IsLoadedOverrideWorld(ns, name))) {
				await m.AssetAPI.UnloadOverrideWorld(ns, name);
				return;
			}

			// unload from the initial mod
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
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), ns, name));
				var scenes = AssetDatabase.FindAssets("t:Scene")
					.Select(AssetDatabase.GUIDToAssetPath)
					.Where(p => FormatPath(p) == FormatPath(dirpath))
					.ToArray();
				if (scenes.Length == 0) continue;
				var scenePath = scenes[0];
				var scene     = SceneManager.GetSceneByPath(scenePath);
				if (!scene.isLoaded) await SceneManager.LoadSceneAsync(scenePath, mode);
				scene = SceneManager.GetSceneByPath(scenePath);
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

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), ns, name));
				var scenes = AssetDatabase.FindAssets("t:Scene")
					.Select(AssetDatabase.GUIDToAssetPath)
					.Where(p => FormatPath(p) == FormatPath(dirpath))
					.ToArray();
				if (scenes.Length == 0) continue;
				var scenePath = scenes[0];
				var scene     = SceneManager.GetSceneByPath(scenePath);
				if (scene.isLoaded) await SceneManager.UnloadSceneAsync(scene);
				return;
			}
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
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), ns, name));

				var scenes = AssetDatabase.FindAssets("t:Scene")
					.Select(AssetDatabase.GUIDToAssetPath)
					.Where(p => FormatPath(p) == FormatPath(dirpath))
					.ToArray();
				if (scenes.Length != 0)
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
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, name));

				var scenes = AssetDatabase.FindAssets("t:Scene")
					.Select(AssetDatabase.GUIDToAssetPath)
					.Where(p => FormatPath(p) == FormatPath(dirpath))
					.ToArray();
				if (scenes.Length == 0) continue;
				var scenePath = scenes[0];
				var scene     = SceneManager.GetSceneByPath(scenePath);
				return scene.isLoaded;
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
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, name));

				var scenes = AssetDatabase.FindAssets("t:Scene")
					.Select(AssetDatabase.GUIDToAssetPath)
					.Where(p => FormatPath(p) == FormatPath(dirpath))
					.ToArray();
				if (scenes.Length == 0) continue;
				var scenePath = scenes[0];
				var scene     = SceneManager.GetSceneByPath(scenePath);
				return scene.isLoaded ? scene : default;
			}

			return default;
		}

		public bool HasLocalWorld(string name)
			=> HasOverrideWorld(_kernelMod.Metadata.GetId(), name);

		public bool IsLoadedLocalWorld(string name)
			=> IsLoadedOverrideWorld(_kernelMod.Metadata.GetId(), name);

		public Scene GetLocalWorld(string name)
			=> GetOverrideWorld(_kernelMod.Metadata.GetId(), name);

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
