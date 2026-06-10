using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;

namespace Nox.ModLoader.Typing {

	public class Entries : IEntries {
		private readonly Dictionary<string, EntryPointElement[]> _entries = new();

		public IReadOnlyDictionary<string, EntryPointElement[]> All
			=> new ReadOnlyDictionary<string, EntryPointElement[]>(_entries);

		static internal Entries LoadFromJson(JToken json) {
			var obj     = json.ToObject<JObject>();
			var entries = new Entries();
			foreach (var entry in obj)
				entries._entries.Add(entry.Key, ParseEntryArray(entry.Value));
			return entries;
		}

		private static EntryPointElement[] ParseEntryArray(JToken token) {
			if (token is JArray arr) {
				return arr.Select(ParseEntryToken).Where(e => e != null).ToArray();
			}
			return Array.Empty<EntryPointElement>();
		}

		private static EntryPointElement ParseEntryToken(JToken token) {
			// Format 1: JSON object { assembly?, namespace, class }
			if (token is JObject obj) {
				return new EntryPointElement {
					Assembly  = obj.TryGetValue("assembly", out var a) ? a.Value<string>() : null,
					Namespace = obj.TryGetValue("namespace", out var n) ? n.Value<string>() : null,
					Class     = obj.TryGetValue("class",    out var c) ? c.Value<string>() : null,
				};
			}

			// Format 2 & 3: string — either "Assembly:Namespace.Class" or "Namespace.Class"
			if (token is JValue val && val.Value is string s)
				return EntryPointElement.Parse(s);

			return null;
		}

		public bool Has(string id)
			=> _entries.ContainsKey(id);

		public EntryPointElement[] Get(string id)
			=> Has(id)
				? _entries[id]
				: Array.Empty<EntryPointElement>();

		public JObject ToJson(EntryPointFormat format = EntryPointFormat.None) {
			var obj = new JObject();
			foreach (var entry in _entries) {
				var elements = entry.Value;
				var useObject = (format & EntryPointFormat.EntryPointObject) != 0;

				if (useObject) {
					var arr = new JArray();
					foreach (var e in elements) {
						var elemObj = new JObject {
							["namespace"] = e.Namespace,
							["class"]     = e.Class,
						};
						if (!string.IsNullOrEmpty(e.Assembly))
							elemObj["assembly"] = e.Assembly;
						arr.Add(elemObj);
					}
					obj.Add(entry.Key, arr);
				} else {
					// String format: "Assembly:Namespace.Class" or "Namespace.Class"
					obj.Add(entry.Key, new JArray(elements.Select(e => e.AbsoluteName)));
				}
			}
			return obj;
		}
	}

}