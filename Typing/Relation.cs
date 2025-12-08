using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;
using Nox.CCK.Utils;

namespace Nox.ModLoader.Typing
{
    public class Relation : CCK.Mods.Metadata.Relation
    {
        private string _id;
        private RelationType _relationType;
        private VersionMatching _version;

        /// <summary>
        /// Get the data of the relation.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        internal static Relation LoadFromJson(JToken json)
        {
            if (json.Type == JTokenType.String)
                return new Relation
                {
                    _id = json.Value<string>(),
                    _relationType = RelationType.Depends,
                    _version = new VersionMatching(">=0.0.0")
                };
            else if (json.Type == JTokenType.Object)
            {
                var obj = json.ToObject<JObject>();
                return new Relation
                {
                    _id = obj.Value<string>("id"),
                    _relationType = obj.TryGetValue("type", out var type) ? RelationExtensions.GetRelationTypeFromName(type.Value<string>()) : RelationType.Depends,
                    _version = obj.TryGetValue("version", out var version) ? new VersionMatching(version.Value<string>()) : new VersionMatching(">=0.0.0")
                };
            }
            return null;
        }

        /// <summary>
        /// Get the id of the relation.
        /// </summary>
        /// <returns></returns>
        public string GetId() => _id;

        /// <summary>
        /// Get the type of the relation.
        /// </summary>
        /// <returns></returns>
        public RelationType GetRelationType() => _relationType;

        /// <summary>
        /// Get the version of the relation.
        /// </summary>
        /// <returns></returns>
        public VersionMatching GetVersion() => _version;

        public JObject ToJson()
        {
            var obj = new JObject
            {
                {"id", _id},
                {"type", RelationExtensions.GetRelationTypeFromEnum(_relationType)},
                {"version", _version.ToString()}
            };
            return obj;
        }
    }
}