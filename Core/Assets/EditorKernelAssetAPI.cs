#if UNITY_EDITOR
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Logger = Nox.CCK.Utils.Logger;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using Nox.CCK;
using System.Collections.Concurrent;

namespace Nox.ModLoader.Cores.Assets {

	public class EditorKernelAssetAPI : IAssetAPI {
		public EditorKernelAssetAPI(ModLoader.Mods.KernelMod kernelMod)
			=> _mod = kernelMod;

		private readonly ModLoader.Mods.KernelMod _mod;
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
		/// Memoized probes, keyed by path then by type name. Each probe is up to two
		/// <see cref="AssetDatabase.LoadAssetAtPath{T}"/> calls on the main thread, and a profiler
		/// capture showed 13.5k <c>ReadObjectFromSerializedFile</c> and 3.8k
		/// <c>PackageInfo.GetPackageByAssetPath</c> triggered by resolving the same paths over and over.
		/// <para>
		/// Two levels of keys: a change only reports the path it touched, and dropping every type of that path from
		/// a flat dictionary would walk the whole cache on each imported file.
		/// </para>
		/// </summary>
		private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, AssetProbe>> Probes = new();

		[InitializeOnLoadMethod]
		private static void TrackAssetDatabase() {
			EditorApplication.projectChanged -= OnProjectChanged;
			EditorApplication.projectChanged += OnProjectChanged;
			_root = Application.dataPath;
		}

		/// <summary>Root the caches were built against.</summary>
		private static string _root;

		/// <summary>
		/// A project that moved (another <see cref="Application.dataPath"/>) makes every absolute-to-relative answer
		/// and every probe wrong at once, so that is the only case that clears them: an import, a move or a delete
		/// only drops the paths it touched, through <see cref="EditorAssetWatcher"/>.
		/// </summary>
		private static void OnProjectChanged() {
			var root = Application.dataPath;

			if (root == _root)
				return;

			_root = root;
			ClearProbes();
			RelativePaths.Clear();
			_folders = null;
		}

		/// <summary>Beyond that many changed folders, dropping the whole cache is cheaper than scanning it.</summary>
		private const int MAX_SUBTREE_SCAN = 32;

		private static void ClearProbes() {
			Probes.Clear();
			_scenes = null;
		}

