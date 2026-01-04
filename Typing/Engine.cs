using System;
using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Typing {
	public class Engine : IEngine {
		private VersionMatching  _version;
		private CCK.Utils.Engine _engine;

		/// <summary>
		/// Get the data of the engine.
		/// </summary>
		/// <param name="json"></param>
		/// <returns></returns>
		internal static Engine LoadFromJson(JToken json) {
			if (json.Type == JTokenType.String)
				return new Engine {
					_engine  = EngineExtensions.GetEngineFromName(json.Value<string>()),
					_version = new VersionMatching(">=0.0.0")
				};
			else if (json.Type == JTokenType.Object) {
				var obj = json.ToObject<JObject>();
				return new Engine {
					_engine  = EngineExtensions.GetEngineFromName(obj.Value<string>("name")),
					_version = obj.TryGetValue("version", out var version) ? new VersionMatching(version.Value<string>()) : new VersionMatching(">=0.0.0")
				};
			}

			return null;
		}

		/// <summary>
		/// Get the name of the engine.
		/// </summary>
		/// <returns></returns>
		public CCK.Utils.Engine GetName()
			=> _engine;

		/// <summary>
		/// Get the version of the engine.
		/// </summary>
		/// <returns></returns>
		public VersionMatching GetVersion()
			=> _version;

		public JObject ToJson() {
			var obj = new JObject {
				{ "name", _engine.ToString() },
				{ "version", _version.ToString() }
			};
			return obj;
		}
	}
}