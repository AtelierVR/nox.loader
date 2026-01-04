using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Mods;
using Nox.CCK.Mods.Metadata;
using Nox.ModLoader.Mods;
using UnityEngine;
using IMod = Nox.ModLoader.Mods.Mod;

namespace Nox.ModLoader.Cores.Mods {
	public class ModAPI : CCK.Mods.Mods.IModAPI {
		private readonly IMod _mod;

		public ModAPI(IMod mod)
			=> _mod = mod;


		public IModMetadata GetMetadata(string id)
			=> GetMod(id)?.GetMetadata();

		public CCK.Mods.IMod GetSelf()
			=> _mod;

		public CCK.Mods.IMod GetMod(string id) {
			var mod = ModManager.GetMod(id);
			if (mod == null) {
				_mod.CoreAPI.LoggerAPI.LogWarning($"Mod with id '{id}' not found.");
				return null;
			}


			var relations = _mod.GetMetadata().GetRelations();
			var found = false;

			foreach (var relation in relations)
				if (mod.GetMetadata().Match(relation))
					found = true;

			if (found)
				return mod;

			if (_mod.GetModType() == KernelMod.MOD_FOLDER_TYPE) {
				_mod.CoreAPI.LoggerAPI.LogWarning($"Mod with id '{id}' is not related to mod '{_mod.GetMetadata().GetId()}' but access is allowed because it is a kernel mod.");
				return mod;
			}
			
			if (_mod.GetMetadata().GetPermissions().Contains("mod_api_all_access")) {
				_mod.CoreAPI.LoggerAPI.LogWarning($"Mod with id '{id}' is not related to mod '{_mod.GetMetadata().GetId()}' but access is allowed due to 'mod_api_all_access' permission.");
				return mod;
			}

			if (Application.isEditor) {
				_mod.CoreAPI.LoggerAPI.LogWarning($"Mod with id '{id}' is not related to mod '{_mod.GetMetadata().GetId()}' but access is allowed for development purposes.");
				return mod;
			}

			_mod.CoreAPI.LoggerAPI.LogError($"Mod with id '{id}' is not related to mod '{_mod.GetMetadata().GetId()}' and cannot be accessed.");
			return null;
		}

		public CCK.Mods.IMod[] GetMods() {
			var mods = ModManager.GetMods();

			if (_mod.GetModType() == KernelMod.MOD_FOLDER_TYPE) {
				// Kernel mods have access to all mods.
				return mods.Cast<CCK.Mods.IMod>().ToArray();
			}

			if (_mod.GetMetadata().GetPermissions().Contains("mod_api_all_access") || Application.isEditor) {
				// Mods with all access permission can access all mods.
				return mods.Cast<CCK.Mods.IMod>().ToArray();
			}

			var relations = _mod.GetMetadata().GetRelations();
			return (from mod in mods
				where relations.Any(relation => mod.GetMetadata().Match(relation))
				select mod).Cast<CCK.Mods.IMod>()
				.ToArray();
		}

		public UniTask<CCK.Mods.IMod> LoadMod(string id) {
			throw new System.NotImplementedException();
		}

		public UniTask<bool> UnloadMod(string id) {
			throw new System.NotImplementedException();
		}

		public UniTask<bool> ReloadMod(string id) {
			throw new System.NotImplementedException();
		}
	}
}