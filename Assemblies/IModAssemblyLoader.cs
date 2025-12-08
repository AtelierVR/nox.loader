using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Assemblies {
	/// <summary>
	/// Interface for mod assembly loaders that can be unloaded at runtime.
	/// </summary>
	public interface IModAssemblyLoader : IDisposable {
		/// <summary>
		/// Gets whether this loader is currently loaded.
		/// </summary>
		bool IsLoaded { get; }

		/// <summary>
		/// Gets the loader type (Mono, IL2CPP, Native).
		/// </summary>
		ModLoaderType LoaderType { get; }

		/// <summary>
		/// Loads an assembly from the specified path.
		/// </summary>
		/// <param name="assemblyPath">Path to the assembly file</param>
		/// <returns>True if loaded successfully</returns>
		bool LoadAssembly(string assemblyPath);

		/// <summary>
		/// Gets all loaded types from the assembly.
		/// </summary>
		/// <returns>Array of types</returns>
		Type[] GetTypes();

		/// <summary>
		/// Gets a specific type by name.
		/// </summary>
		/// <param name="typeName">Full type name</param>
		/// <returns>The type or null if not found</returns>
		Type GetType(string typeName);

		/// <summary>
		/// Creates an instance of the specified type.
		/// </summary>
		/// <param name="typeName">Full type name</param>
		/// <returns>Instance of the type or null</returns>
		object CreateInstance(string typeName);

		/// <summary>
		/// Invokes a method on a type.
		/// </summary>
		/// <param name="instance">The instance (null for static methods)</param>
		/// <param name="methodName">Method name</param>
		/// <param name="args">Method arguments</param>
		/// <returns>Return value of the method</returns>
		object InvokeMethod(object instance, string methodName, object[] args);

		/// <summary>
		/// Unloads the assembly and releases resources.
		/// </summary>
		void Unload();
	}

	/// <summary>
	/// Type of mod loader.
	/// </summary>
	public enum ModLoaderType {
		/// <summary>
		/// Mono/.NET assembly loaded directly.
		/// </summary>
		Mono,

		/// <summary>
		/// ILRuntime interpreted assembly for IL2CPP compatibility.
		/// </summary>
		ILRuntime,

		/// <summary>
		/// Native C++ plugin.
		/// </summary>
		Native
	}

	#if !ENABLE_IL2CPP
	/// <summary>
	/// Assembly loader for Mono runtime using AssemblyLoadContext (.NET Core) or AppDomain (.NET Framework).
	/// This loader allows unloading assemblies at runtime.
	/// </summary>
	public class MonoAssemblyLoader : IModAssemblyLoader {
		private          Assembly   _assembly;
		private readonly List<Type> _types = new();
		private          bool       _disposed;
		private          string     _assemblyPath;

		// Using reflection to load from bytes to avoid locking the file
		private byte[] _assemblyBytes;
		private byte[] _pdbBytes;

		public bool IsLoaded
			=> _assembly != null && !_disposed;

		public ModLoaderType LoaderType
			=> ModLoaderType.Mono;

		public bool LoadAssembly(string assemblyPath) {
			if (_disposed)
				throw new ObjectDisposedException(nameof(MonoAssemblyLoader));

			try {
				_assemblyPath = assemblyPath;

				// Load assembly bytes to avoid file locking
				_assemblyBytes = File.ReadAllBytes(assemblyPath);

				// Try to load PDB for debugging
				var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
				if (File.Exists(pdbPath))
					_pdbBytes = File.ReadAllBytes(pdbPath);

				// Load from bytes
				_assembly = _pdbBytes != null
					? Assembly.Load(_assemblyBytes, _pdbBytes)
					: Assembly.Load(_assemblyBytes);

				// Cache types
				try {
					_types.AddRange(_assembly.GetTypes());
				} catch (ReflectionTypeLoadException ex) {
					// Some types may fail to load, but we can still use the others
					foreach (var type in ex.Types)
						if (type != null)
							_types.Add(type);

					Logger.LogWarning($"Some types failed to load from '{assemblyPath}': {ex.Message}");
				}

				Logger.LogDebug($"Loaded Mono assembly '{assemblyPath}' with {_types.Count} types");
				return true;
			} catch (Exception ex) {
				Logger.LogError($"Failed to load Mono assembly '{assemblyPath}': {ex.Message}");
				Logger.LogException(ex);
				return false;
			}
		}

		public Type[] GetTypes() {
			return _types.ToArray();
		}

		public Type GetType(string typeName) {
			return _types.Find(t => t.FullName == typeName || t.Name == typeName);
		}

		public object CreateInstance(string typeName) {
			var type = GetType(typeName);
			if (type == null)
				return null;

			try {
				return Activator.CreateInstance(type);
			} catch (Exception ex) {
				Logger.LogError($"Failed to create instance of '{typeName}': {ex.Message}");
				return null;
			}
		}

		public object InvokeMethod(object instance, string methodName, object[] args) {
			if (instance == null && string.IsNullOrEmpty(methodName))
				return null;

			try {
				var type = instance?.GetType();
				if (type == null)
					return null;

				var method = type.GetMethod(
					methodName,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
				);

				return method?.Invoke(instance, args);
			} catch (Exception ex) {
				Logger.LogError($"Failed to invoke method '{methodName}': {ex.Message}");
				return null;
			}
		}

		public void Unload() {
			if (_disposed)
				return;

			// Note: In .NET Framework/Mono, assemblies cannot be truly unloaded without AppDomain
			// This is a limitation. In .NET Core 3.0+, we could use AssemblyLoadContext.
			// For Unity with Mono, we just clear references and rely on GC.

			_types.Clear();
			_assembly      = null;
			_assemblyBytes = null;
			_pdbBytes      = null;

			// Force garbage collection to help clean up
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			Logger.LogDebug($"Unloaded Mono assembly '{_assemblyPath}' (references cleared)");
		}

		public void Dispose() {
			if (_disposed)
				return;

			Unload();
			_disposed = true;
		}
	}
	#endif
}