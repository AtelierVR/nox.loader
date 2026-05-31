using Nox.CCK.Mods.Assets;
using Nox.CCK.Mods.Configs;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Events;
using Nox.CCK.Mods.Libs;
using Nox.CCK.Mods.Loggers;
using Nox.CCK.Mods.Metadata;
using Nox.CCK.Mods.Mods;

namespace Nox.ModLoader {
	public class CoreAPI : IModCoreAPI, IMainModCoreAPI, IServerModCoreAPI, IClientModCoreAPI, IEditorModCoreAPI {
		internal Mods.Mod                Mod;
        internal Cores.Mods.ModAPI ModAPI { get; }
        internal Cores.Events.EventAPI EventAPI { get; }
        internal Cores.Configs.ConfigAPI ConfigAPI { get; }
        internal Cores.Loggers.LoggerAPI LoggerAPI { get; }
        internal Core.Libs.LibAPI LibAPI { get; }

        public CoreAPI(Mods.Mod mod) {
			Mod            = mod;
			ModAPI    = new Cores.Mods.ModAPI(mod);
			EventAPI  = new Cores.Events.EventAPI(mod, EventEntryFlags.Main);
			ConfigAPI = new Cores.Configs.ConfigAPI(mod);
			LoggerAPI = new Cores.Loggers.LoggerAPI(mod);
			LibAPI    = new Core.Libs.LibAPI(mod);
		}

		public IModMetadata ModMetadata
			=> Mod.Metadata;

		ILibAPI IModCoreAPI.LibAPI
			=> LibAPI;

		IAssetAPI IModCoreAPI.AssetAPI
			=> Mod.AssetAPI;

		IConfigAPI IModCoreAPI.ConfigAPI
			=> ConfigAPI;

		ILoggerAPI IModCoreAPI.LoggerAPI
			=> LoggerAPI;

		IModAPI IModCoreAPI.ModAPI
			=> ModAPI;

		IEventAPI IModCoreAPI.EventAPI
			=> EventAPI;
	}
}