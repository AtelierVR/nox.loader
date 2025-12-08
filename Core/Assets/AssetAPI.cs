using Cysharp.Threading.Tasks;

namespace Nox.ModLoader.Cores.Assets
{
    public interface IAssetAPI : CCK.Mods.Assets.IAssetAPI
    {
        public UniTask<bool> RegisterAssets();
        public UniTask<bool> UnRegisterAssets();
        public bool IsLoaded();
    }
}