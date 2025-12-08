using Nox.ModLoader.Mods;
using Nox.ModLoader.Typing;

namespace Nox.ModLoader.Discovers
{
    public interface IDiscover
    {
        public ModMetadata[] FindAllPackages();
        public ModMetadata FindPackage(string id);

        public Mod CreateMod(ModMetadata metadata);
    }
}