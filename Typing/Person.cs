using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;

namespace Nox.ModLoader.Typing {

    public class Person : IPerson {
        private Dictionary<string, object> _customs;

        /// <summary>
        /// Get the data of the person.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        internal static Person LoadFromJson(JToken json) {
            if (json.Type == JTokenType.String) 
                return new Person { _customs = new Dictionary<string, object> { { "name", json.Value<string>() } } };
            else if (json.Type == JTokenType.Object) 
                return new Person { _customs = json.ToObject<Dictionary<string, object>>() };
            return null;
        }

        /// <summary>
        /// Get the name of the person.
        /// </summary>
        /// <returns></returns>
        public string GetName() => Get<string>("name");

        /// <summary>
        /// Get the email of the person.
        /// </summary>
        /// <returns></returns>
        public string GetEmail() => Get<string>("email");

        /// <summary>
        /// Get the website of the person.
        /// </summary>
        /// <returns></returns>
        public string GetWebsite() => Get<string>("website");


        public T Get<T>(string key)
            where T : class => Has<T>(key) ? _customs[key] as T : null;

        public bool Has<T>(string key)
            where T : class => _customs.ContainsKey(key);

        public Dictionary<string, object> GetAll() => _customs;

        public JObject ToJson() => JObject.FromObject(_customs);

    }

}