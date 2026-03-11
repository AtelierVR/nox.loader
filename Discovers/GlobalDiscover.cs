using System;
using System.Collections.Generic;
using Nox.CCK.Utils;
using Nox.ModLoader.Mods;
using Nox.ModLoader.Typing;

namespace Nox.ModLoader.Discovers {
	public class GlobalDiscover : IDiscover {
		private static IDiscover _instance;
		public static IDiscover Instance
			=> _instance ?? new GlobalDiscover();

		public GlobalDiscover() {
			_instance = this;
		}

		public IDiscover[] Discovers { get; } = {
			KernelDiscover.Instance,
			FolderDiscover.Instance
		};

		public ModMetadata[] FindAllPackages() {
			List<ModMetadata> packages = new();
			foreach (var discover in Discovers)
				try {
					packages.AddRange(discover.FindAllPackages());
				} catch (Exception e) {
					Logger.LogError(new Exception($"Error in discover {discover.GetType().Name}", e));
				}
			return packages.ToArray();
		}

		public ModMetadata FindPackage(string id) {
			foreach (var discover in Discovers) {
				var package = discover.FindPackage(id);
				if (package != null)
					return package;
			}
			return null;
		}

		public Mod CreateMod(ModMetadata metadata)
			=> metadata.InternalDDiscover.CreateMod(metadata);
	}
}