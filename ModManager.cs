using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.ModLoader.Discovers;
using Nox.ModLoader.Mods;
using Nox.ModLoader.Permissions;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader {
	public class ModManager {
		public static List<Mod> Mods { get; private set; } = new();
		private static bool _initialized;
		
		/// <summary>
		/// Event fired when a mod is loaded.
		/// </summary>
		public static event Action<Mod> OnModLoaded;
		
		/// <summary>
		/// Event fired when a mod is unloaded.
		/// </summary>
		public static event Action<Mod> OnModUnloaded;
		
		/// <summary>
		/// Initialize the mod manager and permission system.
		/// </summary>
		private static void EnsureInitialized() {
			if (_initialized) return;
			_initialized = true;
			
			// Initialize permission registry
			PermissionRegistry.Initialize();
			Logger.LogDebug("[ModManager] Permission registry initialized");
		}

		public static Mod[] GetMods()
			=> Mods.ToArray();

		public static Mod GetMod(string id)
			=> Mods.Find(x => x.GetMetadata().Match(id));
		
		/// <summary>
		/// Checks if a mod is currently loaded.
		/// </summary>
		public static bool IsModLoaded(string id)
			=> GetMod(id)?.IsLoaded() ?? false;

		public static UniTask<ResultLoadInfos> LoadMods(string[] ids)
			=> LoadMods(ids, GlobalDiscover.Instance);

		public static UniTask<ResultLoadInfos> LoadMods()
			=> LoadMods(GlobalDiscover.Instance);

		public static async UniTask<ResultLoadInfos> LoadMods(IDiscover discover) {
			EnsureInitialized();
			
			var mods     = new List<Mod>();
			var packages = discover.FindAllPackages();

			foreach (var package in packages)
				mods.Add(package.InternalDDiscover.CreateMod(package));

			return await PrepareMods(mods.ToArray());
		}

		public static async UniTask<ResultLoadInfos> LoadMods(string[] ids, IDiscover discover)
			=> await PrepareMods(
				(from id in ids
				select discover.FindPackage(id)
				into package
				where package != null
				select package.InternalDDiscover.CreateMod(package))
				.ToArray()
			);

		private static ResultLoad[] CheckModHaveMissingDependencies(Mod mod, Mod[] mods) {
			var metadata = mod.GetMetadata();
			if (metadata == null)
				return new[] {
					new ResultLoad {
						Type   = ResultLoad.ResultType.NoMetadata,
						ForMod = "<unknown>"
					}
				};

			List<ResultLoad> results = new();
			List<Mod>        allMods = new();
			allMods.AddRange(Mods);
			allMods.AddRange(mods);

			foreach (var dependency in metadata.GetDepends()) {
				var isDepend = allMods.Any(currentMod => currentMod.GetMetadata().Match(dependency));

				if (!isDepend)
					results.Add(
						new ResultLoad {
							Type     = ResultLoad.ResultType.MissingDependency,
							Message  = $"Missing dependency {dependency.GetId()}@{dependency.GetVersion()} for {metadata.GetId()}@{metadata.GetVersion()}",
							CausedBy = metadata.GetId(),
							ForMod   = dependency.GetId()
						}
					);
			}

			return results.ToArray();
		}

		// Check if mod breaks other mods, if have any mod that breaks, don't load the mod
		private static ResultLoad[] CheckModBreakOtherMod(Mod mod, Mod[] mods) {
			var metadata = mod.GetMetadata();
			if (metadata == null)
				return new[] {
					new ResultLoad {
						Type   = ResultLoad.ResultType.NoMetadata,
						ForMod = "<unknown>"
					}
				};

			List<Mod> allMods = new();
			allMods.AddRange(Mods);
			allMods.AddRange(mods);

			return (from dependency in metadata.GetBreaks()
				from currentMod in allMods
				where currentMod.GetMetadata().Match(dependency)
				select new ResultLoad {
					Type = ResultLoad.ResultType.MissingDependency,
					Message =
						$"Mod {metadata.GetId()}@{metadata.GetVersion()} breaks {dependency.GetId()}@{dependency.GetVersion()}",
					CausedBy = metadata.GetId(),
					ForMod   = dependency.GetId()
				})
				.ToArray();
		}

		// Check if mod conflicts with other mods, if have any mod that conflicts, you can load the mod but show a warning
		private static ResultLoad[] CheckModConflictOtherMod(Mod mod, Mod[] mods) {
			var metadata = mod.GetMetadata();
			if (metadata == null)
				return new[] {
					new ResultLoad {
						Type   = ResultLoad.ResultType.NoMetadata,
						ForMod = "<unknown>"
					}
				};

			List<Mod> allMods = new();
			allMods.AddRange(Mods);
			allMods.AddRange(mods);

			return (from dependency in metadata.GetConflicts()
				from currentMod in allMods
				where currentMod.GetMetadata().Match(dependency)
				select new ResultLoad {
					Type = ResultLoad.ResultType.MissingDependency,
					Message =
						$"Mod {metadata.GetId()}@{metadata.GetVersion()} conflicts with {dependency.GetId()}@{dependency.GetVersion()}",
					CausedBy = metadata.GetId(),
					ForMod   = dependency.GetId()
				})
				.ToArray();
		}

		private static ResultLoad CheckModIsAlreadyLoaded(Mod mod) {
			var metadata = mod.GetMetadata();
			if (metadata == null)
				return new ResultLoad {
					Type   = ResultLoad.ResultType.NoMetadata,
					ForMod = "<unknown>"
				};

			if (Mods.Any(currentMod => currentMod.GetMetadata().Match(metadata)))
				return new ResultLoad {
					Type    = ResultLoad.ResultType.AlreadyLoaded,
					Message = $"Mod {metadata.GetId()}@{metadata.GetVersion()} is already loaded",
					ForMod  = metadata.GetId()
				};

			return null;
		}

		private static void GetRelations(Mod mod, Mod[] inLoading, ref List<Mod> dependencies) {
			var metadata = mod.GetMetadata();
			if (metadata == null)
				return;

			List<Mod> allMods = new();
			allMods.AddRange(Mods);
			allMods.AddRange(inLoading);

			foreach (var dependency in metadata.GetRelations()) {
				var depend = allMods.FirstOrDefault(currentMod => currentMod.GetMetadata().Match(dependency));
				if (depend == null) {
					Logger.LogWarning($"Mod {metadata.GetId()}@{metadata.GetVersion()} has a missing relation {dependency.GetId()}@{dependency.GetVersion()}");
					continue;
				}

				if (dependencies.Contains(depend))
					continue;

				dependencies.Add(depend);
				GetRelations(depend, inLoading, ref dependencies);
			}
		}

		private static async UniTask<ResultLoadInfos> PrepareMods(Mod[] mods) {
			var results = new List<ResultLoad>();

			// Checking mods
			List<Mod> verifiedMods = new();

			foreach (var mod in mods) {
				var alreadyLoaded       = CheckModIsAlreadyLoaded(mod);
				var missingDependencies = CheckModHaveMissingDependencies(mod, mods);
				var breaks              = CheckModBreakOtherMod(mod, mods);
				var conflicts           = CheckModConflictOtherMod(mod, mods);

				List<ResultLoad> allResults = new();
				if (alreadyLoaded != null)
					allResults.Add(alreadyLoaded);
				allResults.AddRange(missingDependencies);
				allResults.AddRange(breaks);
				allResults.AddRange(conflicts);

				if (allResults.Count > 0) {
					results.AddRange(allResults);

					if (allResults.Exists(x => x.IsError))
						continue;
				}

				verifiedMods.Add(mod);
			}

			var errorResults = results.FindAll(x => x.IsError);
			if (errorResults.Count > 0)
				return new ResultLoadInfos { Mods = Array.Empty<Mod>(), Results = results.ToArray() };

			List<Mod> sortedMods = new();
			foreach (var result in verifiedMods) {
				var i         = 0;
				var relations = new List<Mod>();
				GetRelations(result, verifiedMods.ToArray(), ref relations);
				for (; i < sortedMods.Count; i++)
					if (relations.Contains(sortedMods[i]))
						break;
				sortedMods.Insert(i, result);
			}

			sortedMods.Reverse();
			verifiedMods = sortedMods;

			// Loading mods
			List<Mod> loadedMods = new();

			foreach (var mod in verifiedMods) {
				if (mod.IsLoaded()) {
					loadedMods.Add(mod);
					continue;
				}

				if (!await mod.Load())
					results.Add(
						new ResultLoad {
							Type    = ResultLoad.ResultType.LoadError,
							Message = $"Failed to load mod {mod.GetMetadata().GetId()}({mod.GetMetadata().GetVersion()})",
							ForMod  = mod.GetMetadata().GetId()
						}
					);
				else loadedMods.Add(mod);
			}

			errorResults = results.FindAll(x => x.IsError);
			if (errorResults.Count > 0)
				return new ResultLoadInfos { Mods = Array.Empty<Mod>(), Results = results.ToArray() };

			Mods.AddRange(loadedMods);
			
			// Fire events for loaded mods
			foreach (var mod in loadedMods)
				OnModLoaded?.Invoke(mod);

			results.AddRange(
				loadedMods.Select(
					mod => new ResultLoad {
						Type    = ResultLoad.ResultType.Success,
						Message = $"Mod {mod.GetMetadata().GetId()}({mod.GetMetadata().GetVersion()}) loaded successfully",
						ForMod  = mod.GetMetadata().GetId()
					}
				)
			);


			return new ResultLoadInfos { Mods = loadedMods.ToArray(), Results = results.ToArray() };
		}

		#region Unload Methods

		/// <summary>
		/// Unloads a specific mod by ID.
		/// </summary>
		/// <param name="modId">The ID of the mod to unload</param>
		/// <returns>Result of the unload operation</returns>
		public static async UniTask<ResultUnloadInfos> UnloadMod(string modId) {
			var mod = GetMod(modId);
			if (mod == null)
				return new ResultUnloadInfos {
					Success = false,
					Results = new[] {
						new ResultUnload {
							Type = ResultUnload.ResultType.NotFound,
							Message = $"Mod '{modId}' not found",
							ForMod = modId
						}
					}
				};

			return await UnloadMod(mod);
		}

		/// <summary>
		/// Unloads a specific mod.
		/// </summary>
		/// <param name="mod">The mod to unload</param>
		/// <returns>Result of the unload operation</returns>
		public static async UniTask<ResultUnloadInfos> UnloadMod(Mod mod) {
			var results = new List<ResultUnload>();
			var modId = mod.GetMetadata()?.GetId() ?? "<unknown>";

			// Check for dependents - mods that depend on this mod
			var dependents = GetModsDependingOn(mod);
			if (dependents.Length > 0) {
				// Unload dependents first (in reverse order)
				foreach (var dependent in dependents.Reverse()) {
					var depResult = await UnloadMod(dependent);
					results.AddRange(depResult.Results);
					
					if (!depResult.Success) {
						results.Add(new ResultUnload {
							Type = ResultUnload.ResultType.DependentUnloadFailed,
							Message = $"Failed to unload dependent mod '{dependent.GetMetadata().GetId()}'",
							ForMod = modId
						});
						return new ResultUnloadInfos { Success = false, Results = results.ToArray() };
					}
				}
			}

			try {
				Logger.LogDebug($"Unloading mod {modId}");

				if (!await mod.Unload()) {
					results.Add(new ResultUnload {
						Type = ResultUnload.ResultType.UnloadError,
						Message = $"Failed to unload mod '{modId}'",
						ForMod = modId
					});
					return new ResultUnloadInfos { Success = false, Results = results.ToArray() };
				}

				Mods.Remove(mod);
				OnModUnloaded?.Invoke(mod);

				results.Add(new ResultUnload {
					Type = ResultUnload.ResultType.Success,
					Message = $"Successfully unloaded mod '{modId}'",
					ForMod = modId
				});

				return new ResultUnloadInfos { Success = true, Results = results.ToArray() };
			}
			catch (Exception ex) {
				Logger.LogError($"Exception unloading mod '{modId}': {ex.Message}");
				Logger.LogException(ex);
				
				results.Add(new ResultUnload {
					Type = ResultUnload.ResultType.UnloadError,
					Message = $"Exception unloading mod '{modId}': {ex.Message}",
					ForMod = modId
				});
				return new ResultUnloadInfos { Success = false, Results = results.ToArray() };
			}
		}

		/// <summary>
		/// Unloads multiple mods by their IDs.
		/// </summary>
		/// <param name="modIds">Array of mod IDs to unload</param>
		/// <returns>Combined results of all unload operations</returns>
		public static async UniTask<ResultUnloadInfos> UnloadMods(string[] modIds) {
			var results = new List<ResultUnload>();
			var success = true;

			// Sort mods to unload dependents first
			var modsToUnload = modIds
				.Select(GetMod)
				.Where(m => m != null)
				.ToList();

			// Sort by dependencies (dependents first)
			modsToUnload = SortByDependencies(modsToUnload, reverse: true);

			foreach (var mod in modsToUnload) {
				var result = await UnloadMod(mod);
				results.AddRange(result.Results);
				if (!result.Success)
					success = false;
			}

			return new ResultUnloadInfos { Success = success, Results = results.ToArray() };
		}

		/// <summary>
		/// Unloads all loaded mods.
		/// </summary>
		/// <returns>Combined results of all unload operations</returns>
		public static async UniTask<ResultUnloadInfos> UnloadAllMods() {
			var results = new List<ResultUnload>();
			var success = true;

			// Sort by dependencies (dependents first)
			var modsToUnload = SortByDependencies(Mods.ToList(), reverse: true);

			foreach (var mod in modsToUnload) {
				// Skip if already unloaded (might have been unloaded as a dependency)
				if (!Mods.Contains(mod))
					continue;

				var result = await UnloadMod(mod);
				results.AddRange(result.Results);
				if (!result.Success)
					success = false;
			}

			return new ResultUnloadInfos { Success = success, Results = results.ToArray() };
		}

		/// <summary>
		/// Reloads a mod (unload then load).
		/// </summary>
		/// <param name="modId">The ID of the mod to reload</param>
		/// <returns>True if reload was successful</returns>
		public static async UniTask<bool> ReloadMod(string modId) {
			var mod = GetMod(modId);
			if (mod == null) {
				Logger.LogWarning($"Cannot reload mod '{modId}' - not found");
				return false;
			}

			var discover = mod.Metadata?.InternalDDiscover;
			if (discover == null) {
				Logger.LogError($"Cannot reload mod '{modId}' - no discover found");
				return false;
			}

			// Unload
			var unloadResult = await UnloadMod(mod);
			if (!unloadResult.Success) {
				Logger.LogError($"Failed to unload mod '{modId}' for reload");
				return false;
			}

			// Re-discover and load
			var newMetadata = discover.FindPackage(modId);
			if (newMetadata == null) {
				Logger.LogError($"Failed to find mod '{modId}' for reload");
				return false;
			}

			var loadResult = await LoadMods(new[] { modId }, discover);
			if (loadResult.Results.Any(r => r.IsError)) {
				Logger.LogError($"Failed to reload mod '{modId}'");
				return false;
			}

			Logger.LogDebug($"Successfully reloaded mod '{modId}'");
			return true;
		}

		/// <summary>
		/// Gets all mods that depend on the specified mod.
		/// </summary>
		private static Mod[] GetModsDependingOn(Mod mod) {
			var modMetadata = mod.GetMetadata();
			if (modMetadata == null)
				return Array.Empty<Mod>();

			return Mods
				.Where(m => m != mod && m.GetMetadata()?.GetDepends()
					.Any(dep => modMetadata.Match(dep)) == true)
				.ToArray();
		}

		/// <summary>
		/// Sorts mods by their dependencies.
		/// </summary>
		/// <param name="mods">List of mods to sort</param>
		/// <param name="reverse">If true, dependents come first (for unloading)</param>
		private static List<Mod> SortByDependencies(List<Mod> mods, bool reverse = false) {
			var sorted = new List<Mod>();
			var visited = new HashSet<string>();

			void Visit(Mod mod) {
				var id = mod.GetMetadata()?.GetId();
				if (id == null || visited.Contains(id))
					return;

				visited.Add(id);

				// Visit dependencies first
				var deps = mod.GetMetadata()?.GetDepends() ?? Array.Empty<CCK.Mods.Metadata.Relation>();
				foreach (var dep in deps) {
					var depMod = mods.Find(m => m.GetMetadata()?.Match(dep) == true);
					if (depMod != null)
						Visit(depMod);
				}

				sorted.Add(mod);
			}

			foreach (var mod in mods)
				Visit(mod);

			if (reverse)
				sorted.Reverse();

			return sorted;
		}

		#endregion
	}

	public class ResultLoadInfos {
		public Mod[]        Mods;
		public ResultLoad[] Results;

		public ResultLoad[] GetResults(ResultLoad.ResultType flags)
			=> Array.FindAll(Results, x => x.Type.HasFlag(flags));
	}

	public class ResultLoad {
		public ResultType Type;
		public string     Message;

		public string CausedBy;
		public string ForMod;

		public bool IsSuccess
			=> Type.HasFlag(ResultType.IsSuccess);

		public bool IsWarning
			=> Type.HasFlag(ResultType.IsWarning);

		public bool IsError
			=> Type.HasFlag(ResultType.IsError);


		[Flags]
		public enum ResultType {
			Success           = 1,
			MissingDependency = 2,
			IsConflict        = 4,
			IsBreak           = 8,
			NoMetadata        = 16,
			AlreadyLoaded     = 32,
			LoadError         = 64,

			IsSuccess = Success,
			IsWarning = IsConflict,
			IsError   = MissingDependency | IsBreak | NoMetadata | AlreadyLoaded | LoadError
		}
	}

	/// <summary>
	/// Results from unload operations.
	/// </summary>
	public class ResultUnloadInfos {
		public bool Success;
		public ResultUnload[] Results;

		public ResultUnload[] GetResults(ResultUnload.ResultType flags)
			=> Array.FindAll(Results, x => x.Type.HasFlag(flags));
	}

	/// <summary>
	/// Result of a single mod unload operation.
	/// </summary>
	public class ResultUnload {
		public ResultType Type;
		public string Message;
		public string ForMod;

		public bool IsSuccess => Type == ResultType.Success;
		public bool IsError => Type != ResultType.Success;

		[Flags]
		public enum ResultType {
			Success = 1,
			NotFound = 2,
			UnloadError = 4,
			DependentUnloadFailed = 8,
			NotLoaded = 16
		}
	}
}