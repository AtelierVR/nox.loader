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

			Logger.LogDebug("Finding kernel mods with Assembly Definitions...");

			var files = Directory.GetFiles(Application.dataPath, "*.asmdef", SearchOption.AllDirectories);
			#if UNITY_EDITOR
			files = files.Concat(Directory.GetFiles(Path.Combine(Application.dataPath, "..", "Packages"), "*.asmdef", SearchOption.AllDirectories)).ToArray();
			#endif
			

			foreach (var file in files) {
				var obj = JObject.Parse(File.ReadAllText(file));
				if (!obj.TryGetValue("name", out _)) continue;
				var noxmod = Directory
					.GetFiles(Path.GetDirectoryName(file) ?? string.Empty, "nox.mod.json*", SearchOption.TopDirectoryOnly)
					.FirstOrDefault();
				if (noxmod == null) continue;
				var noxobj = ModMetadata.LoadFromPath(noxmod);
				if (noxobj == null) continue;
				noxobj.InternalData["folder"]     = Path.GetDirectoryName(file);
				noxobj.InternalData["definition"] = file;
				noxobj.InternalData["manifest"]   = noxmod;
				noxobj.InternalData["assets"]     = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, "assets");
				noxobj.InternalDDiscover          = this;
				packages.Add(noxobj);
			}

			if (packages.Count == 0) return Array.Empty<ModMetadata>();

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

		public ModMetadata FindPackage(string id) {
			var asmdef = Directory.GetFiles(Application.dataPath, id + ".asmdef", SearchOption.AllDirectories)
				.FirstOrDefault();
			if (asmdef == null) return null;
			var noxmod = Directory
				.GetFiles(Path.GetDirectoryName(asmdef) ?? string.Empty, "nox.mod.json*", SearchOption.TopDirectoryOnly)
				.FirstOrDefault();
			if (noxmod == null) return null;
			var noxobj = ModMetadata.LoadFromPath(noxmod);
			if (noxobj == null) return null;
			noxobj.InternalData["folder"] = Path.GetDirectoryName(asmdef);
			noxobj.InternalDDiscover      = this;
			return noxobj;
		}

		public Mod CreateMod(ModMetadata metadata)
			=> new KernelMod() { Metadata = metadata };
	}
}