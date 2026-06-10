using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Typing
{
    public class Reference : IReference
    {
        public string   Name        { get; set; }
        public string   File        { get; set; }
        public string   Type        { get; set; }
        public string   Hash        { get; set; }
        public string[] Tags { get; set; }

        // Backward-compat: engine is now embedded in constraints as "engine:name:version"
        private Engine _engine;

        internal static Reference LoadFromJson(JToken json)
        {
            var obj = json.ToObject<JObject>();
            return new Reference
            {
                Name = obj.TryGetValue("name", out var name) ? name.Value<string>() : null,
                File = obj.TryGetValue("file", out var file) ? file.Value<string>() : null,
                Type = obj.TryGetValue("type", out var type) ? type.Value<string>() : null,
                Hash = obj.TryGetValue("hash", out var hash) ? hash.Value<string>() : null,
                _engine = obj.TryGetValue("engine", out var engine) ? Engine.LoadFromJson(engine) : null,
                Tags = obj.TryGetValue("tags", out var c) && c is JArray arr
                    ? arr.Select(v => v.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToArray()
                    : Array.Empty<string>(),
            };
        }

        public string GetNamespace() => Name;
        public string GetFile() => File;
        public IEngine GetEngine() => _engine;
        public string[] GetTags() => Tags;

        public bool IsCompatible()
        {
            var engine = GetEngine();
            var engineOk = engine == null
                || ((engine.GetName() == CCK.Utils.Engine.None || EngineExtensions.CurrentEngine == engine.GetName())
                && (engine.GetVersion()?.Matches(EngineExtensions.CurrentVersion) ?? true));

            if (!engineOk) return false;
            if (Tags == null || Tags.Length == 0) return true;

            return Tags.All(c => {
                var colon = c.IndexOf(':');
                if (colon < 0) return true;
                var prefix = c[..colon];
                var value  = c[(colon + 1)..];
                return prefix switch {
                    "platform" => PlatformExtensions.IsCompatible(value),
                    "arch"     => ArchitectureExtensions.IsCompatible(value),
                    "engine"   => IsEngineCompatible(value),
                    _          => true,
                };
            });
        }

        private static bool IsEngineCompatible(string value) {
            var sep = value.IndexOf(':');
            return EngineExtensions.IsCompatible(
                sep < 0 ? value : value[..sep],
                sep < 0 ? null  : value[(sep + 1)..]);
        }

        public JObject ToJson()
        {
            var obj = new JObject {
                {"name", Name},
                {"file", File},
            };
            if (Type != null)        obj.Add("type", Type);
            if (Hash != null)        obj.Add("hash", Hash);
            if (_engine != null)     obj.Add("engine", _engine.ToJson());
            if (Tags is { Length: > 0 })
                obj.Add("tags", new JArray(Tags));
            return obj;
        }
    }
}