#if UNITY_EDITOR
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Logger = Nox.CCK.Utils.Logger;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using Nox.CCK;
using System.Collections.Concurrent;

namespace Nox.ModLoader.Cores.Assets {

	public class EditorKernelAssetAPI : IAssetAPI {
		public EditorKernelAssetAPI(ModLoader.Mods.KernelMod kernelMod)
			=> _kernelMod = kernelMod;

		private readonly ModLoader.Mods.KernelMod _kernelMod;
		private bool _loaded;

		/// <summary>
		/// Outcome of probing one asset path inside a mod's assets folder.
		/// </summary>
		private sealed class AssetProbe {
			/// <summary>A <see cref="SymbolicAsset"/> lives at the path (an indirection).</summary>
			public bool HasSymbolic;

			/// <summary>Target of that symbolic asset (possibly <c>null</c>).</summary>
			public string SymbolicTarget;

			/// <summary>The asset itself when the path is not an indirection.</summary>
			public Object Asset;

			/// <summary>Whether <see cref="Asset"/> holds something.</summary>
			public bool HasAsset;
		}

		/// <summary>
		/// Memoized probes, keyed by path and type. Each probe is up to two
		/// <see cref="AssetDatabase.LoadAssetAtPath{T}"/> calls on the main thread, and a profiler
		/// capture showed 13.5k <c>ReadObjectFromSerializedFile</c> and 3.8k
		/// <c>PackageInfo.GetPackageByAssetPath</c> triggered by resolving the same paths over and over.
		/// </summary>
		private static readonly ConcurrentDictionary<string, AssetProbe> Probes = new();

		[InitializeOnLoadMethod]
		private static void TrackAssetDatabase() {
			EditorApplication.projectChanged -= ClearProbes;
			EditorApplication.projectChanged += ClearProbes;
		}

		private static void ClearProbes() {
			Probes.Clear();
			RelativePaths.Clear();
			_folders = null;
		}

		/// <summary>
		/// Resolves <paramref name="dirpath"/> through the symbolic-asset indirection, reusing the
		/// previous answer; a cached reference Unity has since destroyed is re-resolved. Symbolic
		/// targets are never cached as assets: their resolution walks the other mods' APIs.
		/// </summary>
		private static AssetProbe ProbeAsset<T>(string dirpath)
			where T : Object {
			var key = dirpath + "|" + typeof(T).Name;
			if (Probes.TryGetValue(key, out var cached)) {
				if (cached.HasSymbolic || !cached.HasAsset || cached.Asset)
					return cached;

				Probes.TryRemove(key, out _);
			}

			var probe    = new AssetProbe();
			var symbolic = AssetDatabase.LoadAssetAtPath<SymbolicAsset>(dirpath + ".asset");
			if (symbolic) {
				probe.HasSymbolic    = true;
				probe.SymbolicTarget = symbolic.Target;
			} else {
				probe.Asset    = AssetDatabase.LoadAssetAtPath<T>(dirpath);
				probe.HasAsset = probe.Asset;
			}

			Probes[key] = probe;
			return probe;
		}

		/// <summary>
		/// Memoized <see cref="ToRelative"/> answers. The conversion canonicalizes the absolute path
		/// (which splits it) and re-combines it, and every asset lookup repeated it on the same path:
		/// a capture showed 189 <c>SplitKeepEmptyEntries</c> over 200 frames originating here.
		/// Cleared with the probes when the project changes, so a moved project cannot serve stale paths.
		/// </summary>
		private static readonly ConcurrentDictionary<string, string> RelativePaths = new();

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
			if (RelativePaths.TryGetValue(path, out var memoized))
				return memoized;

			return RelativePaths[path] = ResolveRelative(path);
		}

		private static string ResolveRelative(string path) {
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
				var probe   = ProbeAsset<T>(dirpath);
				if (probe.HasSymbolic) return HasAsset<T>(probe.SymbolicTarget);
				if (probe.HasAsset) return true;
			}

			return false;
		}

		public T GetInternalAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
				var probe   = ProbeAsset<T>(dirpath);
				if (probe.HasSymbolic) return GetAsset<T>(probe.SymbolicTarget);
				if (probe.HasAsset) return (T)probe.Asset;
			}

			return default;
		}

		public async UniTask<bool> HasAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			if (await HasInternalAssetAsync<T>(path, cancellationToken))
				return true;

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)) {
				if (await m.AssetAPI.HasInternalAssetAsync<T>(path, cancellationToken))
					return true;
			}

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && await mod.AssetAPI.HasInternalAssetAsync<T>(path, cancellationToken);
		}

		public async UniTask<T> GetAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			// get on Internal mod
			var result = await GetInternalAssetAsync<T>(path, cancellationToken);
			if (result)
				return result;

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_kernelMod, path.Namespace)) {
				result = await m.AssetAPI.GetInternalAssetAsync<T>(path, cancellationToken);
				if (result)
					return result;
			}

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null ? await mod.AssetAPI.GetInternalAssetAsync<T>(path, cancellationToken) : null;
		}

		public async UniTask<bool> HasInternalAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			await UniTask.Yield(cancellationToken); // Make it properly async

			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
				var probe   = ProbeAsset<T>(dirpath);
				if (probe.HasSymbolic || probe.HasAsset) return true;
			}

			return false;
		}

		public async UniTask<T> GetInternalAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _kernelMod);
			await UniTask.Yield(cancellationToken); // Make it properly async

			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_kernelMod.GetData<string>("assets"), n, path.Path));
				var probe   = ProbeAsset<T>(dirpath);
				if (probe.HasSymbolic) return await GetAssetAsync<T>(probe.SymbolicTarget, cancellationToken);
				if (probe.HasAsset) return (T)probe.Asset;
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