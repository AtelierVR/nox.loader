using System;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Nox.CCK.Mods.Assets;
using Nox.ModLoader.Cores.Assets;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Mods {
	public class KernelMod : Mod {
		public new const string MOD_FOLDER_TYPE = "kernel";

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

			// Resolve assemblies: use explicit assembly name from entrypoints when available,
			// otherwise fall back to namespace-based discovery.
			var assemblyNames = Metadata.GetEntryPoints().All.Values
				.SelectMany(e => e)
				.Select(e => !string.IsNullOrEmpty(e.Assembly) ? e.Assembly : e.Namespace)
				.Where(n => !string.IsNullOrEmpty(n))
				.ToHashSet();

			Domain = AppDomain.CurrentDomain;
			Assemblies = assemblyNames.Count > 0
				? Domain.GetAssemblies()
					.Where(a => assemblyNames.Contains(a.GetName().Name))
					.ToArray()
				: Array.Empty<Assembly>();

			if (!await AssetAPI.RegisterAssets())
				return false;

			#if UNITY_EDITOR
			EditorGlobalAsset.Register(AssetAPI);
			#endif

			return await base.Load();
		}

		public override async UniTask<bool> Unload() {
			Logger.LogDebug($"Unloading {Metadata.GetId()}");

			if (!await base.Unload())
				return false;

			#if UNITY_EDITOR
			EditorGlobalAsset.Unregister(AssetAPI);
			#endif

			return await AssetAPI.UnRegisterAssets();
		}
	}
}