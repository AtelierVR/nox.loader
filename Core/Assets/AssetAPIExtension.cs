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

        public static List<string> GetNamespaces(ResourceIdentifier path) {
            List<string> namespaces = new() { path.Namespace };
            var mod = ModManager.GetMod(path.Namespace);
            if (mod == null) return namespaces;

            var meta = mod.Metadata;
            namespaces.Add(meta.Id);
            namespaces.AddRange(meta.Provides);

            return namespaces;
        }

        public static string FormatPath(string path)
            => path.Replace('\\', '/').ToLower();

        /// <summary>
        /// Filtre les mods chargés en excluant le mod actuel et ceux qui correspondent au namespace donné.
        /// </summary>
        public static IEnumerable<Mod> GetOtherLoadedMods(Mod crt, string ns)
            => ModManager.Mods.Where(m => m != crt && m.IsLoaded && !m.Metadata.Match(ns));
    }

}
