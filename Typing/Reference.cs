using Newtonsoft.Json.Linq;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Typing
{
    public class Reference : CCK.Mods.Metadata.Reference
    {
        private string _name;
        private string _file;
        private Engine _engine;
        private Platform _platform;

        internal static Reference LoadFromJson(JToken json)
        {
            var obj = json.ToObject<JObject>();
            return new Reference
            {
                _name = obj.TryGetValue("name", out var name) ? name.Value<string>() : null,
                _file = obj.TryGetValue("file", out var file) ? file.Value<string>() : null,
                _engine = obj.TryGetValue("engine", out var engine) ? Engine.LoadFromJson(engine) : null,
                _platform = obj.TryGetValue("platform", out var platform) ? PlatformExtensions.GetPlatformFromName(platform.Value<string>()) : Platform.None
            };
        }

        /// <summary>
        /// Get the namespace of the reference.
        /// </summary>
        /// <returns></returns>
        public string GetNamespace() => _name;

        /// <summary>
        /// Get the file of the reference.
        /// </summary>
        /// <returns></returns>
        public string GetFile() => _file;

        /// <summary>
        /// Get the engine of the reference.
        /// </summary>
        /// <returns></returns>
        public CCK.Mods.Metadata.Engine GetEngine() => _engine;

        /// <summary>
        /// Get the platform of the reference.
        /// </summary>
        /// <returns></returns>
        public Platform GetPlatform() => _platform;

        public bool IsCompatible()
        {
            var engine = GetEngine();
            if (engine == null)
                return GetPlatform() == Platform.None || GetPlatform() == PlatformExtensions.CurrentPlatform;
            
            return (engine.GetName() == CCK.Utils.Engine.None || EngineExtensions.CurrentEngine == engine.GetName())
                && (engine.GetVersion()?.Matches(EngineExtensions.CurrentVersion) ?? true)
                && (GetPlatform() == Platform.None || GetPlatform() == PlatformExtensions.CurrentPlatform);
        }
        
        public JObject ToJson()
        {
            var obj = new JObject
            {
                {"name", _name},
                {"file", _file},
                {"engine", _engine?.ToJson()},
                {"platform", _platform.ToString()}
            };
            return obj;
        }
    }
}