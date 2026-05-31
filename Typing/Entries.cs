using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;

namespace Nox.ModLoader.Typing {

	public class Entries : IEntries {
		private readonly Dictionary<string, string[]> _entries = new();

		static internal Entries LoadFromJson(JToken json) {
			var obj     = json.ToObject<JObject>();
			var entries = new Entries();
			foreach (var entry in obj)
				entries._entries.Add(entry.Key, entry.Value.ToObject<string[]>());
			return entries;
		}

		public bool Has(string id)
			=> _entries.ContainsKey(id);

		public string[] Get(string id)
			=> Has(id)
				? _entries[id]
				: Array.Empty<string>();

		public Dictionary<string, string[]> GetAll()
			=> _entries ?? new Dictionary<string, string[]>();

		public JObject ToJson() {
			var obj = new JObject();
			foreach (var entry in _entries)
				obj.Add(entry.Key, JArray.FromObject(entry.Value));
			return obj;
		}
	}

}