		/// <summary>
		/// Drops what is cached about the paths a change touched, in one pass: <paramref name="paths"/> are the
		/// files, <paramref name="subtrees"/> the folders, whose whole content is stale. Everything is batched so a
		/// reimport of a thousand files costs a thousand dictionary removals and no scan at all.
		/// </summary>
		internal static void Invalidate(HashSet<string> paths, HashSet<string> subtrees) {
			foreach (var path in paths) {
				Probes.TryRemove(path, out _);

				// An indirection is probed through `<path>.asset` but keys the target without that suffix.
				if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
					Probes.TryRemove(path[..^6], out _);

				if (_scenes != null && path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
					_scenes.Remove(FormatPath(path));
			}

			if (subtrees.Count == 0)
				return;

			if (subtrees.Count > MAX_SUBTREE_SCAN) {
				ClearProbes();
				return;
			}

			var prefixes          = new string[subtrees.Count];
			var formattedPrefixes = new string[subtrees.Count];
			var index             = 0;

			foreach (var subtree in subtrees) {
				prefixes[index]          = subtree + "/";
				formattedPrefixes[index] = FormatPath(prefixes[index]);
				index++;
			}

			foreach (var key in Probes.Keys)
			foreach (var prefix in prefixes)
				if (key.StartsWith(prefix, StringComparison.Ordinal)) {
					Probes.TryRemove(key, out _);
					break;
				}

			if (_scenes == null)
				return;

			var stale = new List<string>();

			foreach (var key in _scenes.Keys)
				foreach (var prefix in formattedPrefixes)
					if (key.StartsWith(prefix, StringComparison.Ordinal)) {
						stale.Add(key);
						break;
					}

			foreach (var key in stale)
				_scenes.Remove(key);
		}

		/// <summary>Adds a scene Unity just read again, so a project that had its list built keeps it.</summary>
		internal static void TrackScene(string path) {
			if (_scenes == null || string.IsNullOrEmpty(path))
				return;

			if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
				_scenes[FormatPath(path)] = path;
		}

		/// <summary>
		/// Resolves <paramref name="dirpath"/> through the symbolic-asset indirection, reusing the
		/// previous answer; a cached reference Unity has since destroyed is re-resolved. Symbolic
		/// targets are never cached as assets: their resolution walks the other mods' APIs.
		/// </summary>
		private static AssetProbe ProbeAsset<T>(string dirpath)
			where T : Object {
			var type = typeof(T).Name;

			var byType = Probes.GetOrAdd(dirpath, static _ => new ConcurrentDictionary<string, AssetProbe>());

			if (byType.TryGetValue(type, out var cached)) {
				if (cached.HasSymbolic || !cached.HasAsset || cached.Asset)
					return cached;

				byType.TryRemove(type, out _);
			}

			var probe = new AssetProbe();

			if (Exists(dirpath + ".asset")) {
				var symbolic = AssetDatabase.LoadAssetAtPath<SymbolicAsset>(dirpath + ".asset");

				if (symbolic) {
					probe.HasSymbolic    = true;
					probe.SymbolicTarget = symbolic.Target;
				}
			}

			if (!probe.HasSymbolic && Exists(dirpath)) {
				probe.Asset    = AssetDatabase.LoadAssetAtPath<T>(dirpath);
				probe.HasAsset = probe.Asset;
			}

			byType[type] = probe;
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
			=> $"{GetType().Name}[Id={_mod.Metadata.Id}, Version={_mod.Metadata.Version}]";

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

		/// <summary>Absolute path of a project-relative one, or <c>null</c> when it is under none of the roots.</summary>
		private static string ToAbsolute(string path) {
			foreach (var (abs, rel) in GetFolders()) {
				if (path.Length <= rel.Length)
					continue;

				if (string.Compare(path, 0, rel, 0, rel.Length, StringComparison.OrdinalIgnoreCase) != 0)
					continue;

				var separator = path[rel.Length];

				if (separator != '/' && separator != '\\')
					continue;

				return Path.Combine(abs, path[(rel.Length + 1)..]);
			}

			return null;
		}

		/// <summary>
		/// Whether a file sits at that path. Most probes look for an asset the namespace walk will never find, and an
		/// AssetDatabase lookup that answers "nothing" still resolves the path through the package list while a file
		/// system stat does not. An unknown root falls back to the AssetDatabase so nothing that resolved stops.
		/// </summary>
		private static bool Exists(string path) {
			var absolute = ToAbsolute(path);
			return absolute == null || File.Exists(absolute);
		}

		public static string FormatPath(string path)
			=> AssetAPIExtension.FormatPath(path);

		/// <summary>
		/// Scenes of the project, keyed by their formatted path. <see cref="AssetDatabase.FindAssets"/> scans the whole
		/// database and every world lookup paid for it once per namespace and per call, so the list is read once and
		/// kept up to date by <see cref="EditorAssetWatcher"/>.
		/// </summary>
		private static Dictionary<string, string> _scenes;

		private static Dictionary<string, string> Scenes {
			get {
				if (_scenes != null)
					return _scenes;

				var scenes = new Dictionary<string, string>(StringComparer.Ordinal);

				foreach (var guid in AssetDatabase.FindAssets("t:Scene")) {
					var path = AssetDatabase.GUIDToAssetPath(guid);

					if (!string.IsNullOrEmpty(path))
						scenes[FormatPath(path)] = path;
				}

				return _scenes = scenes;
			}
		}

		/// <summary>
		/// Resolves the asset path of the first scene matching <paramref name="dirpath"/>.
		/// Returns <c>null</c> when no scene asset matches.
		/// </summary>
		private static string FindScenePath(string dirpath)
			=> Scenes.TryGetValue(FormatPath(dirpath), out var path)
				? path
				: null;

		/// <summary>
		/// Maps a runtime <see cref="LoadSceneMode"/> to its editor equivalent.
		/// </summary>
		private static OpenSceneMode ToOpenSceneMode(LoadSceneMode mode)
			=> mode == LoadSceneMode.Additive ? OpenSceneMode.Additive : OpenSceneMode.Single;


		public bool HasAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			if (HasInternalAsset<T>(path))
				return true;

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace))
				if (m.AssetAPI.HasInternalAsset<T>(path))
					return true;

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && mod.AssetAPI.HasInternalAsset<T>(path);
		}

