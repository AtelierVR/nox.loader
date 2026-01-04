using System.Collections.Generic;
using Nox.CCK.Mods.Assets;
using Nox.CCK.Mods.Chats;
using Nox.CCK.Mods.Configs;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Events;
using Nox.CCK.Mods.Libs;
using Nox.CCK.Mods.Loggers;
using Nox.CCK.Mods.Metadata;
using Nox.CCK.Mods.Mods;
using Nox.CCK.Mods.Panels;
using Nox.ModLoader.Typing;

namespace Nox.ModLoader {
	public class CoreAPI : IModCoreAPI, IMainModCoreAPI, IServerModCoreAPI, IClientModCoreAPI, IEditorModCoreAPI {
		internal readonly Mods.Mod                Mod;
		internal readonly Cores.Panels.PanelAPI   LocalPanelAPI;
		internal readonly Cores.Mods.ModAPI       LocalModAPI;
		internal readonly Cores.Events.EventAPI   LocalEventAPI;
		internal readonly Cores.Configs.ConfigAPI LocalConfigAPI;
		internal readonly Cores.Loggers.LoggerAPI LocalLoggerAPI;

		public CoreAPI(Mods.Mod mod) {
			Mod            = mod;
			LocalPanelAPI  = new Cores.Panels.PanelAPI(mod);
			LocalModAPI    = new Cores.Mods.ModAPI(mod);
			LocalEventAPI  = new Cores.Events.EventAPI(mod, EventEntryFlags.Main);
			LocalConfigAPI = new Cores.Configs.ConfigAPI(mod);
			LocalLoggerAPI = new Cores.Loggers.LoggerAPI(mod);
		}

		public IModMetadata ModMetadata
			=> Mod.Metadata;

		public IChatAPI ChatAPI
			=> throw new System.NotImplementedException();

		public EditorLibsAPI LibsAPI
			=> throw new System.NotImplementedException();


		public IAssetAPI AssetAPI
			=> Mod.AssetAPI;

		public IConfigAPI ConfigAPI
			=> LocalConfigAPI;

		public ILoggerAPI LoggerAPI
			=> LocalLoggerAPI;

		public IEditorModPanelAPI PanelAPI
			=> LocalPanelAPI;

		public IModAPI ModAPI
			=> LocalModAPI;

		public IEventAPI EventAPI
			=> LocalEventAPI;
	}
}