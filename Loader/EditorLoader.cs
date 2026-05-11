#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.ModLoader.EntryPoints;
using Nox.ModLoader.Mods;
using UnityEngine;
using UnityEditor;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Loader {
	public static class EditorLoader {
		#region Loader Initialization

		[UnityEditor.Callbacks.DidReloadScripts]
		private static void OnScriptsReloaded()
			=> OnScriptsReloadedAsync().Forget();

		private static async UniTask OnScriptsReloadedAsync() {
			if (Application.isBatchMode) {
				Logger.LogDebug("Mod Loader skipped in batchmode.", tag: nameof(EditorLoader));
				return;
			}

			if (LoaderDisabled) {
				Logger.LogDebug("Mod Loader is disabled in Editor mode. Skipping initialization.", tag: nameof(EditorLoader));
				return;
			}
			
			Logger.LogDebug("Initializing Mod Loader in Editor mode...", tag: nameof(EditorLoader));

			Application.runInBackground = true;

			await LoaderManager.Discover();

			// Initialize editors entries
			EditorApplication.update += LoaderManager.OnUpdate;
			LoaderManager.Enable(EntryPoint.MainEntry, EntryPoint.EditorEntry);
			await LoaderManager.Initialize();

			// Handle play mode state changes
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			if (EditorApplication.isPlaying)
				OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state) {
			switch (state) {
				case PlayModeStateChange.EnteredPlayMode:
					// Check if auto-start is enabled
					if (!AutoStart) {
						Logger.LogDebug("Mod Loader auto-start is disabled for Play Mode.", tag: nameof(EditorLoader));
						return;
					}

					if (!ProcessWantsToLoad()) {
						Logger.LogDebug("Mod Loader loading cancelled.", tag: nameof(EditorLoader));
						return;
					}

					// Remove editor update (runtime will handle updates)
					EditorApplication.update -= LoaderManager.OnUpdate;

					// Notify mods that play mode is entered
					LoaderManager.OnEnterPlayMode();

					// Start runtime entries
					RuntimeLoader.Enable();
					break;
				case PlayModeStateChange.ExitingPlayMode:
					// Stop runtime entries
					RuntimeLoader.Disable();

					// Notify mods that play mode is exited
					LoaderManager.OnExitPlayMode();

					// Re-add editor update
					EditorApplication.update += LoaderManager.OnUpdate;

					ReloadMods();
					break;
				case PlayModeStateChange.EnteredEditMode:
				case PlayModeStateChange.ExitingEditMode:
				default:
					break;
			}
		}

		[MenuItem("Nox/Play Mode/Reload Mods")]
		public static void ReloadMods()
			=> ReloadModsAsync().Forget();

		private static async UniTask ReloadModsAsync() {
			if (Application.isPlaying) {
				Logger.LogWarning("Cannot reload mods while in Play Mode.", tag: nameof(EditorLoader));
				return;
			}

			Logger.LogDebug("Reloading Mods...", tag: nameof(EditorLoader));

			var mods = ModManager.GetMods()
				.Reverse();

			foreach (var mod in mods)
				await mod.Unload();

			ModManager.Mods.Clear();

			await LoaderManager.Discover();
			LoaderManager.Enable(EntryPoint.MainEntry, EntryPoint.EditorEntry);
			await LoaderManager.Initialize();

			Logger.LogDebug("Mods reloaded.", tag: nameof(EditorLoader));
		}

		#endregion

		#region Startup Preferences
		
		public static bool LoaderDisabled {
			get => Config.LoadEditor().Get("loader_disabled", false);
			set {
				var config = Config.LoadEditor();
				config.Set("loader_disabled", value);
				config.Save();
			}
		}
		
		private const string LoaderMenuPath = "Nox/Loader/Enable";

		[MenuItem(LoaderMenuPath, false)]
		public static void ToggleLoader() {
			LoaderDisabled = !LoaderDisabled;
			Logger.Log(LoaderDisabled
				? "Loader Disabled... Please restart the editor to take effect."
				: "Loader Enabled... Please restart the editor to take effect.");
		}

		[MenuItem(LoaderMenuPath, true)]
		private static bool ToggleLoaderValidate() {
			Menu.SetChecked(LoaderMenuPath, !LoaderDisabled);
			return true;
		}

		[MenuItem("Nox/Play Mode/Wants To Load/Force Yes")]
		public static void WantsToLoadYes() {
			if (WantsTo == WantsToLoad.Yes) {
				Logger.Log("Wants to load is already set to Yes...");
				return;
			}

			WantsTo = WantsToLoad.Yes;
			Logger.Log("Wants to load set to Yes...");
		}

		[MenuItem("Nox/Play Mode/Wants To Load/Force No")]
		public static void WantsToLoadNo() {
			if (WantsTo == WantsToLoad.No) {
				Logger.Log("Wants to load is already set to No...");
				return;
			}

			WantsTo = WantsToLoad.No;
			Logger.Log("Wants to load set to No...");
		}

		[MenuItem("Nox/Play Mode/Wants To Load/Ask every time")]
		public static void WantsToLoadAsk() {
			if (WantsTo == WantsToLoad.None) {
				Logger.Log("Wants to load is already set to Ask every time...");
				return;
			}

			WantsTo = WantsToLoad.None;
			Logger.Log("Wants to load set to None...");
		}

		private const string AutoStartMenuPath = "Nox/Play Mode/Auto Start";

		[MenuItem(AutoStartMenuPath, false)]
		private static void ToggleAutoStart() {
			AutoStart = !AutoStart;
			Logger.Log(AutoStart
				? "Auto Start Enabled..."
				: "Auto Start Disabled... It's effective next time you enter Play Mode.");
			if (AutoStart && EditorApplication.isPlaying)
				OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
		}

		[MenuItem(AutoStartMenuPath, true)]
		private static bool ToggleAutoStartValidate() {
			Menu.SetChecked(AutoStartMenuPath, AutoStart);
			return true;
		}

		private enum WantsToLoad : byte {
			None = 0,
			Yes  = 1,
			No   = 2
		}

		private static WantsToLoad WantsTo {
			get
				=> Config.LoadEditor().Get("wants_to_load", (byte)WantsToLoad.None)
					switch {
						0 => WantsToLoad.None,
						1 => WantsToLoad.Yes,
						2 => WantsToLoad.No,
						_ => WantsToLoad.None
					};
			set {
				var config = Config.LoadEditor();
				config.Set("wants_to_load", (byte)value);
				config.Save();
			}
		}

		private static bool AutoStart {
			get => Config.LoadEditor().Get("auto_start", true);
			set {
				var config = Config.LoadEditor();
				config.Set("auto_start", value);
				config.Save();
			}
		}

		private static bool ProcessWantsToLoad() {
			var blockers = new List<Mod>();
			var mods     = ModManager.GetMods();

			// Send can_load event to all mods
			foreach (var mod in mods) {
				mod.CoreAPI.LocalEventAPI.Emit("mod_can_load", mod, new Action<object[]>(Action));
				continue;

				void Action(object[] obj) {
					if (obj.Length > 0 && obj[0] is false) {
						Logger.LogWarning($"Mod {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()} blocked loading the game.");
						blockers.Add(mod);
					} else Logger.LogWarning($"Mod {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()} allowed loading the game. But is ignored.");
				}
			}

			if (blockers.Count <= 0)
				return true;

			Logger.LogWarning("Mod Loader will not load the game due to mod blockers:");
			foreach (var mod in blockers)
				Logger.LogWarning($" - {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}");

			switch (WantsTo) {
				case WantsToLoad.Yes:
					Logger.Log("User wants to load the game (settings)...");
					break;
				case WantsToLoad.No:
					Logger.Log("User does not want to load the game (settings)...");
					return false;
				case WantsToLoad.None:
				default: {
					var result = Logger.OpenDialog(
						nameof(EditorLoader),
						string.Join(
							"",
							$"Mod {blockers[0].Metadata.GetId()}@{blockers[0].Metadata.GetVersion()}",
							$"{(blockers.Count > 1 ? $" and {blockers.Count - 1} other(s)" : "")}",
							" blocked loading the game. Do you want to continue?"
						),
						"Yes, continue",
						"No, cancel"
					);

					if (!result) {
						Logger.Log("User does not want to load the game...");
						return false;
					}

					Logger.Log("User wants to load the game (asked)...");

					break;
				}
			}

			return true;
		}

		#endregion
	}
}
#endif