		public T GetAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			// get on Internal mod
			var result = GetInternalAsset<T>(path);
			if (result)
				return result;

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)) {
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
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_mod.GetData<string>("assets"), n, path.Path));
				var probe   = ProbeAsset<T>(dirpath);
				if (probe.HasSymbolic) return HasAsset<T>(probe.SymbolicTarget);
				if (probe.HasAsset) return true;
			}

			return false;
		}

		public T GetInternalAsset<T>(ResourceIdentifier path)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_mod.GetData<string>("assets"), n, path.Path));
				var probe   = ProbeAsset<T>(dirpath);
				if (probe.HasSymbolic) return GetAsset<T>(probe.SymbolicTarget);
				if (probe.HasAsset) return (T)probe.Asset;
			}

			return default;
		}

		public async UniTask<bool> HasAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			if (await HasInternalAssetAsync<T>(path, cancellationToken))
				return true;

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)) {
				if (await m.AssetAPI.HasInternalAssetAsync<T>(path, cancellationToken))
					return true;
			}

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && await mod.AssetAPI.HasInternalAssetAsync<T>(path, cancellationToken);
		}

		public async UniTask<T> GetAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			// get on Internal mod
			var result = await GetInternalAssetAsync<T>(path, cancellationToken);
			if (result)
				return result;

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)) {
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
			path = AssetAPIExtension.Resolve(path, _mod);
			await UniTask.Yield(cancellationToken); // Make it properly async

			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_mod.GetData<string>("assets"), n, path.Path));
				var probe   = ProbeAsset<T>(dirpath);
				if (probe.HasSymbolic || probe.HasAsset) return true;
			}

			return false;
		}

		public async UniTask<T> GetInternalAssetAsync<T>(ResourceIdentifier path, CancellationToken cancellationToken = default)
			where T : Object {
			path = AssetAPIExtension.Resolve(path, _mod);
			await UniTask.Yield(cancellationToken); // Make it properly async

			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_mod.GetData<string>("assets"), n, path.Path));
				var probe   = ProbeAsset<T>(dirpath);
				if (probe.HasSymbolic) return await GetAssetAsync<T>(probe.SymbolicTarget, cancellationToken);
				if (probe.HasAsset) return (T)probe.Asset;
			}

			return default;
		}

		public async UniTask<Scene> LoadWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
			path = AssetAPIExtension.Resolve(path, _mod);
			// load on Internal mod
			if (IsLoadedWorld(path))
				return GetWorld(path);

			if (HasInternalWorld(path))
				return await LoadInternalWorld(path, mode);

			// load on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
				.Where(m => m.AssetAPI.HasInternalWorld(path)))
				return await m.AssetAPI.LoadInternalWorld(path, mode);

			// load from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			if (mod != null)
				return await mod.AssetAPI.LoadInternalWorld(path, mode);

			return default;
		}

		public bool HasWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			if (HasInternalWorld(path))
				return true;

			// get on other mods
			if (AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
				.Any(m => m.AssetAPI.HasInternalWorld(path)))
				return true;

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && mod.AssetAPI.HasInternalWorld(path);
		}

		public Scene GetWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			// get on Internal mod
			if (IsLoadedInternalWorld(path))
				return GetInternalWorld(path);

			// get on other mods
			foreach (var m in AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
				.Where(m => m.AssetAPI.IsLoadedInternalWorld(path)))
				return m.AssetAPI.GetInternalWorld(path);

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
			if (mod != null)
				await mod.AssetAPI.UnloadInternalWorld(path);
		}

		public bool IsLoadedWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			if (IsLoadedInternalWorld(path))
				return true;

			// get on other mods
			if (AssetAPIExtension.GetOtherLoadedMods(_mod, path.Namespace)
				.Any(m => m.AssetAPI.IsLoadedInternalWorld(path)))
				return true;

			// get from the initial mod
			var mod = ModManager.GetMod(path.Namespace);
			return mod != null && mod.AssetAPI.IsLoadedInternalWorld(path);
		}

		public async UniTask<Scene> LoadInternalWorld(ResourceIdentifier path, LoadSceneMode mode = LoadSceneMode.Single) {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath   = ToRelative(Path.Combine(_mod.GetData<string>("assets"), n, path.Path));
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
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath   = ToRelative(Path.Combine(_mod.GetData<string>("assets"), n, path.Path));
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
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath = ToRelative(Path.Combine(_mod.GetData<string>("assets"), n, path.Path));
				if (FindScenePath(dirpath) != null)
					return true;
			}

			return false;
		}

		public bool IsLoadedInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath   = ToRelative(Path.Combine(_mod.GetData<string>("assets"), n, path.Path));
				var scenePath = FindScenePath(dirpath);
				if (scenePath == null)
					continue;
				return SceneManager.GetSceneByPath(scenePath).isLoaded;
			}

			return false;
		}

		public Scene GetInternalWorld(ResourceIdentifier path) {
			path = AssetAPIExtension.Resolve(path, _mod);
			var namespaces = AssetAPIExtension.GetNamespaces(path);

			foreach (var n in namespaces) {
				var dirpath   = ToRelative(Path.Combine(_mod.GetData<string>("assets"), n, path.Path));
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

	/// <summary>
	/// Keeps the caches of <see cref="EditorKernelAssetAPI"/> in step with the asset database: an import, a move or
	/// a delete only drops the paths it touched. Clearing everything on <c>EditorApplication.projectChanged</c>
	/// made every save of any file throw away the whole index, so the lookups that followed reloaded the project.
	/// </summary>
	internal sealed class EditorAssetWatcher : AssetPostprocessor {
		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
			string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload) {
			// Every cache is static: a domain reload already emptied them.
			if (didDomainReload)
				return;

			var paths    = new HashSet<string>(StringComparer.Ordinal);
			var subtrees = new HashSet<string>(StringComparer.Ordinal);

			Collect(paths, subtrees, importedAssets, null);
			Collect(paths, subtrees, movedAssets, null);
			Collect(paths, subtrees, deletedAssets, null);
			Collect(paths, subtrees, movedFromAssetPaths, movedAssets);

			EditorKernelAssetAPI.Invalidate(paths, subtrees);

			foreach (var path in importedAssets)
				EditorKernelAssetAPI.TrackScene(path);

			foreach (var path in movedAssets)
				EditorKernelAssetAPI.TrackScene(path);
		}

		/// <summary>
		/// Sorts changed paths between files and folders. A path is a folder when it carries no extension, or when
		/// it is the source of a move that landed on a folder (the folder name itself may contain a dot).
		/// </summary>
		private static void Collect(HashSet<string> paths, HashSet<string> subtrees, string[] changed,
			string[] movedTo) {
			if (changed == null)
				return;

			for (var i = 0; i < changed.Length; i++) {
				var path = changed[i];

				if (string.IsNullOrEmpty(path))
					continue;

				path = path.Replace('\\', '/');
				paths.Add(path);

				var isFolder = Path.GetExtension(path).Length == 0;

				if (!isFolder && movedTo != null && i < movedTo.Length)
					isFolder = AssetDatabase.IsValidFolder(movedTo[i]);

				if (isFolder)
					subtrees.Add(path);
			}
		}
	}
}
#endif
