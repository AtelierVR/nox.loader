#if UNITY_EDITOR
using System;
using Nox.CCK.Utils;
using UnityEditor;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Loader {
	/// <summary>
	/// Vérifie au chargement du domaine que la plateforme de build active correspond à l'OS de la
	/// machine. Les AssetBundles (mondes, avatars) sont compilés pour la cible active : travailler
	/// sur une autre plateforme que celle de l'OS produit des bundles inutilisables sur le poste.
	/// </summary>
	public static class PlatformGuard {
		#region Preferences

		/// <summary>Clé <c>Config.LoadEditor()</c> : avertissement désactivé par l'utilisateur.</summary>
		private const string IgnoredKey = "platform_guard_ignored";

		private static bool Ignored {
			get => Config.LoadEditor().Get(IgnoredKey, false);
			set {
				var config = Config.LoadEditor();
				config.Set(IgnoredKey, value);
				config.Save();
			}
		}

		/// <summary>
		/// Une seule sollicitation par session d'éditeur : <c>SessionState</c> survit aux reloads de
		/// domaine mais pas au redémarrage, contrairement à <see cref="Ignored"/> qui est persisté.
		/// </summary>
		private const string AckedSessionKey = "Nox.ModLoader.PlatformGuard.Acked";

		private static bool AckedThisSession {
			get => SessionState.GetBool(AckedSessionKey, false);
			set => SessionState.SetBool(AckedSessionKey, value);
		}

		#endregion

		#region Menu

		private const string CheckMenuPath  = "Nox/Loader/Check Platform Target";
		private const string IgnoreMenuPath = "Nox/Loader/Ignore Platform Warning";

		[MenuItem(CheckMenuPath, false)]
		private static void CheckMenu()
			=> Check(true);

		[MenuItem(IgnoreMenuPath, false)]
		private static void ToggleIgnoreMenu() {
			Ignored          = !Ignored;
			AckedThisSession = false;

			Logger.Log(Ignored
				? "Platform mismatch warning disabled."
				: "Platform mismatch warning enabled.", tag: nameof(PlatformGuard));

			if (!Ignored)
				Check(true);
		}

		[MenuItem(IgnoreMenuPath, true)]
		private static bool ToggleIgnoreValidate() {
			Menu.SetChecked(IgnoreMenuPath, Ignored);
			return true;
		}

		#endregion

		#region Check

		[InitializeOnLoadMethod]
		private static void Initialize() {
			// Différé : pendant le reload de domaine l'éditeur n'est pas en état de changer de cible.
			EditorApplication.delayCall += () => Check();
		}

		/// <summary>
		/// Compare la plateforme de build active à l'OS de la machine et propose de basculer ou de
		/// continuer. Sans effet en batchmode (CI), quand l'avertissement est désactivé, ou quand il
		/// a déjà été traité pour cette session — sauf <paramref name="force"/>.
		/// </summary>
		public static void Check(bool force = false) {
			if (Application.isBatchMode) return;                        // CI : aucune interaction possible
			if (EditorApplication.isPlayingOrWillChangePlaymode) return; // Ne pas interrompre le Play mode
			if (!force && (Ignored || AckedThisSession)) return;

			var host = PlatformExtensions.RuntimePlatform;
			if (host == Platform.None) return; // OS inconnu : rien à comparer

			var current = PlatformExtensions.CurrentPlatform;
			if (current == host) return;

			// Marqué avant la popup : aucune boucle possible si le basculement échoue et recharge le domaine.
			AckedThisSession = true;

			var target = host.GetBuildTarget();

			if (!host.IsSupported()) {
				EditorUtility.DisplayDialog(
					"Wrong build target",
					$"The active build target ({current}) does not match your system ({host}).\n\n"
					+ $"The {host} build support module is not installed, so Unity cannot switch to it.\n"
					+ "Install it from Unity Hub (Installs → your editor → Add modules) and try again.",
					"OK"
				);
				return;
			}

			var choice = EditorUtility.DisplayDialogComplex(
				"Wrong build target",
				$"The active build target ({current}) does not match your system ({host}).\n\n"
				+ "Asset bundles (worlds, avatars) built from this editor are made for the active "
				+ "target and cannot be loaded on another platform.\n\n"
				+ $"Switch to {target}?",
				$"Switch to {host}",
				"Continue",
				"Don't ask again"
			);

			switch (choice) {
				case 0:
					PlatformExtensions.CurrentPlatform = host;
					// Le switch est asynchrone côté éditeur : on relit la cible pour détecter un échec.
					if (PlatformExtensions.CurrentPlatform != host) {
						Logger.LogError($"Failed to switch the build target to {host} ({target}).", tag: nameof(PlatformGuard));
						EditorUtility.DisplayDialog(
							"Switch failed",
							$"Unity could not switch to {host}.\n\n"
							+ "Install the build support module from Unity Hub and try again "
							+ $"(menu {CheckMenuPath}).",
							"OK"
						);
					}
					break;
				case 1:
					Logger.LogWarning(
						$"Continuing with build target {current} on a {host} machine. "
						+ $"Builds produced here will not run on {host}.",
						tag: nameof(PlatformGuard)
					);
					break;
				default:
					Ignored = true;
					Logger.LogWarning(
						$"Platform mismatch warning disabled (build target: {current}, system: {host}). "
						+ $"Re-enable it from the menu {IgnoreMenuPath}.",
						tag: nameof(PlatformGuard)
					);
					break;
			}
		}

		#endregion
	}
}
#endif
