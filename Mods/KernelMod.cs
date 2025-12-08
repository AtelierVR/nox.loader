using System;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Nox.ModLoader.Cores.Assets;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Mods {
	public class KernelMod : Mod {
		internal Assembly[] Assemblies;
		internal AppDomain  Domain;

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
			
			Domain = AppDomain.CurrentDomain;
			Assemblies = Domain.GetAssemblies()
				.Where(
					a => a.GetName().Name       == Metadata.GetId()
						|| a.GetName().FullName == Metadata.GetId()
						|| a.FullName           == Metadata.GetId()
						|| reference.Any(
							r => r.GetNamespace()   == a.GetName().Name
								|| r.GetNamespace() == a.GetName().FullName
								|| r.GetNamespace() == a.FullName
						)
				)
				.ToArray();
			
			if(!await AssetAPI.RegisterAssets())
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