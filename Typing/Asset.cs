using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;

namespace Nox.ModLoader.Typing
{
    public class Asset : IAsset
    {
        public string   Name   { get; set; }
        public string   File   { get; set; }
        public string   Hash   { get; set; }
        public string[] Assets { get; set; }
        public string[] Scenes { get; set; }

        public string   GetName()   => Name;
        public string   GetFile()   => File;
        public string   GetHash()   => Hash;
        public string[] GetAssets() => Assets;
        public string[] GetScenes() => Scenes;

        public JObject ToJson()
        {
            var obj = new JObject {
                {"name", Name},
                {"file", File},
                {"hash", Hash},
            };
            if (Assets is { Length: > 0 }) obj.Add("assets", new JArray(Assets));
            if (Scenes is { Length: > 0 }) obj.Add("scenes", new JArray(Scenes));
            return obj;
        }
    }
}
