using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;

namespace Nox.ModLoader.Typing {

    public class Entries : IEntries {
        private Dictionary<string, string[]> _entries;

        internal static Entries LoadFromJson(JToken json) {
            var obj = json.ToObject<JObject>();
            var entries = new Dictionary<string, string[]>();
            foreach (var entry in obj) entries.Add(entry.Key, entry.Value.ToObject<string[]>());
            return new Entries { _entries = entries };
        }

        public bool Has(string id)
            => _entries.ContainsKey(id);

        public string[] Get(string id) => Has(id)
            ? _entries[id]
            : Array.Empty<string>();

        public Dictionary<string, string[]> GetAll()
            => _entries;

        public JObject ToJson() {
            var obj = new JObject();
            foreach (var entry in _entries) obj.Add(entry.Key, JArray.FromObject(entry.Value));
            return obj;
        }
    }

}