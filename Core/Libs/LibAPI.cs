using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
			var subFolders = GetSubFolders();

			// Helper: build a Plugins/{sub} list from a root folder (only existing dirs)
			string[] Build(string root) {
				return subFolders.Select(s => Path.Combine(root, "Plugins", s))
					.Append(Path.Combine(root, "Plugins"))
					.Where(Directory.Exists)
					.ToArray();
			}

			#if UNITY_EDITOR
			// Editor: plugins live next to the mod's asmdef/folder
			var folder = _mod.GetData<string>("folder");
			if (!string.IsNullOrEmpty(folder))
				return Build(folder);
			_mod.CoreAPI.LoggerAPI.LogWarning($"Mod {_mod.Metadata.GetId()} does not have a folder path. Native plugins will not be loaded. This may indicate an issue with the mod's structure or packaging.");
			return Array.Empty<string>();
			#else
			if (_mod is KernelMod) {
				// Kernel mods ship their native plugins into the game's Plugins/<sub>/ folder
				return Build(Application.dataPath);
			} else {
				// External (folder) mods keep plugins next to their bundle
				var folder = _mod.GetData<string>("folder");
				if (string.IsNullOrEmpty(folder)) {
					_mod.CoreAPI.LoggerAPI.LogWarning($"Mod {_mod.Metadata.GetId()} does not have a folder path. Native plugins will not be loaded. This may indicate an issue with the mod's structure or packaging.");
					return Array.Empty<string>();
				}
				return Build(folder);
			}
			#endif
		}

		public string[] GetLibraries()
			=> LibManager.GetLibraries(MId);

		public string GetExtension()
			=> LibManager.GetExtension();

		public string[] GetSubFolders()
			=> LibManager.GetSubFolders();

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