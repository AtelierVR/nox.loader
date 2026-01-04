using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nox.CCK.Utils;
using Nox.ModLoader.Mods;
using Nox.ModLoader.Typing;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Discovers {
	public class FolderDiscover : IDiscover {
		private static IDiscover _instance;

		public static IDiscover Instance
			=> _instance ?? new FolderDiscover();

		private FolderDiscover()
			=> _instance = this;


		private const bool UseGlobalPackages = true;

		private static string[] PackageFolders {
			get {
				List<string> folders = new();

				#if UNITY_EDITOR
				folders.Add(Path.Combine(Application.dataPath, "..", "Library", "NoxMods"));
				folders.Add(Path.Combine(Application.dataPath, "..", "NoxMods"));
				#endif

				if (UseGlobalPackages) {
					folders.Add(Path.Combine(Constants.AppPath, "mods"));
					var config = Config.Load();
					if (config.Has("mod_folders"))
						folders.AddRange(config.Get<string[]>("mod_folders"));
				}

				return folders.Where(Directory.Exists).ToArray();
			}
		}

		public ModMetadata[] FindAllPackages() {
			List<ModMetadata> packages = new();

			Logger.LogDebug($"Finding folder mods in {PackageFolders.Length} folder(s): {(PackageFolders.Length == 0 ? "Skipped" : "")}");
			foreach (var folder in PackageFolders)
				Logger.LogDebug($" - {folder}");

			foreach (var psf in PackageFolders) {
				// Find folder-based mods
				var pfs = Directory.GetDirectories(psf);
				foreach (var pf in pfs) {
					var noxmod = Directory.GetFiles(pf, "nox.mod.json*", SearchOption.TopDirectoryOnly).FirstOrDefault();
					if (noxmod == null) continue;
					var noxobj = ModMetadata.LoadFromPath(noxmod);
					if (noxobj == null) continue;
					noxobj.InternalData["folder"] = pf;
					noxobj.InternalDDiscover      = this;
					packages.Add(noxobj);
				}
			}

			if (packages.Count == 0) return Array.Empty<ModMetadata>();

			Logger.LogDebug("Found " + packages.Count + " folder mod(s):");
			foreach (var package in packages)
				Logger.LogDebug($" - {package.GetId()}");

			return packages.ToArray();
		}

		public ModMetadata FindPackage(string id) {
			foreach (var psf in PackageFolders) {
				// Search in folders
				var pfs = Directory.GetDirectories(psf);
				foreach (var pf in pfs) {
					var noxmod = Directory.GetFiles(pf, "nox.mod.json*", SearchOption.TopDirectoryOnly).FirstOrDefault();
					if (noxmod == null) continue;
					var noxobj = ModMetadata.LoadFromPath(noxmod);
					if (noxobj         == null) continue;
					if (noxobj.GetId() != id) continue;
					noxobj.InternalData["folder"] = pf;
					noxobj.InternalDDiscover      = this;
					return noxobj;
				}
			}

			return null;
		}

		public Mod CreateMod(ModMetadata metadata)
			=> new FolderMod { Metadata = metadata };
	}
}