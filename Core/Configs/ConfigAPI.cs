using System.IO;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Cores.Configs {
	public class ConfigAPI : CCK.Mods.Configs.IConfigAPI {
		private readonly ModLoader.Mods.Mod _mod;

		public ConfigAPI(ModLoader.Mods.Mod mod)
			=> _mod = mod;

		public string GetFolder() {
			var dir = Path.Combine(Constants.ConfigPath, _mod.Metadata.GetId());
			if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
			return dir;
		}

		public void ClearFolder() {
			var folder = GetFolder();
			if (Directory.Exists(folder))
				Directory.Delete(folder, true);
		}
	}
}