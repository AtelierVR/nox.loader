using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Nox.CCK.Utils;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Assemblies {
	/// <summary>
	/// Handles loading and unloading of native C++ plugins at runtime.
	/// This is designed to work with both Mono and IL2CPP backends.
	/// </summary>
	public class NativePluginLoader : IDisposable {
		private          IntPtr                       _libraryHandle = IntPtr.Zero;
		private readonly string                       _libraryPath;
		private readonly Dictionary<string, Delegate> _delegateCache = new();
		private          bool                         _disposed;

		public string LibraryPath
			=> _libraryPath;

		public bool IsLoaded
			=> _libraryHandle != IntPtr.Zero && !_disposed;

		#region Platform-specific Native Methods

		#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		private const string Kernel32 = "kernel32.dll";

		[DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Ansi)]
		private static extern IntPtr LoadLibrary(string lpFileName);

		[DllImport(Kernel32, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool FreeLibrary(IntPtr hModule);

		[DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Ansi)]
		private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

		private static IntPtr LoadNativeLibrary(string path)
			=> LoadLibrary(path);

		private static bool UnloadNativeLibrary(IntPtr handle)
			=> FreeLibrary(handle);

		private static IntPtr GetNativeFunction(IntPtr handle, string name)
			=> GetProcAddress(handle, name);
		#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || UNITY_ANDROID
        private const string Libdl = "libdl.so.2";
        private const int RTLD_NOW = 2;

        [DllImport(Libdl)]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport(Libdl)]
        private static extern int dlclose(IntPtr handle);

        [DllImport(Libdl)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport(Libdl)]
        private static extern IntPtr dlerror();

        private static IntPtr LoadNativeLibrary(string path) => dlopen(path, RTLD_NOW);
        private static bool UnloadNativeLibrary(IntPtr handle) => dlclose(handle) == 0;
        private static IntPtr GetNativeFunction(IntPtr handle, string name) => dlsym(handle, name);
		#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private const string Libdl = "libdl.dylib";
        private const int RTLD_NOW = 2;

        [DllImport(Libdl)]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport(Libdl)]
        private static extern int dlclose(IntPtr handle);

        [DllImport(Libdl)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        private static IntPtr LoadNativeLibrary(string path) => dlopen(path, RTLD_NOW);
        private static bool UnloadNativeLibrary(IntPtr handle) => dlclose(handle) == 0;
        private static IntPtr GetNativeFunction(IntPtr handle, string name) => dlsym(handle, name);
		#elif UNITY_IOS
        // iOS doesn't support dynamic loading of native libraries
        private static IntPtr LoadNativeLibrary(string path) => IntPtr.Zero;
        private static bool UnloadNativeLibrary(IntPtr handle) => false;
        private static IntPtr GetNativeFunction(IntPtr handle, string name) => IntPtr.Zero;
		#else
        private static IntPtr LoadNativeLibrary(string path) => IntPtr.Zero;
        private static bool UnloadNativeLibrary(IntPtr handle) => false;
        private static IntPtr GetNativeFunction(IntPtr handle, string name) => IntPtr.Zero;
		#endif

		#endregion

		/// <summary>
		/// Creates a new native plugin loader for the specified library.
		/// </summary>
		/// <param name="libraryPath">Path to the native library</param>
		public NativePluginLoader(string libraryPath) {
			if (string.IsNullOrEmpty(libraryPath))
				throw new ArgumentNullException(nameof(libraryPath));

			_libraryPath = GetPlatformLibraryPath(libraryPath);
		}

		/// <summary>
		/// Gets the platform-specific library path.
		/// </summary>
		private static string GetPlatformLibraryPath(string basePath) {
			var directory = Path.GetDirectoryName(basePath);
			var fileName  = Path.GetFileNameWithoutExtension(basePath);

			// Remove lib prefix if present for consistency
			if (fileName.StartsWith("lib"))
				fileName = fileName.Substring(3);

			#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			return Path.Combine(directory ?? "", $"{fileName}.dll");
			#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            // Try .dylib first, then .bundle
            var dylibPath = Path.Combine(directory ?? "", $"lib{fileName}.dylib");
            if (File.Exists(dylibPath))
                return dylibPath;
            return Path.Combine(directory ?? "", $"{fileName}.bundle");
			#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || UNITY_ANDROID
            return Path.Combine(directory ?? "", $"lib{fileName}.so");
			#else
            return basePath;
			#endif
		}

		/// <summary>
		/// Loads the native library.
		/// </summary>
		/// <returns>True if loaded successfully</returns>
		public bool Load() {
			if (_disposed)
				throw new ObjectDisposedException(nameof(NativePluginLoader));

			if (IsLoaded)
				return true;

			try {
				if (!File.Exists(_libraryPath)) {
					Logger.LogError($"Native library not found: {_libraryPath}");
					return false;
				}

				_libraryHandle = LoadNativeLibrary(_libraryPath);

				if (_libraryHandle == IntPtr.Zero) {
					Logger.LogError($"Failed to load native library: {_libraryPath}");
					return false;
				}

				Logger.LogDebug($"Loaded native library: {_libraryPath}");
				return true;
			} catch (Exception ex) {
				Logger.LogError($"Exception loading native library '{_libraryPath}': {ex.Message}");
				Logger.LogException(ex);
				return false;
			}
		}

		/// <summary>
		/// Gets a function pointer from the loaded library.
		/// </summary>
		/// <param name="functionName">Name of the exported function</param>
		/// <returns>Function pointer or IntPtr.Zero if not found</returns>
		public IntPtr GetFunction(string functionName) {
			if (!IsLoaded) {
				Logger.LogWarning($"Cannot get function '{functionName}' - library not loaded");
				return IntPtr.Zero;
			}

			var ptr = GetNativeFunction(_libraryHandle, functionName);
			if (ptr == IntPtr.Zero)
				Logger.LogWarning($"Function '{functionName}' not found in '{_libraryPath}'");

			return ptr;
		}

		/// <summary>
		/// Gets a delegate for a native function.
		/// </summary>
		/// <typeparam name="T">Delegate type matching the native function signature</typeparam>
		/// <param name="functionName">Name of the exported function</param>
		/// <returns>Delegate or null if not found</returns>
		public T GetDelegate<T>(string functionName) where T : Delegate {
			var cacheKey = $"{typeof(T).FullName}:{functionName}";

			if (_delegateCache.TryGetValue(cacheKey, out var cached))
				return (T)cached;

			var ptr = GetFunction(functionName);
			if (ptr == IntPtr.Zero)
				return null;

			try {
				var del = Marshal.GetDelegateForFunctionPointer<T>(ptr);
				_delegateCache[cacheKey] = del;
				return del;
			} catch (Exception ex) {
				Logger.LogError($"Failed to create delegate for '{functionName}': {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Unloads the native library.
		/// </summary>
		/// <returns>True if unloaded successfully</returns>
		public bool Unload() {
			if (_disposed || _libraryHandle == IntPtr.Zero)
				return true;

			try {
				// Clear delegate cache first
				_delegateCache.Clear();

				// Force garbage collection to release any references
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();

				var result = UnloadNativeLibrary(_libraryHandle);

				if (result) {
					Logger.LogDebug($"Unloaded native library: {_libraryPath}");
					_libraryHandle = IntPtr.Zero;
				} else {
					Logger.LogError($"Failed to unload native library: {_libraryPath}");
				}

				return result;
			} catch (Exception ex) {
				Logger.LogError($"Exception unloading native library '{_libraryPath}': {ex.Message}");
				return false;
			}
		}

		public void Dispose() {
			if (_disposed)
				return;

			Unload();
			_disposed = true;
		}

		#region Common Native Plugin Interface

		// Common function signatures for mod plugins

		/// <summary>
		/// Delegate for plugin initialization.
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate int InitializeDelegate();

		/// <summary>
		/// Delegate for plugin shutdown.
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ShutdownDelegate();

		/// <summary>
		/// Delegate for getting plugin version.
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate IntPtr GetVersionDelegate();

		/// <summary>
		/// Delegate for plugin update tick.
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void UpdateDelegate(float deltaTime);

		/// <summary>
		/// Calls the plugin's Initialize function if it exists.
		/// </summary>
		/// <returns>Result code from Initialize, or -1 if not found</returns>
		public int CallInitialize() {
			var init = GetDelegate<InitializeDelegate>("NoxMod_Initialize");
			return init?.Invoke() ?? -1;
		}

		/// <summary>
		/// Calls the plugin's Shutdown function if it exists.
		/// </summary>
		public void CallShutdown() {
			var shutdown = GetDelegate<ShutdownDelegate>("NoxMod_Shutdown");
			shutdown?.Invoke();
		}

		/// <summary>
		/// Gets the plugin version string if available.
		/// </summary>
		public string GetVersion() {
			var getVersion = GetDelegate<GetVersionDelegate>("NoxMod_GetVersion");
			var ptr        = getVersion?.Invoke() ?? IntPtr.Zero;
			return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
		}

		/// <summary>
		/// Calls the plugin's Update function if it exists.
		/// </summary>
		public void CallUpdate(float deltaTime) {
			var update = GetDelegate<UpdateDelegate>("NoxMod_Update");
			update?.Invoke(deltaTime);
		}

		#endregion

		/// <summary>
		/// Checks if a file is a valid native library for the current platform.
		/// </summary>
		public static bool IsValidNativeLibrary(string path) {
			if (!File.Exists(path))
				return false;

			var ext = Path.GetExtension(path).ToLowerInvariant();

			#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			return ext == ".dll";
			#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return ext == ".dylib" || ext == ".bundle";
			#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || UNITY_ANDROID
            return ext == ".so";
			#else
            return false;
			#endif
		}
	}
}