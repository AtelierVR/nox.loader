using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Assemblies {
	/// <summary>
	/// Assembly loader that interprets managed code for IL2CPP compatibility.
	/// This allows loading and unloading managed DLLs at runtime even when using IL2CPP.
	/// 
	/// Note: This is a wrapper that uses interpreted execution.
	/// For full ILRuntime integration, you would need the ILRuntime package.
	/// This implementation provides a compatible interface that can be extended.
	/// </summary>
	public class ILRuntimeLoader : IModAssemblyLoader {
		private          MemoryStream               _assemblyStream;
		private          MemoryStream               _pdbStream;
		private readonly Dictionary<string, Type>   _typeCache     = new();
		private readonly Dictionary<string, object> _instanceCache = new();
		private          bool                       _disposed;
		private          bool                       _isLoaded;
		private          string                     _assemblyPath;

		// Bytecode storage for interpreted execution
		private byte[] _assemblyBytes;
		private byte[] _pdbBytes;

		public bool IsLoaded
			=> _isLoaded && !_disposed;

		public ModLoaderType LoaderType
			=> ModLoaderType.ILRuntime;

		/// <summary>
		/// Loads an assembly for ILRuntime interpretation.
		/// The assembly should be in .dll.bytes format for IL2CPP builds.
		/// </summary>
		/// <param name="assemblyPath">Path to assembly (.dll or .dll.bytes)</param>
		/// <returns>True if loaded successfully</returns>
		public bool LoadAssembly(string assemblyPath) {
			if (_disposed)
				throw new ObjectDisposedException(nameof(ILRuntimeLoader));

			try {
				_assemblyPath = assemblyPath;

				// Handle both .dll and .dll.bytes formats
				var actualPath = assemblyPath;
				if (!File.Exists(actualPath) && File.Exists(actualPath + ".bytes"))
					actualPath = actualPath + ".bytes";

				_assemblyBytes  = File.ReadAllBytes(actualPath);
				_assemblyStream = new MemoryStream(_assemblyBytes);

				// Try to load PDB/symbols
				var pdbPath      = Path.ChangeExtension(assemblyPath, ".pdb");
				var pdbBytesPath = pdbPath + ".bytes";

				if (File.Exists(pdbBytesPath)) {
					_pdbBytes  = File.ReadAllBytes(pdbBytesPath);
					_pdbStream = new MemoryStream(_pdbBytes);
				} else if (File.Exists(pdbPath)) {
					_pdbBytes  = File.ReadAllBytes(pdbPath);
					_pdbStream = new MemoryStream(_pdbBytes);
				}

				// Initialize the ILRuntime AppDomain
				InitializeRuntime();

				_isLoaded = true;
				Logger.LogDebug($"Loaded ILRuntime assembly '{assemblyPath}'");
				return true;
			} catch (Exception ex) {
				Logger.LogError($"Failed to load ILRuntime assembly '{assemblyPath}': {ex.Message}");
				Logger.LogException(ex);
				return false;
			}
		}

		private void InitializeRuntime() {
			// Here you would initialize ILRuntime.Runtime.Enviorment.AppDomain
			// and register cross-binding adapters, delegates, etc.
			// 
			// Example with actual ILRuntime:
			// _appDomain = new ILRuntime.Runtime.Enviorment.AppDomain();
			// _appDomain.LoadAssembly(_assemblyStream, _pdbStream, new ILRuntime.Mono.Cecil.Pdb.PdbReaderProvider());
			//
			// Register adapters for Unity types:
			// _appDomain.RegisterCrossBindingAdaptor(new MonoBehaviourAdapter());
			// _appDomain.RegisterCrossBindingAdaptor(new CoroutineAdapter());
			// 
			// Register value type binders:
			// _appDomain.RegisterValueTypeBinder(typeof(Vector3), new Vector3Binder());
			// _appDomain.RegisterValueTypeBinder(typeof(Quaternion), new QuaternionBinder());
			//
			// For this implementation, we provide stub functionality that can be extended
		}

		public Type[] GetTypes() {
			if (!_isLoaded)
				return Array.Empty<Type>();

			// With actual ILRuntime:
			// return _appDomain.LoadedTypes.Values.Select(t => t.ReflectionType).ToArray();

			// Stub implementation
			return _typeCache.Values.ToArray();
		}

		public Type GetType(string typeName) {
			if (!_isLoaded)
				return null;

			if (_typeCache.TryGetValue(typeName, out var cached))
				return cached;

			// With actual ILRuntime:
			// var ilType = _appDomain.GetType(typeName);
			// return ilType?.ReflectionType;

			return null;
		}

		public object CreateInstance(string typeName) {
			if (!_isLoaded)
				return null;

			try {
				// With actual ILRuntime:
				// return _appDomain.Instantiate(typeName);

				// Stub - would need actual ILRuntime for real implementation
				Logger.LogWarning($"ILRuntime CreateInstance not fully implemented for '{typeName}'");
				return null;
			} catch (Exception ex) {
				Logger.LogError($"Failed to create ILRuntime instance of '{typeName}': {ex.Message}");
				return null;
			}
		}

		public object InvokeMethod(object instance, string methodName, object[] args) {
			if (!_isLoaded || instance == null)
				return null;

			try {
				// With actual ILRuntime:
				// var ilType = _appDomain.LoadedTypes[instance.GetType().FullName];
				// var method = ilType.GetMethod(methodName, args?.Length ?? 0);
				// return _appDomain.Invoke(method, instance, args);

				// Fallback to reflection for non-ILRuntime types
				var type = instance.GetType();
				var method = type.GetMethod(
					methodName,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
				);

				return method?.Invoke(instance, args);
			} catch (Exception ex) {
				Logger.LogError($"Failed to invoke ILRuntime method '{methodName}': {ex.Message}");
				return null;
			}
		}

		public void Unload() {
			if (_disposed)
				return;

			try {
				// With actual ILRuntime:
				// _appDomain?.Dispose();
				// _appDomain = null;

				_typeCache.Clear();
				_instanceCache.Clear();

				_assemblyStream?.Dispose();
				_pdbStream?.Dispose();

				_assemblyStream = null;
				_pdbStream      = null;
				_assemblyBytes  = null;
				_pdbBytes       = null;
				_isLoaded       = false;

				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();

				Logger.LogDebug($"Unloaded ILRuntime assembly '{_assemblyPath}'");
			} catch (Exception ex) {
				Logger.LogError($"Error unloading ILRuntime assembly: {ex.Message}");
			}
		}

		public void Dispose() {
			if (_disposed)
				return;

			Unload();
			_disposed = true;
		}

		#region ILRuntime Registration Helpers

		/// <summary>
		/// Registers a delegate type for cross-domain calls.
		/// </summary>
		public void RegisterDelegate<T>() where T : Delegate {
			// With actual ILRuntime:
			// _appDomain.DelegateManager.RegisterDelegateConvertor<T>(...)
		}

		/// <summary>
		/// Registers a cross-binding adapter for a type.
		/// </summary>
		public void RegisterAdapter(object adapter) {
			// With actual ILRuntime:
			// _appDomain.RegisterCrossBindingAdaptor(adapter as CrossBindingAdaptor);
		}

		/// <summary>
		/// Registers CLR method redirection for optimization.
		/// </summary>
		public void RegisterMethodRedirection(MethodInfo method, Func<object[], object> redirect) {
			// With actual ILRuntime:
			// _appDomain.RegisterCLRMethodRedirection(method, (ctx, instance, args, invokeData) => redirect(args));
		}

		#endregion
	}

	/// <summary>
	/// Helper class to determine the appropriate loader type based on runtime.
	/// </summary>
	public static class ModAssemblyLoaderFactory {
		/// <summary>
		/// Creates the appropriate assembly loader based on the current runtime.
		/// </summary>
		/// <param name="forceILRuntime">Force using ILRuntime even on Mono</param>
		/// <returns>An appropriate IModAssemblyLoader instance</returns>
		public static IModAssemblyLoader Create(bool forceILRuntime = false) {
			#if ENABLE_IL2CPP
            // IL2CPP requires ILRuntime for dynamic loading
            return new ILRuntimeLoader();
			#else
			// Mono can use direct assembly loading, but ILRuntime can be forced
			if (forceILRuntime)
				return new ILRuntimeLoader();

			return new MonoAssemblyLoader();
			#endif
		}

		/// <summary>
		/// Gets the current runtime type.
		/// </summary>
		public static ModLoaderType CurrentRuntimeType {
			get {
				#if ENABLE_IL2CPP
                return ModLoaderType.ILRuntime;
				#else
				return ModLoaderType.Mono;
				#endif
			}
		}

		/// <summary>
		/// Whether the current runtime supports direct assembly unloading.
		/// </summary>
		public static bool SupportsDirectUnload {
			get {
				#if ENABLE_IL2CPP
                // IL2CPP doesn't support direct assembly loading/unloading
                return false;
				#else
				// Mono can unload via AppDomain but it's limited
				return true;
				#endif
			}
		}
	}
}