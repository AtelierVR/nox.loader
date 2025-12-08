using System;
using System.Collections.Generic;
using System.IO;
using Nox.ModLoader.Assemblies;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Mods.Helpers {
	/// <summary>
	/// Helper class for loading assemblies in IL2CPP builds.
	/// Only supports ILRuntime bytecode (.bytes) files.
	/// </summary>
	internal static class IL2CPPAssemblyHelper {
		/// <summary>
		/// Loads an assembly using ILRuntime for IL2CPP builds.
		/// Only supports .bytes bytecode files.
		/// </summary>
		/// <param name="fullPath">The full path to the assembly (without .bytes extension)</param>
		/// <param name="filePath">The relative file path for logging</param>
		/// <param name="assemblyLoaders">The list to add the loader to</param>
		/// <param name="loadedTypes">The list to add loaded types to</param>
		/// <returns>True if the assembly was loaded successfully</returns>
		public static bool LoadAssembly(
			string fullPath,
			string filePath,
			List<IModAssemblyLoader> assemblyLoaders,
			List<Type> loadedTypes
		) {
			// IL2CPP only supports ILRuntime bytecode
			var bytesPath = fullPath + ".bytes";
			if (!File.Exists(bytesPath)) {
				Logger.LogWarning($"[IL2CPP] Bytecode assembly not found: {bytesPath}");
				return false;
			}

			var loader = new ILRuntimeLoader();
			if (loader.LoadAssembly(bytesPath)) {
				assemblyLoaders.Add(loader);
				loadedTypes.AddRange(loader.GetTypes());
				Logger.LogDebug($"[IL2CPP] Loaded ILRuntime assembly '{filePath}' with {loader.GetTypes().Length} types");
				return true;
			}

			loader.Dispose();
			Logger.LogWarning($"[IL2CPP] Failed to load ILRuntime assembly '{filePath}'");
			return false;
		}
	}
}
