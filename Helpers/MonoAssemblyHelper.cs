#if !ENABLE_IL2CPP
using System;
using System.Collections.Generic;
using System.IO;
using Nox.ModLoader.Assemblies;
using Nox.ModLoader.Permissions;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Mods.Helpers {
	/// <summary>
	/// Helper class for loading assemblies in Mono builds.
	/// Supports both ILRuntime bytecode (.bytes) and native C# DLL files.
	/// Uses ModAssemblyLoadContext for proper isolation per mod.
	/// </summary>
	internal static class MonoAssemblyHelper {
		/// <summary>
		/// Loads an assembly for Mono builds using a custom AssemblyLoadContext.
		/// Tries ILRuntime bytecode (.bytes) first, then falls back to native C# DLL.
		/// </summary>
		/// <param name="fullPath">The full path to the assembly</param>
		/// <param name="filePath">The relative file path for logging</param>
		/// <param name="assemblyLoaders">The list to add the loader to</param>
		/// <param name="loadedTypes">The list to add loaded types to</param>
		/// <param name="loadContext">The mod's AssemblyLoadContext for isolation</param>
		/// <param name="permissionContext">The mod's permission context for security validation</param>
		/// <returns>True if the assembly was loaded successfully</returns>
		public static bool LoadAssembly(
			string fullPath,
			string filePath,
			List<IModAssemblyLoader> assemblyLoaders,
			List<Type> loadedTypes,
			ModAssemblyLoadContext loadContext = null,
			ModPermissionContext permissionContext = null
		) {
			// Check for ILRuntime bytecode version first
			var bytesPath = fullPath + ".bytes";
			if (File.Exists(bytesPath)) {
				return LoadILRuntimeAssembly(bytesPath, filePath, assemblyLoaders, loadedTypes);
			}

			// Fallback to native C# DLL
			if (File.Exists(fullPath)) {
				return LoadNativeCSharpAssembly(fullPath, filePath, assemblyLoaders, loadedTypes, loadContext, permissionContext);
			}

			Logger.LogWarning($"[Mono] Referenced assembly not found: {fullPath}");
			return false;
		}

		/// <summary>
		/// Loads an ILRuntime bytecode assembly (.bytes file) for Mono builds.
		/// ILRuntime has its own isolation mechanism.
		/// </summary>
		private static bool LoadILRuntimeAssembly(
			string bytesPath,
			string filePath,
			List<IModAssemblyLoader> assemblyLoaders,
			List<Type> loadedTypes
		) {
			var loader = new ILRuntimeLoader();
			if (loader.LoadAssembly(bytesPath)) {
				assemblyLoaders.Add(loader);
				loadedTypes.AddRange(loader.GetTypes());
				Logger.LogDebug($"[Mono/ILRuntime] Loaded bytecode assembly '{filePath}' with {loader.GetTypes().Length} types");
				return true;
			}

			loader.Dispose();
			Logger.LogWarning($"[Mono/ILRuntime] Failed to load bytecode assembly '{filePath}'");
			return false;
		}

		/// <summary>
		/// Loads a native C# DLL assembly for Mono builds.
		/// Uses ModAssemblyLoadContext if provided for proper isolation.
		/// Validates the assembly for security violations before loading.
		/// </summary>
		private static bool LoadNativeCSharpAssembly(
			string dllPath,
			string filePath,
			List<IModAssemblyLoader> assemblyLoaders,
			List<Type> loadedTypes,
			ModAssemblyLoadContext loadContext,
			ModPermissionContext permissionContext
		) {
			// Validate the assembly for security violations BEFORE loading
			var validationResult = AssemblySecurityValidator.ValidateAssembly(dllPath, permissionContext);
			if (!validationResult.IsValid) {
				Logger.LogError($"[Mono/Security] Assembly '{filePath}' BLOCKED due to security violations:");
				foreach (var violation in validationResult.Violations) {
					var permInfo = string.IsNullOrEmpty(violation.RequiredPermission) 
						? "" 
						: $" [requires permission: {violation.RequiredPermission}]";
					Logger.LogError($"  - [{violation.ViolationType}] {violation.TypeName}.{violation.MemberName}{permInfo}");
				}
				return false;
			}
			Logger.LogDebug($"[Mono/Security] Assembly '{filePath}' passed security validation");

			// If we have a custom context, use the context-aware loader
			if (loadContext != null) {
				var loader = new ContextAwareAssemblyLoader(loadContext);
				if (loader.LoadAssembly(dllPath)) {
					assemblyLoaders.Add(loader);
					loadedTypes.AddRange(loader.GetTypes());
					Logger.LogDebug($"[Mono/Context] Loaded assembly '{filePath}' in isolated context with {loader.GetTypes().Length} types");
					return true;
				}

				loader.Dispose();
				Logger.LogWarning($"[Mono/Context] Failed to load assembly '{filePath}' in isolated context");
				return false;
			}

			// Fallback to standard loader (no isolation)
			var standardLoader = new MonoAssemblyLoader();
			if (standardLoader.LoadAssembly(dllPath)) {
				assemblyLoaders.Add(standardLoader);
				loadedTypes.AddRange(standardLoader.GetTypes());
				Logger.LogDebug($"[Mono/C#] Loaded native assembly '{filePath}' with {standardLoader.GetTypes().Length} types");
				return true;
			}

			standardLoader.Dispose();
			Logger.LogWarning($"[Mono/C#] Failed to load native assembly '{filePath}'");
			return false;
		}
	}

	/// <summary>
	/// Assembly loader that uses a ModAssemblyLoadContext for isolation.
	/// </summary>
	internal class ContextAwareAssemblyLoader : IModAssemblyLoader {
		private readonly ModAssemblyLoadContext _context;
		private          System.Reflection.Assembly _assembly;
		private readonly List<Type> _types = new();
		private          bool _disposed;
		private          string _assemblyPath;

		public bool IsLoaded => _assembly != null && !_disposed;
		public ModLoaderType LoaderType => ModLoaderType.Mono;

		public ContextAwareAssemblyLoader(ModAssemblyLoadContext context) {
			_context = context ?? throw new ArgumentNullException(nameof(context));
		}

		public bool LoadAssembly(string assemblyPath) {
			if (_disposed)
				throw new ObjectDisposedException(nameof(ContextAwareAssemblyLoader));

			_assemblyPath = assemblyPath;
			_assembly = _context.LoadAssemblyFromPath(assemblyPath);

			if (_assembly != null) {
				_types.AddRange(_context.Types);
				return true;
			}

			return false;
		}

		public Type[] GetTypes() => _types.ToArray();

		public Type GetType(string typeName) {
			return _types.Find(t => t.FullName == typeName || t.Name == typeName);
		}

		public object CreateInstance(string typeName) {
			return _context.CreateInstance(typeName);
		}

		public object InvokeMethod(object instance, string methodName, object[] args) {
			if (instance == null || string.IsNullOrEmpty(methodName))
				return null;

			try {
				var method = instance.GetType().GetMethod(
					methodName,
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
				);
				return method?.Invoke(instance, args);
			} catch (Exception ex) {
				Logger.LogError($"[ContextLoader] Failed to invoke method '{methodName}': {ex.Message}");
				return null;
			}
		}

		public void Unload() {
			// The context handles unloading - we just clear our references
			_types.Clear();
			_assembly = null;
		}

		public void Dispose() {
			if (_disposed) return;
			Unload();
			_disposed = true;
		}
	}
}
#endif
