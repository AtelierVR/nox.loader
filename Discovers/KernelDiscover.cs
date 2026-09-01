using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Nox.CCK.Utils;
using Nox.ModLoader.Mods;
using Nox.ModLoader.Typing;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Discovers {
	public class KernelDiscover : IDiscover {
		private static IDiscover _instance;

		public static IDiscover Instance
			=> _instance ?? new KernelDiscover();

		public KernelDiscover() {
			_instance = this;
		}

		public ModMetadata[] FindAllPackages() {
			#if UNITY_EDITOR
			List<ModMetadata> packages = new();

			Logger.LogDebug("Finding kernel mods with Mod Metadata...");

			foreach (var noxmod in EnumerateModManifests()) {
				try {
					var noxobj = ModMetadata.LoadFromPath(noxmod);
					if (noxobj == null)
						continue;
					var folder                     = Path.GetDirectoryName(noxmod) ?? string.Empty;
					noxobj.InternalData["folder"]   = folder;
					noxobj.InternalData["manifest"] = noxmod;
					noxobj.InternalData["assets"]   = Path.Combine(folder, "Assets");
					noxobj.InternalDDiscover        = this;
					packages.Add(noxobj);
				} catch (Exception e) {
					Logger.LogError(new Exception($"Error loading mod metadata from {noxmod}", e));
					continue;
				}
			}

			if (packages.Count == 0)
				return Array.Empty<ModMetadata>();

			Logger.LogDebug("Found " + packages.Count + " kernel mod(s):");
			foreach (var package in packages)
				Logger.LogDebug(" - " + package.GetId());

			return packages.ToArray();
			#else
            List<ModMetadata> packages = new();
            Logger.Log("Finding kernel mods with GameData...");

            try
            {
                var dataObject =
 JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "Nox", "game_data.json")));

                List<ModMetadata> mods = new();
                foreach (var mod in dataObject.GetValue("mods").ToList())
                {
                    var noxobj = ModMetadata.LoadFromJson(mod.ToObject<JObject>());
                    if (noxobj == null) continue;

                    var kernel = noxobj.GetCustom<JObject>("kernel");
                    if (kernel == null) continue;

                    var active = kernel.GetValue("active").ToObject<bool>();
                    var folder = kernel.GetValue("base_path").ToObject<string>();
                    if (!active || string.IsNullOrEmpty(folder)) continue;

                    noxobj.InternalData["folder"] = folder;
                    noxobj.InternalDDiscover = this;
                    packages.Add(noxobj);
                }
                return packages.ToArray();
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                Logger.LogError("game_data.asset not found! Skipping kernel mods...");
                return packages.ToArray();
            }
			#endif
		}

		/// <summary>
		/// Enumerate every <c>nox.mod.json</c> manifest in the project (Assets and Packages).
		/// </summary>
		private IEnumerable<string> EnumerateModManifests() {
			var modFiles = new List<string>();
			modFiles.AddRange(Directory.GetFiles(Application.dataPath, "nox.mod.json", SearchOption.AllDirectories));
			#if UNITY_EDITOR
			// Packages/ — local file: packages
			var packagesDir = Path.Combine(Application.dataPath, "..", "Packages");
			if (Directory.Exists(packagesDir))
				modFiles.AddRange(Directory.GetFiles(packagesDir, "nox.mod.json", SearchOption.AllDirectories));
			// Library/PackageCache/ — git/upm packages resolved from manifest.json
			var cacheDir = Path.Combine(Application.dataPath, "..", "Library", "PackageCache");
			if (Directory.Exists(cacheDir))
				try {
					modFiles.AddRange(Directory.GetFiles(cacheDir, "nox.mod.json", SearchOption.AllDirectories));
				} catch (DirectoryNotFoundException e) {
					Logger.LogWarning($"Skipping inaccessible PackageCache directory: {e.Message}");
				}
			#endif
			return modFiles;
		}

		public ModMetadata FindPackage(string id) {
			foreach (var noxmod in EnumerateModManifests()) {
				try {
					var noxobj = ModMetadata.LoadFromPath(noxmod);
					if (noxobj == null)
						continue;
					if (noxobj.GetId() != id && !(noxobj.GetProvides()?.Contains(id) ?? false))
						continue;
					var folder                     = Path.GetDirectoryName(noxmod) ?? string.Empty;
					noxobj.InternalData["folder"] = folder;
					noxobj.InternalDDiscover      = this;
					return noxobj;
				} catch {
					// Ignore malformed manifests.
				}
			}
			return null;
		}

		public Mod CreateMod(ModMetadata metadata)
			=> new KernelMod() { Metadata = metadata };
	}
}