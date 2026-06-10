using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Mods.Initializers;
using Nox.CCK.Mods.Metadata;
using Nox.CCK.Utils;

namespace Nox.ModLoader.EntryPoints {
	public static class EntryPointHelper {
		public static T[] Instantiate<T>(this EntryPoint entry) where T : IModInitializer {
			var types = entry.GetTypes<T>();

			if (types.Length == 0)
				return Array.Empty<T>();

			var instances = new Dictionary<string, T>();

			foreach (var (key, type) in types) {
				if (instances.ContainsKey(key))
					continue;

				var instance = (T)Activator.CreateInstance(type, true);

				if (instance == null) {
					Logger.LogError($"Failed to create instance of type {type.FullName} for entry point {entry.Name} in mod {entry.Mod.Metadata.GetId()}");
					continue;
				}

				instances.Add(key, instance);
			}

			return instances.Values.ToArray();
		}

		private static (string, Type)[] GetTypes<T>(this EntryPoint entry) where T : IModInitializer {
			var entries = entry.Mod.Metadata.GetEntryPoints();

			if (!entries.Has(entry.Name))
				return Array.Empty<(string, Type)>();

			var elements = entries.Get(entry.Name);
			var t = typeof(T);
			var assemblies = entry.Mod.GetAssemblies();

			var results = new List<(string, Type)>();

			foreach (var element in elements) {
				var fullName = element.FullName;
				if (string.IsNullOrEmpty(fullName)) continue;

				Type foundType = null;

				// If assembly is explicitly specified, lookup directly by name
				if (!string.IsNullOrEmpty(element.Assembly)) {
					var asm = Array.Find(assemblies, a => a.GetName().Name == element.Assembly);
					if (asm != null) {
						foundType = asm.GetType(fullName); // O(1) dictionary lookup
						if (foundType == null)
							Logger.LogWarning($"Type {fullName} not found in assembly {element.Assembly} for entry point {entry.Name} in mod {entry.Mod.Metadata.GetId()}");
					} else {
						Logger.LogWarning($"Assembly {element.Assembly} not found for entry point {entry.Name} in mod {entry.Mod.Metadata.GetId()}");
					}
				} else {
					// Fallback: auto-discover using GetType() (O(1) per assembly)
					foreach (var asm in assemblies) {
						foundType = asm.GetType(fullName);
						if (foundType != null) {
							// Self-heal: cache the assembly for future lookups
							element.Assembly = asm.GetName().Name;
							break;
						}
					}

					if (foundType == null)
						Logger.LogWarning($"Could not find type {fullName} in mod {entry.Mod.Metadata.GetId()}");
				}

				if (foundType == null) continue;

				if (foundType.GetInterface(t.FullName) == null) {
					Logger.LogWarning($"Type {fullName} does not implement {t.FullName} in mod {entry.Mod.Metadata.GetId()}");
					continue;
				}

				results.Add((fullName, foundType));
			}

			return results.ToArray();
		}
	}
}