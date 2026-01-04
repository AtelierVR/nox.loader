using System;
using Nox.CCK.Mods.Initializers;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Loader {
	public class LoaderManager {
		public static void Enable(params string[] entries) {
			if (entries.Length == 0) {
				Logger.LogWarning("No mod entries provided to enable.", tag: nameof(LoaderManager));
				return;
			}

			var mods = ModManager.GetMods();
			if (mods.Length == 0) {
				Logger.LogWarning("No mods loaded to enable entries for. Try discovering mods first.", tag: nameof(LoaderManager));
				return;
			}

			Logger.ShowProgress(nameof(LoaderManager), "Enabling mod entries...", -1.0f);

			for (var i = 0; i < mods.Length; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Enabling {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					(float)(i + 1) / mods.Length
				);

				foreach (var entry in entries)
					mod.GetEntry(entry)?.Enable();
			}

			Logger.ClearProgress();
		}

		public static void Disable(params string[] entries) {
			if (entries.Length == 0) {
				Logger.LogWarning("No mod entries provided to disable.", tag: nameof(LoaderManager));
				return;
			}

			var mods = ModManager.GetMods();
			if (mods.Length == 0) {
				Logger.LogWarning("No mods loaded to disable entries for. Try discovering mods first.", tag: nameof(LoaderManager));
				return;
			}

			Logger.ShowProgress(nameof(LoaderManager), "Disabling mod entries...", -1.0f);

			for (var i = 0; i < mods.Length; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Disabling {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					(float)(i + 1) / mods.Length
				);

				foreach (var entry in entries)
					mod.GetEntry(entry)?.Disable();
			}

			Logger.ClearProgress();
		}

		public static void OnUpdate() {
			foreach (var mod in ModManager.Mods)
				mod.Update();
		}

		public static void OnFixedUpdate() {
			foreach (var mod in ModManager.Mods)
				mod.FixedUpdate();
		}

		public static void OnLateUpdate() {
			foreach (var mod in ModManager.Mods)
				mod.LateUpdate();
		}

		public static async UniTask Initialize() {
			var mods = ModManager.GetMods();
			if (mods.Length == 0) {
				Logger.LogWarning("No mods loaded to initialize. Try discovering mods first.", tag: nameof(LoaderManager));
				return;
			}

			Logger.ShowProgress(nameof(LoaderManager), "Initializing Mods...", 0f);
			for (var i = 0; i < mods.Length; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Initializing {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					(float)(i + 1) / mods.Length / 2
				);

				await mod.Initialize();
			}

			Logger.ShowProgress(nameof(LoaderManager), "Post-Initializing Mods...", 0.5f);
			for (var i = 0; i < mods.Length; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Post-Initializing {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					0.5f + (float)(i + 1) / mods.Length / 2
				);

				await mod.PostInitialize();
			}

			Logger.ClearProgress();
		}

		public static async UniTask Dispose() {
			var mods = ModManager.GetMods();
			if (mods.Length == 0) {
				Logger.LogWarning("No mods loaded to dispose. Try discovering mods first.", tag: nameof(LoaderManager));
				return;
			}

			Logger.ShowProgress(nameof(LoaderManager), "Pre-Disposing Mods...", 0f);
			for (var i = 0; i < mods.Length; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Pre-Disposing {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					(float)(i + 1) / mods.Length / 2
				);

				await mod.PreDispose();
			}

			Logger.ShowProgress(nameof(LoaderManager), "Disposing Mods...", 0.5f);
			for (var i = 0; i < mods.Length; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Disposing {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					0.5f + (float)(i + 1) / mods.Length / 2
				);

				await mod.Dispose();
			}

			Logger.ClearProgress();
		}

		public static async UniTask Discover() {
			Logger.ShowProgress(nameof(LoaderManager), "Discovering Mods...", -1.0f);

			var loaded = await ModManager.LoadMods();

			Logger.Log($"{loaded.Mods.Length} mods loaded:", tag: nameof(LoaderManager));
			foreach (var mod in loaded.Mods)
				Logger.Log($" - {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}", tag: nameof(LoaderManager));

			foreach (var result in loaded.Results)
				if (result.IsError)
					Logger.LogError(result.Message, tag: nameof(LoaderManager));
				else if (result.IsWarning)
					Logger.LogWarning(result.Message, tag: nameof(LoaderManager));

			Logger.ClearProgress();
		}
	}
}