using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;

namespace Nox.ModLoader.Typing {
	public class Contact : IContact {
		private Dictionary<string, object> _customs = new();

		/// <summary>
		/// Get the data of the contact.
		/// </summary>
		/// <param name="json"></param>
		/// <returns></returns>
		static internal Contact LoadFromJson(JToken json)
			=> new() {
				_customs = json.Type switch {
					JTokenType.String => new Dictionary<string, object> {
						{ "name", json.Value<string>() }
					},
					JTokenType.Object => json.ToObject<Dictionary<string, object>>(),
					_                 => null
				}
			};

		/// <summary>
		/// Get the data of the contact.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="key"></param>
		/// <returns></returns>
		public T Get<T>(string key) where T : class
			=> Has<T>(key) ? _customs[key] as T : null;

		/// <summary>
		///Check if the contact has the data.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="key"></param>
		/// <returns></returns>
		public bool Has<T>(string key) where T : class
			=> _customs.ContainsKey(key);

		/// <summary>
		/// Get all the data of the contact.
		/// </summary>
		/// <returns></returns>
		public Dictionary<string, object> GetAll()
			=> _customs;

		public JObject ToJson()
			=> JObject.FromObject(_customs);
	}
}