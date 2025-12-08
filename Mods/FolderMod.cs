using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Nox.ModLoader.Assemblies;
using Nox.ModLoader.Cores.Assets;
using Nox.ModLoader.Mods.Helpers;
using Nox.ModLoader.Permissions;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Mods {
	/// <summary>
	/// Mod loaded from a folder on disk.
	/// Supports both managed (DLL) and native (C++) plugins with runtime loading/unloading.
	/// Each mod has its own isolated AssemblyLoadContext for proper unloading.
	/// </summary>
	public class FolderMod : Mod {
		private readonly List<IModAssemblyLoader> _assemblyLoaders = new();
		private readonly List<NativePluginLoader> _nativeLoaders   = new();
		private readonly List<Type>               _loadedTypes     = new();
		private          bool                     _isLoaded;

		#if !ENABLE_IL2CPP
		/// <summary>
		/// Custom AssemblyLoadContext for isolating this mod's assemblies.
		/// Allows proper unloading of mod assemblies at runtime.
		/// </summary>
		private ModAssemblyLoadContext _context;
		
		/// <summary>
		/// Permission context for this mod, based on declared permissions in nox.mod.json.
		/// </summary>
		private ModPermissionContext _permissionContext;
		#endif

		public string FolderPath
			=> Metadata?.InternalData.TryGetValue("folder", out var folder) == true
				? (string)folder
				: null;

		public override AppDomain GetAppDomain() {
			#if !ENABLE_IL2CPP
			return _context?.AppDomain ?? AppDomain.CurrentDomain;
			#else
			return AppDomain.CurrentDomain;
			#endif
		}

		public override Assembly[] GetAssemblies() {
			var assemblies = new List<Assembly>();

			#if !ENABLE_IL2CPP
			// Get assemblies from the isolated context
			if (_context != null)
				assemblies.AddRange(_context.Assemblies);

			// Also check standard loaders for backwards compatibility
			foreach (var loader in _assemblyLoaders) {
				if (loader is not MonoAssemblyLoader { IsLoaded: true } monoLoader) continue;
				var types = monoLoader.GetTypes();
				if (types.Length <= 0) continue;
				var assembly = types[0].Assembly;
				if (!assemblies.Contains(assembly))
					assemblies.Add(assembly);
			}
			#endif

			return assemblies.ToArray();
		}

		internal FolderMod() {
			CoreAPI  = new CoreAPI(this);
			AssetAPI = new FolderAssetAPI(this);
		}

		public override bool IsLoaded()
			=> _isLoaded && base.IsLoaded();

		public override async UniTask<bool> Load() {
			var folderPath = FolderPath;
			if (string.IsNullOrEmpty(folderPath)) {
				Logger.LogError($"FolderMod {Metadata?.GetId()} has no folder path");
				return false;
			}

			Logger.LogDebug($"Loading folder mod {Metadata.GetId()}@{Metadata.GetVersion()} from '{folderPath}'");

			try {
				#if !ENABLE_IL2CPP
				// Create isolated AssemblyLoadContext for this mod
				_context = new ModAssemblyLoadContext(Metadata.GetId(), folderPath);
				Logger.LogDebug($"Created isolated AssemblyLoadContext for mod {Metadata.GetId()}");
				
				// Create permission context based on declared permissions
				// SECURITY: FolderMods can NEVER be kernel mods - kernel is reserved for built-in KernelMods only
				if (Metadata.IsKernel())
				{
					Logger.LogWarning($"[Security] Mod '{Metadata.GetId()}' declares 'kernel: true' but external mods cannot be kernel mods. Ignoring kernel flag.");
				}
				_permissionContext = new ModPermissionContext(
					Metadata.GetId(),
					Metadata.GetPermissions(),
					isKernel: false // FolderMods are NEVER kernel mods
				);
				Logger.LogDebug($"[Permissions] {_permissionContext.GetSummary()}");
				#endif

				// Load managed assemblies
				if (!await LoadManagedAssemblies()) {
					Logger.LogError($"Failed to load managed assemblies for mod {Metadata.GetId()}");
					return false;
				}

				// Load native plugins
				if (!LoadNativePlugins()) {
					Logger.LogError($"Failed to load native plugins for mod {Metadata.GetId()}");
					return false;
				}

				// Register assets
				if (!await AssetAPI.RegisterAssets()) {
					Logger.LogError($"Failed to register assets for mod {Metadata.GetId()}");
					return false;
				}

				_isLoaded = true;
				return await base.Load();
			} catch (Exception ex) {
				Logger.LogException(new Exception($"Exception loading folder mod {Metadata.GetId()}", ex));
				return false;
			}
		}

		private async UniTask<bool> LoadManagedAssemblies() {
			var folderPath = FolderPath;
			if (string.IsNullOrEmpty(folderPath))
				return true;

			var references = Metadata.GetReferences()
				.Where(r => r.IsCompatible())
				.ToList();

			foreach (var reference in references) {
				var filePath = reference.GetFile();
				if (string.IsNullOrEmpty(filePath))
					continue;

				var fullPath = Path.Combine(folderPath, filePath);

				#if ENABLE_IL2CPP
				if (!IL2CPPAssemblyHelper.LoadAssembly(fullPath, filePath, _assemblyLoaders, _loadedTypes))
					return false;
				#else
				if (!MonoAssemblyHelper.LoadAssembly(fullPath, filePath, _assemblyLoaders, _loadedTypes, _context, _permissionContext))
					return false;
				#endif
			}

			return true;
		}

		private bool LoadNativePlugins() {
			var folderPath = FolderPath;
			if (string.IsNullOrEmpty(folderPath))
				return true;

			// Look for native plugins in platform-specific folders
			var nativePath = GetPlatformNativePath(folderPath);
			if (!Directory.Exists(nativePath))
				return true;

			var nativeFiles = GetPlatformNativeFiles(nativePath);

			foreach (var nativePath2 in nativeFiles) {
				// Skip managed DLLs
				if (IsManagedAssembly(nativePath2))
					continue;

				var loader = new NativePluginLoader(nativePath2);
				if (loader.Load()) {
					_nativeLoaders.Add(loader);
					var result = loader.CallInitialize();
					if (result != 0)
						Logger.LogWarning($"Native plugin '{nativePath2}' Initialize returned {result}");

					Logger.LogDebug($"Loaded native plugin: {nativePath2}");
				} else {
					loader.Dispose();
				}
			}

			return true;
		}

		private static string GetPlatformNativePath(string basePath) {
			#if UNITY_STANDALONE_WIN
			return Path.Combine(basePath, "native", Environment.Is64BitProcess ? "win-x64" : "win-x86");
			#elif UNITY_STANDALONE_LINUX
            return Path.Combine(basePath, "native", "linux-x64");
			#elif UNITY_STANDALONE_OSX
            return Path.Combine(basePath, "native", "osx-x64");
			#elif UNITY_ANDROID
            return Path.Combine(basePath, "native", "android");
			#else
            return Path.Combine(basePath, "native");
			#endif
		}

		private static string[] GetPlatformNativeFiles(string nativePath) {
			#if UNITY_STANDALONE_WIN
			return !Directory.Exists(nativePath)
				? Array.Empty<string>()
				: Directory.GetFiles(nativePath, "*.dll");
			#elif UNITY_STANDALONE_OSX
			var files = new List<string>();
			files.AddRange(Directory.GetFiles(nativePath, "*.dylib"));
			files.AddRange(Directory.GetFiles(nativePath, "*.bundle"));
			return files.ToArray();
			#elif UNITY_STANDALONE_LINUX || UNITY_ANDROID
            return Directory.GetFiles(nativePath, "*.so");
			#else
            return Array.Empty<string>();
			#endif
		}

		/// <summary>
		/// Checks if a file is a managed .NET assembly.
		/// </summary>
		/// <param name="path"></param>
		/// <returns></returns>
		private static bool IsManagedAssembly(string path) {
			try {
				using var fs     = new FileStream(path, FileMode.Open, FileAccess.Read);
				using var reader = new BinaryReader(fs);

				if (reader.ReadUInt16() != 0x5A4D)
					return false;

				fs.Seek(0x3C, SeekOrigin.Begin);
				var peOffset = reader.ReadUInt32();

				fs.Seek(peOffset, SeekOrigin.Begin);
				if (reader.ReadUInt32() != 0x00004550)
					return false;

				fs.Seek(2 + 2, SeekOrigin.Current);
				fs.Seek(12, SeekOrigin.Current);
				var optionalHeaderSize = reader.ReadUInt16();

				if (optionalHeaderSize == 0)
					return false;

				fs.Seek(2, SeekOrigin.Current);
				var magic   = reader.ReadUInt16();
				var is64Bit = magic == 0x20B;

				var skipSize = is64Bit ? 104 : 88;
				fs.Seek(skipSize, SeekOrigin.Current);
				fs.Seek(14 * 8, SeekOrigin.Current);

				var cliHeaderRva  = reader.ReadUInt32();
				var cliHeaderSize = reader.ReadUInt32();

				return cliHeaderRva != 0 && cliHeaderSize != 0;
			} catch {
				return false;
			}
		}

		public override async UniTask<bool> Unload() {
			Logger.LogDebug($"Unloading folder mod {Metadata.GetId()}@{Metadata.GetVersion()}");

			try {
				if (!await base.Unload())
					return false;

				// Shutdown and unload native plugins
				for (var i = _nativeLoaders.Count - 1; i >= 0; i--) {
					var loader = _nativeLoaders[i];
					loader.CallShutdown();
					loader.Unload();
					loader.Dispose();
				}

				_nativeLoaders.Clear();

				// Unload managed assemblies
				foreach (var loader in _assemblyLoaders) {
					loader.Unload();
					loader.Dispose();
				}

				_assemblyLoaders.Clear();
				_loadedTypes.Clear();

				#if !ENABLE_IL2CPP
				// Unload the isolated AssemblyLoadContext
				if (_context != null) {
					_context.Unload();
					_context = null;
					Logger.LogDebug($"Unloaded AssemblyLoadContext for mod {Metadata.GetId()}");
				}
				#endif

				// Unregister assets
				if (!await AssetAPI.UnRegisterAssets())
					Logger.LogWarning($"Failed to unregister assets for mod {Metadata.GetId()}");

				_isLoaded = false;

				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();

				Logger.LogDebug($"Successfully unloaded folder mod {Metadata.GetId()}");
				return true;
			} catch (Exception ex) {
				Logger.LogException(new Exception($"Exception unloading folder mod {Metadata.GetId()}", ex));
				return false;
			}
		}
	}
}