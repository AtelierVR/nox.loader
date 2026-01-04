using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Mods.Initializers;
using Nox.CCK.Utils;

namespace Nox.ModLoader.EntryPoints {
	public static class EntryPointHelper {
		public static T[] Instantiate<T>(this EntryPoint entry) where T : IModInitializer {
			var types = entry.GetTypes<T>();

			if (types.Length == 0)
				return Array.Empty<T>();

			var instances = new Dictionary<string, T>();

			foreach (var (ns, type) in types) {
				if (instances.ContainsKey(ns))
					continue;

				var instance = (T)Activator.CreateInstance(type, true);

				if (instance == null) {
					Logger.LogError($"Failed to create instance of type {type.FullName} for entry point {entry.Name} in mod {entry.Mod.Metadata.GetId()}");
					continue;
				}

				instances.Add(ns, instance);
			}

			return instances.Values.ToArray();
		}

		private static (string, Type)[] GetTypes<T>(this EntryPoint entry) where T : IModInitializer {
			var entries = entry.Mod.Metadata.GetEntryPoints();

			if (!entries.Has(entry.Name))
				return Array.Empty<(string, Type)>();

			var namespaces = entries.Get(entry.Name);
			var t = typeof(T);

			var ts = (from assembly in entry.Mod.GetAssemblies()
				from type in assembly.GetTypes()
				from ns in namespaces
				where type.FullName == ns
				select (ns, type)).ToList();
			
			foreach (var ns in namespaces) {
				var match = ts.FirstOrDefault(x => x.Item1 == ns);
				if (match == default) {
					Logger.LogWarning($"Could not find type {ns} in mod {entry.Mod.Metadata.GetId()}");
					continue;
				}
				
				if (match.type.GetInterface(t.FullName) == null) {
					Logger.LogWarning($"Type {ns} does not implement {t.FullName} in mod {entry.Mod.Metadata.GetId()}");
					ts.Remove(match);
					continue;
				}
				
				// Additional check for parameterless constructor
			}

			return ts.ToArray();
		}
	}
}