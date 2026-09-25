using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Nox.CCK.Utils;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Core.Libs {
	/// <summary>
	/// Global native library manager. Tracks which mods loaded which libraries
	/// and physically loads/unloads them via LoadLibrary / dlopen.
	/// <para>
	/// Each <c>LibEntry</c> stores the set of mod IDs that requested it.
	/// The library is only freed when the last mod calls <c>Unload</c>.</para>
	/// </summary>
	internal static class LibManager {
		private struct LibEntry {
			public IntPtr Handle;
			/// <summary>Path resolved by <see cref="Load"/>, used to load the library lazily when needed.</summary>
			public string Path;
			/// <summary>True when <see cref="Handle"/> comes from our own load, so we must release it.</summary>
			public bool Owned;
			/// <summary>True once loading failed, so lookups do not retry the load for every export.</summary>
			public bool Failed;
			public HashSet<string> ModIds;
		}

		private static readonly Dictionary<string, LibEntry> _libCache = new();
		private static readonly object _lock = new();

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
		private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW")]
		private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

		[DllImport("kernel32", SetLastError = true)]
		private static extern bool FreeLibrary(IntPtr hModule);

		/// <summary>Searches the loaded DLL's own directory for its dependencies.</summary>
		private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;
#endif
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
		[DllImport("libdl.so.2", SetLastError = true)]
		private static extern IntPtr dlopen(string filename, int flags);
		private const int RTLD_NOW = 2;

		[DllImport("libdl.so.2", SetLastError = true)]
		private static extern int dlclose(IntPtr handle);

		[DllImport("libdl.so.2", SetLastError = true)]
		private static extern IntPtr dlsym(IntPtr handle, string symbol);

		[DllImport("libdl.so.2", SetLastError = true)]
		private static extern IntPtr dlerror();
#endif
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
		[DllImport("libdl", SetLastError = true)]
		private static extern IntPtr dlopen(string filename, int flags);
		private const int RTLD_NOW = 2;

		[DllImport("libdl", SetLastError = true)]
		private static extern int dlclose(IntPtr handle);

		[DllImport("libdl", SetLastError = true)]
		private static extern IntPtr dlsym(IntPtr handle, string symbol);

		[DllImport("libdl", SetLastError = true)]
		private static extern IntPtr dlerror();
#endif

		/// <summary>
		/// Load a native library from the first folder where it is found.
		/// Adds <paramref name="modId"/> to the reference set.
		/// Returns the total number of mods now referencing this library.
		/// </summary>
		public static int Load(string name, string modId, string[] searchFolders) {
			lock (_lock) {
				// Already loaded → add mod to ref set
				if (_libCache.TryGetValue(name, out var entry)) {
					entry.ModIds.Add(modId);
					_libCache[name] = entry;
					return entry.ModIds.Count;
				}

				// Find the library
				var ext = GetExtension();
				var filename = name + ext;
				string path = null;

				foreach (var folder in searchFolders) {
					var candidate = Path.Combine(folder, filename);
					if (File.Exists(candidate)) {
						path = candidate;
						break;
					}
				}

				if (path == null)
					throw new DllNotFoundException(
						$"Could not find native library '{name}' in search folders.");

				// Physically load — set the DLL directory first so dependencies resolve
				var fullPath = Path.GetFullPath(path);
				var dir = Path.GetDirectoryName(fullPath);
				IntPtr handle;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
				// Unity usually resolves native plugins itself (see the .meta PluginImporter settings), so
				// we only remember the path here; GetHandle loads it lazily when Unity has not — a plugin
				// disabled for this platform, or one shipped from a mod folder, which Unity never imports.
				_libCache[name] = new LibEntry {
					Handle = IntPtr.Zero, // resolved lazily by GetHandle
					Path   = fullPath,
					ModIds = new HashSet<string> { modId },
				};
				return 1;
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
				handle = dlopen(fullPath, RTLD_NOW);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
				handle = dlopen(fullPath, RTLD_NOW);
#else
				handle = new IntPtr(1);
#endif

				if (handle == IntPtr.Zero) {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
					int win32Err = Marshal.GetLastWin32Error();
					throw new DllNotFoundException(
						$"Failed to load native library '{name}' from {fullPath} " +
						$"(error 0x{win32Err:X8}). The library may be missing dependencies " +
						$"(e.g. Visual C++ Redistributable).");
#else
					throw new DllNotFoundException(
						$"Failed to load native library '{name}' from {fullPath}: {LastNativeError()}");
#endif
				}

				_libCache[name] = new LibEntry {
					Handle = handle,
					Path   = fullPath,
					Owned  = true,
					ModIds = new HashSet<string> { modId },
				};

				return 1;
			}
		}

		/// <summary>
		/// Remove <paramref name="modId"/> from the reference set.
		/// Physically unloads the library when the last mod releases it.
		/// </summary>
		public static void Unload(string name, string modId) {
			lock (_lock) {
				if (!_libCache.TryGetValue(name, out var entry))
					return;

				entry.ModIds.Remove(modId);

				if (entry.ModIds.Count > 0) {
					_libCache[name] = entry;
					return;
				}

				// Last reference — physically unload. A handle coming from GetModuleHandle holds no
				// reference of ours, so only a library we loaded ourselves is released here.
				if (entry.Handle != IntPtr.Zero) {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
					if (entry.Owned)
						FreeLibrary(entry.Handle);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
					dlclose(entry.Handle);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
					dlclose(entry.Handle);
#endif
				}
				_libCache.Remove(name);
			}
		}

		/// <summary>
		/// Message of the last native loader error (<c>dlerror</c>). This turns an opaque
		/// "failed to load" into an actionable message such as
		/// <c>libcrypto.so.3: cannot open shared object file: No such file or directory</c>.
		/// Must be called immediately after the failing <c>dlopen</c>.
		/// </summary>
		private static string LastNativeError() {
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
			try {
				var message = dlerror();
				return message != IntPtr.Zero
					? Marshal.PtrToStringAnsi(message)
					: "unknown dlopen error";
			} catch {
				return "unknown dlopen error";
			}
#else
			return "unknown error";
#endif
		}

		/// <summary>
		/// Checks that <paramref name="modId"/> has previously loaded <paramref name="name"/>
		/// (whitelist) and returns its cached <c>LibEntry</c>. Throws if not whitelisted.
		/// </summary>
		private static LibEntry RequireLoaded(string name, string modId)
		{
			if (!_libCache.TryGetValue(name, out var entry) || !entry.ModIds.Contains(modId))
				throw new InvalidOperationException(
					$"Mod '{modId}' has not loaded native library '{name}'. " +
					"Call Load(name) before resolving handles/symbols (whitelist).");
			return entry;
		}

		/// <summary>
		/// Returns the native module handle for <paramref name="name"/> if (and only if)
		/// <paramref name="modId"/> previously loaded it. On Windows the handle is resolved lazily and
		/// cached, including a definitive failure (see <see cref="Load"/>).
		/// </summary>
		public static IntPtr GetHandle(string name, string modId)
		{
			lock (_lock)
			{
				var entry = RequireLoaded(name, modId);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
				// Cache the resolved module handle (GetModuleHandle is cheap but repeated calls
				// across every symbol resolution are wasteful).
				if (entry.Handle == IntPtr.Zero && !entry.Failed)
				{
					var moduleName = name + GetExtension();
					entry.Handle = GetModuleHandle(moduleName);

					// Not in the process: load it ourselves from the path resolved by Load().
					// LOAD_WITH_ALTERED_SEARCH_PATH makes the library's own directory the search root, so
					// sibling dependencies resolve even though only its full path is known.
					if (entry.Handle == IntPtr.Zero && !string.IsNullOrEmpty(entry.Path))
					{
						entry.Handle = LoadLibraryEx(entry.Path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
						entry.Owned  = entry.Handle != IntPtr.Zero;

						if (entry.Handle == IntPtr.Zero)
						{
							entry.Failed = true;
							var win32Err = Marshal.GetLastWin32Error();
							Logger.LogError(
								$"Failed to load native library '{entry.Path}' (error 0x{win32Err:X8}). " +
								"The library may be missing dependencies (e.g. Visual C++ Redistributable).",
								tag: nameof(LibManager));
						}
					}

					_libCache[name] = entry;
				}
#endif
				return entry.Handle;
			}
		}

		/// <summary>
		/// Resolves the address of the native export <paramref name="symbol"/> from a library
		/// that <paramref name="modId"/> has previously loaded (whitelist).
		/// </summary>
		public static IntPtr GetSymbol(string name, string symbol, string modId)
		{
			var handle = GetHandle(name, modId);
			if (handle == IntPtr.Zero)
				return IntPtr.Zero;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			return GetProcAddress(handle, symbol);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
			return dlsym(handle, symbol);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
			return dlsym(handle, symbol);
#else
			return IntPtr.Zero;
#endif
		}

		/// <summary>
		/// Resolves and wraps a native export as a managed delegate (DllImport-free). The library
		/// must have been previously loaded by <paramref name="modId"/> (whitelist).
		/// </summary>
		public static T GetDelegate<T>(string name, string symbol, string modId)
			where T : Delegate
		{
			var ptr = GetSymbol(name, symbol, modId);
			if (ptr == IntPtr.Zero)
				throw new EntryPointNotFoundException(
					$"Could not find the entrypoint '{symbol}' in native library '{name}'.");
			return Marshal.GetDelegateForFunctionPointer<T>(ptr);
		}

		/// <summary>
		/// Returns all library names loaded by <paramref name="modId"/>.
		/// Must be called inside <c>lock(LibManager.Lock)</c>.
		/// </summary>
		internal static string[] GetLibraries(string modId) {
			var result = new List<string>();
			foreach (var kv in _libCache) 
				if (kv.Value.ModIds.Contains(modId))
					result.Add(kv.Key);
			return result.ToArray();
		}

		internal static object Lock => _lock;

		// Both values read the current Unity build target (PlatformExtensions.CurrentPlatform), which
		// is an editor-only, main-thread-only API. Native exports, however, are resolved lazily from
		// background threads (e.g. the FFmpeg read thread), where calling it throws
		// "get_activeBuildTarget can only be called from the main thread". The first call happens on
		// the main thread while pre-loading libraries, so worker threads only ever read the cache.
		// _subFolders holds relative sub-paths (e.g. "windows/x64") to combine with a Plugins root;
		// the empty entry means the root folder itself.
		private static string _extension;
		private static string[] _subFolders;

		/// <summary>
		/// Returns the prioritized list of plugin sub-paths (relative to the <c>Plugins</c> root)
		/// for the current platform and CPU architecture, ordered from most specific to least:
		/// <c>&lt;platform&gt;/&lt;arch&gt;</c>, <c>&lt;platform&gt;</c>, then <c>""</c> (the root itself).
		/// Example on Windows x64: <c>["windows/x64", "windows", ""]</c>.
		/// Delegates to <see cref="Library.CurrentSubFolders"/>.
		/// Cached — see the note on <see cref="_subFolders"/>.
		/// </summary>
		public static string[] GetSubFolders()
			=> _subFolders ??= Library.CurrentSubFolders;

		/// <summary>
		/// Public extension accessor (mirrors ILibAPI.GetExtension).
		/// Cached — see the note on <see cref="_extension"/>.
		/// </summary>
		public static string GetExtension()
			=> _extension ??= Library.CurrentLibraryExtension;

		/// <summary>
		/// Global fallback plugin folders (shared across all mods), i.e. the game's
		/// <c>&lt;dataPath&gt;/Plugins/&lt;platform&gt;/&lt;arch&gt;</c>, <c>&lt;dataPath&gt;/Plugins/&lt;platform&gt;</c>
		/// and finally <c>&lt;dataPath&gt;/Plugins</c> itself (the empty sub-path).
		/// </summary>
		public static string[] GetGlobalPluginFolders() {
			var root = Path.Combine(Application.dataPath, "Plugins");
			return GetSubFolders()
				.Select(s => Path.Combine(root, s))
				.Where(Directory.Exists)
				.ToArray();
		}

		/// <summary>
		/// Search <paramref name="modFolders"/> then global folders for <paramref name="name"/>.
		/// Both lists are ordered most-specific-first, so the native binary is picked from
		/// <c>&lt;platform&gt;/&lt;arch&gt;</c> before <c>&lt;platform&gt;</c> and finally the root folder.
		/// Returns the full path or <c>null</c>.
		/// </summary>
		public static string ToPath(string name, string[] modFolders) {
			var ext = GetExtension();
			var filename = name + ext;

			foreach (var folder in modFolders) {
				var path = Path.Combine(folder, filename);
				if (File.Exists(path)) return path;
			}
			foreach (var folder in GetGlobalPluginFolders()) {
				var path = Path.Combine(folder, filename);
				if (File.Exists(path)) return path;
			}
			return null;
		}
	}
}
