using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Nox.CCK.Utils;

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
			public HashSet<string> ModIds;
		}

		private static readonly Dictionary<string, LibEntry> _libCache = new();
		private static readonly object _lock = new();

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
		[DllImport("kernel32", SetLastError = true, EntryPoint = "LoadLibraryW")]
		private static extern IntPtr LoadLibraryWin(string lpFileName);

		[DllImport("kernel32", SetLastError = true)]
		private static extern bool FreeLibrary(IntPtr hModule);

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool SetDllDirectory(string lpPathName);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
		[DllImport("dl", SetLastError = true)]
		private static extern IntPtr dlopen(string filename, int flags);
		private const int RTLD_NOW = 2;

		[DllImport("dl", SetLastError = true)]
		private static extern int dlclose(IntPtr handle);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
		[DllImport("libdl", SetLastError = true)]
		private static extern IntPtr dlopen(string filename, int flags);
		private const int RTLD_NOW = 2;

		[DllImport("libdl", SetLastError = true)]
		private static extern int dlclose(IntPtr handle);
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
				// LoadLibrary often fails in Unity's process for native plugins
				// because Unity already manages plugin resolution differently.
				// Skip physical pre-load — [DllImport] will find the DLL through
				// Unity's native plugin system. We still track the mod reference.
				_libCache[name] = new LibEntry {
					Handle = IntPtr.Zero, // not physically pre-loaded
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
						$"Failed to load native library '{name}' from {fullPath}.");
#endif
				}

				_libCache[name] = new LibEntry {
					Handle = handle,
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

				// Last reference — physically unload (if we loaded it)
				if (entry.Handle != IntPtr.Zero) {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
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

		/// <summary>
		/// Returns the prioritized list of compatible plugin subfolder names for the current
		/// platform and CPU architecture (delegates to <see cref="Library.CurrentSubFolders"/>).
		/// </summary>
		public static string[] GetSubFolders()
			=> Library.CurrentSubFolders;

		/// <summary>Public extension accessor (mirrors ILibAPI.GetExtension).</summary>
		public static string GetExtension()
			=> Library.CurrentLibraryExtension;

		/// <summary>Global fallback plugin folders (shared across all mods).</summary>
		public static string[] GetGlobalPluginFolders() {
			var pluginsBase = Path.Combine(
				UnityEngine.Application.dataPath, "Plugins");
			return GetSubFolders().Select(s => Path.Combine(pluginsBase, s))
				.Append(pluginsBase)
				.Where(Directory.Exists)
				.ToArray();
		}

		/// <summary>
		/// Search <paramref name="modFolders"/> then global folders for <paramref name="name"/>.
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
