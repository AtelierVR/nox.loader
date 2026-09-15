using System;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Loader {
	public static class LoaderManager {
		/// <summary>
		/// Budget total accordé, par passe de démontage synchrone, à l'attente des callbacks
		/// <c>*Async</c> des mods (voir <see cref="WaitSync"/>).
		/// </summary>
		private static readonly TimeSpan SyncDisposeBudget = TimeSpan.FromSeconds(2);

		private static DateTime _syncDeadline;

		#if UNITY_EDITOR
		public static void OnEnterPlayMode() {
			var mods = ModManager.GetMods();
			foreach (var mod in mods)
				mod.EnterPlayMode();
		}

		public static void OnExitPlayMode() {
			var mods = ModManager.GetMods();
			foreach (var mod in mods)
				mod.ExitPlayMode();
		}
		#endif

		public static void Enable(params string[] entries) {
			if (entries.Length == 0) {
				Logger.LogWarning("No mod entries provided to enable.", tag: nameof(LoaderManager));
				return;
			}

			var mods = ModManager.GetMods();
			if (mods.Count == 0) {
				Logger.LogWarning("No mods loaded to enable entries for. Try discovering mods first.", tag: nameof(LoaderManager));
				return;
			}

			Logger.ShowProgress(nameof(LoaderManager), "Enabling mod entries...", -1.0f, background: true);

			for (var i = 0; i < mods.Count; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Enabling {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					(float)(i + 1) / mods.Count,
					background: true
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
			if (mods.Count == 0) {
				Logger.LogWarning("No mods loaded to disable entries for. Try discovering mods first.", tag: nameof(LoaderManager));
				return;
			}

			Logger.ShowProgress(nameof(LoaderManager), "Disabling mod entries...", -1.0f, background: true);

			for (var i = 0; i < mods.Count; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Disabling {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					(float)(i + 1) / mods.Count,
					background: true
				);

				foreach (var entry in entries)
					mod.GetEntry(entry)?.Disable();
			}

			Logger.ClearProgress();
		}

		public static void OnUpdate() {
			var mods = ModManager.GetMods();
			foreach (var mod in mods)
				mod.Update();
		}

		public static void OnFixedUpdate() {
			var mods = ModManager.GetMods();
			foreach (var mod in mods)
				mod.FixedUpdate();
		}

		public static void OnLateUpdate() {
			var mods = ModManager.GetMods();
			foreach (var mod in mods)
				mod.LateUpdate();
		}

		public static async UniTask Initialize() {
			var mods = ModManager.GetMods();
			if (mods.Count == 0) {
				Logger.LogWarning("No mods loaded to initialize. Try discovering mods first.", tag: nameof(LoaderManager));
				return;
			}

			Logger.ShowProgress(nameof(LoaderManager), "Initializing Mods...", 0f, background: true);
			for (var i = 0; i < mods.Count; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Initializing {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					(float)(i + 1) / mods.Count / 2,
					background: true
				);

				await mod.Initialize();
				await UniTask.SwitchToMainThread();
			}

			Logger.ShowProgress(nameof(LoaderManager), "Post-Initializing Mods...", 0.5f, background: true);
			for (var i = 0; i < mods.Count; i++) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Post-Initializing {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					0.5f + ((float)(i + 1) / mods.Count / 2),
					background: true
                );

				await mod.PostInitialize();
				await UniTask.SwitchToMainThread();
			}

			Logger.ClearProgress();
		}

		/// <summary>
		/// Démonte tous les mods <b>synchroniquement</b>, dans l'ordre inverse de l'initialisation
		/// (les dépendants avant leurs dépendances).
		/// <para>
		/// Indispensable avant un reload de domaine : Unity ne déroule alors ni les
		/// <c>OnDestroy</c> ni le démontage normal, et les continuations UniTask ne sont plus
		/// pompées — impossible d'<c>await</c> ici. Chaque mod libère donc ses ressources
		/// (sockets d'écoute, threads, handles…) via la partie synchrone de ses callbacks de
		/// dispose, au lieu de les laisser survivre au domaine suivant.
		/// </para>
		/// </summary>
		/// <param name="reason">Origine de l'appel, pour le log.</param>
		public static void DisposeSync(string reason) {
			var mods = ModManager.GetMods();
			if (mods.Count == 0)
				return;

			Logger.Log($"Disposing {mods.Count} mods synchronously ({reason})...", tag: nameof(LoaderManager));

			_syncDeadline = DateTime.UtcNow + SyncDisposeBudget;

			for (var i = mods.Count - 1; i >= 0; i--) {
				var mod = mods[i];
				try {
					mod.DisposeSync();
				} catch (Exception e) {
					Logger.LogError(
						new Exception($"Failed to dispose {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()} synchronously", e),
						tag: nameof(LoaderManager)
					);
				}
			}

			Logger.Log("Mods disposed synchronously.", tag: nameof(LoaderManager));
		}

		/// <summary>
		/// Attend la fin d'une tâche de démontage en bloquant (variantes <c>*Async</c> des
		/// callbacks de dispose, appelées par <see cref="Mods.Mod.DisposeSync"/>).
		/// <para>
		/// Deux garde-fous : une tâche déjà terminée n'est jamais bloquante, et l'attente est
		/// plafonnée par le budget global de la passe. Bloquer le thread principal empêche les
		/// continuations UniTask repassant par la player loop de s'exécuter : au-delà du budget,
		/// attendre plus longtemps ne terminerait rien et figerait l'éditeur (cf. l'incident
		/// « Reload Domain hang »).
		/// </para>
		/// </summary>
		/// <param name="task">Tâche à attendre.</param>
		/// <param name="what">Libellé pour le log.</param>
		internal static void WaitSync(UniTask task, string what) {
			if (task.Status.IsCompleted()) {
				// Terminée : on récupère le résultat tout de suite (et on propage l'exception éventuelle).
				task.GetAwaiter().GetResult();
				return;
			}

			var remaining = _syncDeadline - DateTime.UtcNow;
			if (remaining <= TimeSpan.Zero) {
				Logger.LogWarning($"Not waiting for {what}: the synchronous dispose budget is exhausted.", tag: nameof(LoaderManager));
				return;
			}

			try {
				if (!task.AsTask().Wait(remaining))
					Logger.LogWarning($"Timed out waiting for {what}.", tag: nameof(LoaderManager));
			} catch (Exception e) {
				Logger.LogError(new Exception($"Error while waiting for {what}", e), tag: nameof(LoaderManager));
			}
		}

		public static async UniTask Dispose() {
			var mods = ModManager.GetMods();
			if (mods.Count == 0) {
				Logger.LogWarning("No mods loaded to dispose. Try discovering mods first.", tag: nameof(LoaderManager));
				return;
			}

			// Dispose in reverse initialization order so dependants are disposed before their dependencies
			Logger.ShowProgress(nameof(LoaderManager), "Pre-Disposing Mods...", 0f, background: true);
			for (var i = mods.Count - 1; i >= 0; i--) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Pre-Disposing {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					(float)(mods.Count - i) / mods.Count / 2,
					background: true
				);

				await mod.PreDispose();
				await UniTask.SwitchToMainThread();
			}

			Logger.ShowProgress(nameof(LoaderManager), "Disposing Mods...", 0.5f, background: true);
			for (var i = mods.Count - 1; i >= 0; i--) {
				var mod = mods[i];
				Logger.ShowProgress(
					nameof(LoaderManager),
					$"Disposing {mod.Metadata.GetId()}@{mod.Metadata.GetVersion()}...",
					0.5f + ((float)(mods.Count - i) / mods.Count / 2),
					background: true
                );

				await mod.Dispose();
				await UniTask.SwitchToMainThread();
			}

			Logger.ClearProgress();
		}

		public static async UniTask Discover() {
			Logger.ShowProgress(nameof(LoaderManager), "Discovering Mods...", -1.0f, background: true);

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