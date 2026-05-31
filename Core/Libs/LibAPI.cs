using System;
using System.IO;
using Nox.CCK.Mods.Libs;
using Nox.ModLoader.Mods;
using UnityEngine;

namespace Nox.ModLoader.Core.Libs {
	public class LibAPI : ILibAPI {
		private readonly Mod _mod;
		private string MId
			=> _mod.Metadata.GetId();

		public LibAPI(Mod mod)
			=> _mod = mod;

		public string[] GetFolders() {
			var arch        = GetArch();
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

		public string[] GetLibraries()
			=> LibManager.GetLibraries(MId);

		public string GetExtension()
			=> LibManager.GetExtension();

		public string GetArch()
			=> LibManager.GetArch();

		public string ToPath(string name)
			=> LibManager.ToPath(name, GetFolders());


		// ── Delegated to LibManager ──

		public void Load(string name)
			=> LibManager.Load(name, MId, GetFolders());

		public void Unload(string name)
			=> LibManager.Unload(name, MId);

		public void Unload() {
			var libs = LibManager.GetLibraries(MId);
			foreach (var lib in libs)
				Unload(lib);
		}
	}
}