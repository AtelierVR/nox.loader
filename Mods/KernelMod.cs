using System;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Nox.ModLoader.Cores.Assets;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Mods {
	public class KernelMod : Mod {
		public const string MOD_FOLDER_TYPE = "kernel";

		internal Assembly[] Assemblies;
		internal AppDomain Domain;

		public override string GetModType()
			=> MOD_FOLDER_TYPE;

		public override AppDomain GetAppDomain()
			=> Domain;

		public override Assembly[] GetAssemblies()
			=> Assemblies;

		internal KernelMod() {
			CoreAPI = new CoreAPI(this);
			#if UNITY_EDITOR
			AssetAPI = new EditorKernelAssetAPI(this);
			#else
            AssetAPI = new KernelAssetAPI(this);
			#endif
		}

		public override bool IsLoaded()
			=> base.IsLoaded() && AssetAPI.IsLoaded();

		public override async UniTask<bool> Load() {
			Logger.LogDebug($"Loading {Metadata.GetId()}@{Metadata.GetVersion()}");

			var reference = Metadata.GetReferences()
				.Where(i => i.IsCompatible());

			// Retrieve all entrypoints to detect corresponding assemblies
			var namespaces = Metadata.GetEntryPoints().GetAll()
				.SelectMany(e => e.Value)
				.Select(e =>
				{
					// Extract namespace from entrypoint (e.g. "Nox.Control.Main" -> "Nox.Control")
					var lastDot = e.LastIndexOf('.');
					return lastDot > 0 ? e[..lastDot] : e;
				})
				.Distinct();

			Domain = AppDomain.CurrentDomain;
			Assemblies = Domain.GetAssemblies()
				.Where(a => namespaces.Contains(a.GetName().Name))
				.ToArray();

			if (!await AssetAPI.RegisterAssets())
				return false;

			return await base.Load();
		}

		public override async UniTask<bool> Unload() {
			Logger.LogDebug($"Unloading {Metadata.GetId()}");

			if (!await base.Unload())
				return false;

			return await AssetAPI.UnRegisterAssets();
		}
	}
}