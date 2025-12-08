using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Mods.Initializers;

namespace Nox.ModLoader.EntryPoints {
	public static class EntryPointHelper {
		public static T[] Instantiate<T>(this EntryPoint entry) where T : IModInitializer {
			var types = GetTypes<T>(entry);

			if (types.Length == 0)
				return Array.Empty<T>();

			var instances = new Dictionary<string, T>();

			foreach (var type in types) {
				var instance = (T)Activator.CreateInstance(type, true);
				if (type.FullName != null) instances.Add(type.FullName, instance);
			}

			return instances.Values.ToArray();
		}

		private static Type[] GetTypes<T>(this EntryPoint entry) where T : IModInitializer {
			var entries = entry.Mod.Metadata.GetEntryPoints();

			if (!entries.Has(entry.Name))
				return Type.EmptyTypes;

			var namespaces = entries.Get(entry.Name);
			var t          = typeof(T);

			return (from assembly in entry.Mod.GetAssemblies()
				from type in assembly.GetTypes()
				from ns in namespaces
				where type.FullName == ns && type.GetInterface(t.FullName) != null
				select type).ToArray();
		}
	}
}