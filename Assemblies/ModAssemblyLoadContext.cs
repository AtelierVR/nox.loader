#if !ENABLE_IL2CPP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Assemblies {
	/// <summary>
	/// Custom assembly context for isolating mod assemblies in Unity/Mono.
	/// Note: True assembly unloading is not supported in Mono, but this class
	/// provides logical isolation and reference tracking per mod.
	/// </summary>
	public class ModAssemblyLoadContext : IDisposable {
		private readonly string         _modId;
		private readonly string         _basePath;
		private readonly AppDomain      _appDomain;
		private readonly List<Assembly> _assemblies = new();
		private readonly List<Type>     _types      = new();
		private          bool           _disposed;

		/// <summary>
		/// Blacklist of regex patterns for assembly names that mods are not allowed to load.
		/// These patterns match sensitive system assemblies that could be used maliciously.
		/// </summary>
		private static readonly List<Regex> BlacklistPatterns = new() {
			// System security and cryptography
			new Regex(@"^System\.Security(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			
			// Reflection emit (dynamic code generation)
			new Regex(@"^System\.Reflection\.Emit(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			
			// Process and system access
			new Regex(@"^System\.Diagnostics\.(Process|Debug|Debugger)$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			new Regex(@"^System\.(Management|ServiceProcess)(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			
			// Windows registry and Win32 APIs
			new Regex(@"^Microsoft\.Win32(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			
			// Low-level networking
			new Regex(@"^System\.Net\.(Sockets|NetworkInformation|Ping)$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			
			// Code compilation and scripting
			new Regex(@"^(Microsoft\.CSharp|Mono\.CSharp)(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			new Regex(@"^System\.CodeDom(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			
			// Native interop
			new Regex(@"^System\.Runtime\.InteropServices(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			
			// Unity internals
			new Regex(@"^UnityEditor(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			new Regex(@"^Unity\.IL2CPP(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
			
			// Nox internals - prevent mods from loading core systems
			new Regex(@"^(api\.nox\.mod_loader|Nox\.ModLoader)(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
		};

		/// <summary>
		/// Lock object for thread-safe access to the blacklist.
		/// </summary>
		private static readonly object BlacklistLock = new();

		/// <summary>
		/// Adds a regex pattern to the blacklist.
		/// </summary>
		/// <param name="pattern">Regex pattern to match assembly names</param>
		/// <param name="options">Regex options (default: IgnoreCase | Compiled)</param>
		public static void AddToBlacklist(string pattern, RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Compiled) {
			if (string.IsNullOrEmpty(pattern)) return;
			
			lock (BlacklistLock) {
				try {
					BlacklistPatterns.Add(new Regex(pattern, options));
					Logger.LogDebug($"[ModContext] Added blacklist pattern: {pattern}");
				} catch (ArgumentException ex) {
					Logger.LogError($"[ModContext] Invalid regex pattern '{pattern}': {ex.Message}");
				}
			}
		}

		/// <summary>
		/// Adds a simple assembly name to the blacklist (exact match).
		/// </summary>
		/// <param name="assemblyName">Exact assembly name to block</param>
		public static void AddExactToBlacklist(string assemblyName) {
			if (string.IsNullOrEmpty(assemblyName)) return;
			AddToBlacklist($"^{Regex.Escape(assemblyName)}$");
		}

		/// <summary>
		/// Adds a prefix pattern to the blacklist (matches assembly name starting with prefix).
		/// </summary>
		/// <param name="prefix">Prefix to match</param>
		public static void AddPrefixToBlacklist(string prefix) {
			if (string.IsNullOrEmpty(prefix)) return;
			AddToBlacklist($"^{Regex.Escape(prefix)}(\\..+)?$");
		}

		/// <summary>
		/// Removes a pattern from the blacklist by its string representation.
		/// </summary>
		/// <param name="pattern">The regex pattern string to remove</param>
		/// <returns>True if a pattern was removed</returns>
		public static bool RemoveFromBlacklist(string pattern) {
			if (string.IsNullOrEmpty(pattern)) return false;
			
			lock (BlacklistLock) {
				var index = BlacklistPatterns.FindIndex(r => r.ToString() == pattern);
				if (index >= 0) {
					BlacklistPatterns.RemoveAt(index);
					Logger.LogDebug($"[ModContext] Removed blacklist pattern: {pattern}");
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Checks if an assembly name matches any blacklist pattern.
		/// </summary>
		/// <param name="assemblyName">The assembly name to check</param>
		/// <returns>True if the assembly is blacklisted</returns>
		public static bool IsBlacklisted(string assemblyName) {
			if (string.IsNullOrEmpty(assemblyName))
				return false;

			lock (BlacklistLock) {
				return BlacklistPatterns.Any(regex => regex.IsMatch(assemblyName));
			}
		}

		/// <summary>
		/// Gets the blacklist pattern that matches an assembly name.
		/// </summary>
		/// <param name="assemblyName">The assembly name to check</param>
		/// <returns>The matching pattern or null if not blacklisted</returns>
		public static string GetMatchingPattern(string assemblyName) {
			if (string.IsNullOrEmpty(assemblyName))
				return null;

			lock (BlacklistLock) {
				return BlacklistPatterns.FirstOrDefault(regex => regex.IsMatch(assemblyName))?.ToString();
			}
		}

		/// <summary>
		/// Gets a copy of all blacklist patterns.
		/// </summary>
		public static IReadOnlyList<string> GetBlacklistPatterns() {
			lock (BlacklistLock) {
				return BlacklistPatterns.Select(r => r.ToString()).ToList().AsReadOnly();
			}
		}

		/// <summary>
		/// Gets the mod identifier for this context.
		/// </summary>
		public string ModId
			=> _modId;

		/// <summary>
		/// Gets whether this context has been unloaded.
		/// </summary>
		public bool IsUnloaded
			=> _disposed;

		/// <summary>
		/// Gets all loaded types from this context.
		/// </summary>
		public IReadOnlyList<Type> Types
			=> _types;

		/// <summary>
		/// Gets all loaded assemblies from this context.
		/// </summary>
		public IReadOnlyList<Assembly> Assemblies
			=> _assemblies;

		/// <summary>
		/// Gets the AppDomain for this context.
		/// </summary>
		public AppDomain AppDomain
			=> _appDomain;

		/// <summary>
		/// Creates a new ModAssemblyLoadContext for the specified mod.
		/// </summary>
		/// <param name="modId">The mod identifier</param>
		/// <param name="basePath">Base path for resolving dependencies</param>
		public ModAssemblyLoadContext(string modId, string basePath) {
			_appDomain = AppDomain.CurrentDomain;
			_modId     = modId;
			_basePath  = basePath;

			// Subscribe to assembly resolution for this mod's dependencies
			AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

			Logger.LogDebug($"[ModContext] Created context for mod '{modId}'");
		}

		/// <summary>
		/// Handles assembly resolution for mod dependencies.
		/// </summary>
		private Assembly OnAssemblyResolve(object sender, ResolveEventArgs args) {
			if (_disposed) return null;

			var assemblyName = new AssemblyName(args.Name);
			
			// Check blacklist before resolving
			if (IsBlacklisted(assemblyName.Name)) {
				Logger.LogWarning($"[ModContext:{_modId}] Blocked blacklisted assembly reference: '{assemblyName.Name}'");
				return null;
			}

			var assemblyPath = Path.Combine(_basePath, $"{assemblyName.Name}.dll");

			if (File.Exists(assemblyPath)) {
				Logger.LogDebug($"[ModContext:{_modId}] Resolving dependency '{assemblyName.Name}' from mod folder");
				return LoadAssemblyFromPath(assemblyPath);
			}

			return null;
		}

		/// <summary>
		/// Loads an assembly from the specified path into this context.
		/// </summary>
		/// <param name="assemblyPath">Path to the assembly file</param>
		/// <returns>The loaded assembly, or null if loading failed</returns>
		public Assembly LoadAssemblyFromPath(string assemblyPath) {
			if (_disposed)
				throw new ObjectDisposedException(nameof(ModAssemblyLoadContext));

			// Check if the assembly name is blacklisted
			var fileName = Path.GetFileNameWithoutExtension(assemblyPath);
			if (IsBlacklisted(fileName)) {
				Logger.LogWarning($"[ModContext:{_modId}] Blocked loading of blacklisted assembly: '{fileName}'");
				return null;
			}

			try {
				// Load assembly bytes to avoid file locking
				var    assemblyBytes = File.ReadAllBytes(assemblyPath);
				byte[] pdbBytes      = null;

				// Try to load PDB for debugging
				var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
				if (File.Exists(pdbPath))
					pdbBytes = File.ReadAllBytes(pdbPath);

				// Load from bytes
				var assembly = pdbBytes != null
					? Assembly.Load(assemblyBytes, pdbBytes)
					: Assembly.Load(assemblyBytes);

				// Check for blacklisted references
				var blacklistedRefs = ValidateAssemblyReferences(assembly);
				if (blacklistedRefs.Count > 0) {
					Logger.LogWarning($"[ModContext:{_modId}] Assembly '{fileName}' references blacklisted assemblies: {string.Join(", ", blacklistedRefs)}");
					// Note: Assembly is already loaded at this point in Mono, but we don't add it to our tracked list
					return null;
				}

				_assemblies.Add(assembly);

				// Cache types
				try {
					_types.AddRange(assembly.GetTypes());
				} catch (ReflectionTypeLoadException ex) {
					foreach (var type in ex.Types)
						if (type != null)
							_types.Add(type);
					Logger.LogWarning($"[ModContext:{_modId}] Some types failed to load from '{assemblyPath}': {ex.Message}");
				}

				Logger.LogDebug($"[ModContext:{_modId}] Loaded assembly '{Path.GetFileName(assemblyPath)}' with {_types.Count} types");
				return assembly;
			} catch (Exception ex) {
				Logger.LogError($"[ModContext:{_modId}] Failed to load assembly '{assemblyPath}': {ex.Message}");
				Logger.LogException(ex);
				return null;
			}
		}

		/// <summary>
		/// Validates that an assembly doesn't reference blacklisted assemblies.
		/// </summary>
		/// <param name="assembly">The assembly to validate</param>
		/// <returns>List of blacklisted assembly names that are referenced</returns>
		private static List<string> ValidateAssemblyReferences(Assembly assembly) {
			var blacklistedRefs = new List<string>();
			
			try {
				foreach (var refAssembly in assembly.GetReferencedAssemblies()) {
					if (IsBlacklisted(refAssembly.Name)) {
						blacklistedRefs.Add(refAssembly.Name);
					}
				}
			} catch (Exception ex) {
				Logger.LogWarning($"Failed to validate assembly references: {ex.Message}");
			}

			return blacklistedRefs;
		}

		/// <summary>
		/// Gets a type by name from this context.
		/// </summary>
		public Type GetType(string typeName) {
			return _types.Find(t => t.FullName == typeName || t.Name == typeName);
		}

		/// <summary>
		/// Creates an instance of the specified type.
		/// </summary>
		public object CreateInstance(string typeName) {
			var type = GetType(typeName);
			if (type == null)
				return null;

			try {
				return Activator.CreateInstance(type);
			} catch (Exception ex) {
				Logger.LogError($"[ModContext:{_modId}] Failed to create instance of '{typeName}': {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Unloads this context and clears all references.
		/// Note: In Mono, assemblies cannot be truly unloaded, but references are cleared.
		/// </summary>
		public void Unload() {
			if (_disposed)
				return;

			Logger.LogDebug($"[ModContext:{_modId}] Unloading context with {_assemblies.Count} assemblies");

			// Unsubscribe from assembly resolution
			AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;

			_types.Clear();
			_assemblies.Clear();
			_disposed = true;

			// Force garbage collection to help clean up
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			Logger.LogDebug($"[ModContext:{_modId}] Context unloaded (references cleared)");
		}

		public void Dispose() {
			if (_disposed)
				return;

			Unload();
		}
	}
}
#endif