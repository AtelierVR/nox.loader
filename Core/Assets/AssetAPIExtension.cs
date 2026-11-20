using Nox.CCK.Utils;
using Nox.ModLoader.Mods;
using System.Collections.Generic;
using System.Linq;

namespace Nox.ModLoader.Cores.Assets {

	public static class AssetAPIExtension {

		public static ResourceIdentifier Resolve(ResourceIdentifier path, Mod mod) {
			if (path.HasNamespace())
				return path;
			return new ResourceIdentifier(mod.Metadata.Id, path.SplitPath);
		}

		/// <summary>
		/// Namespaces an asset lookup has to try: the one of the path, then the id of the mod that owns it and
		/// everything that mod provides. Only the mod that owns the namespace probes them (see the
		/// <c>Metadata.Match</c> guard in the asset APIs), so this runs twice per lookup at most instead of once
		/// per loaded mod. The array is returned as is: <c>foreach</c> over an array does not allocate, an
		/// <c>IReadOnlyList</c> would box its enumerator.
		/// </summary>
		public static string[] GetNamespaces(ResourceIdentifier path) {
			var ns = path.Namespace;
			var mod = ModManager.GetMod(ns);

			if (mod == null)
				return new[] { ns };

			var meta = mod.Metadata;
			var provides = meta.Provides;
			var chain = new string[2 + provides.Length];

			chain[0] = ns;
			chain[1] = meta.Id;

			for (var i = 0; i < provides.Length; i++)
				chain[2 + i] = provides[i];

			return chain;
		}

		/// <summary>
		/// Lower-cased path with forward slashes, as the bundle indexes and the asset database compare it.
		/// Returns the argument itself when it is already normalized (no allocation at all) and builds the
		/// result in a single string otherwise: <c>Replace('\\', '/').ToLower()</c> allocated two strings per
		/// lookup, and a <see cref="System.Text.StringBuilder"/> would allocate a builder and its buffer even
		/// when nothing has to change.
		/// </summary>
		public static string FormatPath(string path) {
			if (string.IsNullOrEmpty(path))
				return path;

			var normalized = true;

			for (var i = 0; i < path.Length; i++) {
				var c = path[i];
				if (c == '\\' || char.ToLowerInvariant(c) != c) {
					normalized = false;
					break;
				}
			}

			if (normalized)
				return path;

			return string.Create(
				path.Length,
				path,
				(span, source) => {
					for (var i = 0; i < span.Length; i++) {
						var c = source[i];
						span[i] = c == '\\'
							? '/'
							: char.ToLowerInvariant(c);
					}
				}
			);
		}

		/// <summary>
		/// Filtre les mods chargés en excluant le mod actuel et ceux qui correspondent au namespace donné.
		/// </summary>
		public static IEnumerable<Mod> GetOtherLoadedMods(Mod crt, string ns)
			=> ModManager.Mods.Where(m => m != crt && m.IsLoaded && !m.Metadata.Match(ns));
	}
}
