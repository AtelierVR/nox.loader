using System.Collections.Generic;
using Nox.ModLoader.Mods;
using Nox.ModLoader.Typing;

namespace Nox.ModLoader.Discovers
{
    public class GlobalDiscover : IDiscover
    {
        private static IDiscover _instance;
        public static IDiscover Instance => _instance ?? new GlobalDiscover();

        public GlobalDiscover()
        {
            _instance = this;
        }

        public IDiscover[] Discovers { get; private set; } = new IDiscover[]
        {
            KernelDiscover.Instance,
            FolderDiscover.Instance
        };

        public ModMetadata[] FindAllPackages()
        {
            List<ModMetadata> packages = new();
            foreach (var discover in Discovers)
                packages.AddRange(discover.FindAllPackages());
            return packages.ToArray();
        }

        public ModMetadata FindPackage(string id)
        {
            foreach (var discover in Discovers)
            {
                var package = discover.FindPackage(id);
                if (package != null)
                    return package;
            }
            return null;
        }

        public Mod CreateMod(ModMetadata metadata) 
            => metadata.InternalDDiscover.CreateMod(metadata);
    }
}
