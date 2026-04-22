using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Nox.CCK.Mods.Libs;
using Nox.ModLoader.Mods;
using UnityEngine;

namespace Nox.ModLoader.Core.Libs {
	public class LibAPI : ILibAPI {
		private readonly Mod _mod;

		public LibAPI(Mod mod) {
			_mod = mod;
		}

		public string[] GetNativePluginFolders() {
			var arch        = GetArchSubfolder();
			var pluginsBase = Path.Combine(Application.dataPath, "Plugins");

			#if UNITY_EDITOR
			// Editor: plugins live next to the mod's asmdef/folder
			var folder = _mod.GetData<string>("folder");
			if (!string.IsNullOrEmpty(folder))
				return string.IsNullOrEmpty(arch)
					? new[] { Path.Combine(folder, "Plugins") }
					: new[] { Path.Combine(folder, "Plugins", arch), Path.Combine(folder, "Plugins") };
			return Array.Empty<string>();
			#else
			if (_mod is KernelMod) {
				// Kernel mods ship their native plugins into the game's Plugins/<arch>/ folder
				return string.IsNullOrEmpty(arch)
					? new[] { pluginsBase }
					: new[] { Path.Combine(pluginsBase, arch), pluginsBase };
			} else {
				// External (folder) mods keep plugins next to their bundle
				var folder = _mod.GetData<string>("folder");
				if (string.IsNullOrEmpty(folder)) return Array.Empty<string>();
				var modPlugins = Path.Combine(folder, "Plugins");
				return string.IsNullOrEmpty(arch)
					? new[] { modPlugins }
					: new[] { Path.Combine(modPlugins, arch), modPlugins };
			}
			#endif
		}

		public string[] GetLibraries() {
			var ext = GetExtension();
			return GetNativePluginFolders()
				.Where(Directory.Exists)
				.SelectMany(d => Directory.GetFiles(d, "*" + ext))
				.Select(f => Path.GetFileNameWithoutExtension(f))
				.Distinct()
				.ToArray();
		}

		public string GetExtension() {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return ".dylib";
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return ".dll";
			return ".so";
		}

		public string GetArch() {
			var arch = GetArchSubfolder();
			return string.IsNullOrEmpty(arch) ? null : arch;
		}

		public string ToPath(string name) {
			var ext = GetExtension();
			var filename = name + ext;
			foreach (var folder in GetNativePluginFolders()) {
				var path = Path.Combine(folder, filename);
				if (File.Exists(path)) return path;
			}
			return null;
		}

		private static string GetArchSubfolder() {
			if (RuntimeInformation.OSArchitecture == Architecture.X64)   return "x86_64";
			if (RuntimeInformation.OSArchitecture == Architecture.Arm64)  return "ARM64";
			return string.Empty;
		}
	}
}
