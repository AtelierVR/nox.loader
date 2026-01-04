using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Nox.CCK.Mods.Metadata;
using Nox.ModLoader.Discovers;
using UnityEngine;

namespace Nox.ModLoader.Typing {

    /// <summary>
    /// ModMetadata is a class that represents the metadata of a mod.
    /// </summary>
    public class ModMetadata : IModMetadata {
        // Internal code

        private string _id;
        private string[] _provides;
        private Version _version;
        private string _name;
        private string _description;
        private string _license;
        private Contact _contact;
        private Person[] _authors;
        private Person[] _contributors;
        private Relation[] _relations;
        private JObject _customs;
        private Entries _entryPoints;
        private string[] _permissions;
        private Reference[] _references;
        private LoadConstraint[] _constraints;

        // Internal code

        internal readonly Dictionary<string, object> InternalData = new();
        internal IDiscover InternalDDiscover;


        /// <summary>
        /// Keys to ignore when loading metadata from json for custom data.
        /// </summary>
        private static readonly string[] IgnoreKeys = new string[] { "type", "id", "name", "description", "version", "license", "permissions", "customs", "platforms", "engines", "icon", "references", "relations", "authors", "contributors", "contact", "required", "side", "provides", "entrypoints", "constraints" };

        /// <summary>
        /// Load the metadata from a json object.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static ModMetadata LoadFromJson(JObject json) {
            try {
                var obj = new ModMetadata {
                    _id = json.TryGetValue("id", out var id)
                        ? id.Value<string>()
                        : null,
                    _provides = json.TryGetValue("provides", out var provides)
                        ? provides.ToObject<string[]>()
                        : Array.Empty<string>(),
                    _version = json.TryGetValue("version", out var version)
                        ? new Version(version.Value<string>())
                        : new Version(),
                    _name = json.TryGetValue("name", out var name)
                        ? name.Value<string>()
                        : null,
                    _description = json.TryGetValue("description", out var description)
                        ? description.Value<string>()
                        : null,
                    _license = json.TryGetValue("license", out var license)
                        ? license.Value<string>()
                        : null,
                    _contact = json.TryGetValue("contact", out var contact)
                        ? Contact.LoadFromJson(contact)
                        : null,
                    _authors = json.TryGetValue("authors", out var authors)
                        ? authors.ToArray().Select(Person.LoadFromJson).ToArray()
                        : Array.Empty<Person>(),
                    _contributors = json.TryGetValue("contributors", out var contributors)
                        ? contributors.ToArray().Select(Person.LoadFromJson).ToArray()
                        : Array.Empty<Person>(),
                    _relations = json.TryGetValue("relations", out var relations)
                        ? relations.ToArray().Select(Relation.LoadFromJson).ToArray()
                        : Array.Empty<Relation>(),
                    _entryPoints = json.TryGetValue("entrypoints", out var entrypoints)
                        ? Entries.LoadFromJson(entrypoints.Value<JObject>())
                        : null,
                    _permissions = json.TryGetValue("permissions", out var permissions)
                        ? permissions.ToObject<string[]>()
                        : Array.Empty<string>(),
                    _references = json.TryGetValue("references", out var references)
                        ? references.ToArray().Select(Reference.LoadFromJson).ToArray()
                        : Array.Empty<Reference>(),
                    _constraints = json.TryGetValue("constraints", out var constraints) && constraints is JArray constraintsArray
                        ? constraintsArray.Select(LoadConstraint.LoadFromJson).Where(c => c != null).ToArray()
                        : Array.Empty<LoadConstraint>(),
                    _customs = new JObject(),
                };

                foreach (var (key, value) in json)
                    if (!IgnoreKeys.Contains(key))
                        obj._customs[key] = value;
                return obj;
            } catch (Exception e) {
                CCK.Utils.Logger.LogError("Failed to load mod metadata from json");
                CCK.Utils.Logger.LogException(e);
            }

            return null;
        }

        /// <summary>
        /// Remove commentary from a json text (json with commentary / jsonc).
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string RemoveCommentary(string text) {
            var tex = System.Text.RegularExpressions.Regex
                            .Replace(text, @"\/\/.*", m => m.Value.Contains("\"") ? m.Value : "");
            text = System.Text.RegularExpressions.Regex.Replace(tex, @"\/\*[\s\S]*?\*\/", "");
            return tex;
        }

        /// <summary>
        /// Get json object from a json text.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static JObject JsonFromText(string text)
            => JObject.Parse(RemoveCommentary(text));

        /// <summary>
        /// Get json object from a json file.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static JObject JsonFromPath(string path)
            => JsonFromText(System.IO.File.ReadAllText(path));

        /// <summary>
        /// Load the metadata from a json file.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static ModMetadata LoadFromPath(string path)
            => LoadFromJson(JsonFromPath(path));


        // Main data


        /// <summary>
        /// Get the id of the mod.
        /// </summary>
        /// <returns></returns>
        public string GetId()
            => _id;

        /// <summary>
        /// Get alternative ids of the mod.
        /// </summary>
        /// <returns></returns>
        public string[] GetProvides()
            => _provides;

        /// <summary>
        /// Get the version of the mod.
        /// </summary>
        /// <returns></returns>
        public Version GetVersion()
            => _version;


        // Display data


        /// <summary>
        /// Get the name of the mod.
        /// </summary>
        /// <returns></returns>
        public string GetName()
            => _name;

        /// <summary>
        /// Get the description of the mod.
        /// </summary>
        /// <returns></returns>
        public string GetDescription()
            => _description;

        /// <summary>
        /// Get the icon of the mod.
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public string GetIcon(uint size = 0)
            => null;

        /// <summary>
        /// Get the authors of the mod.
        /// </summary>
        /// <returns></returns>
        public IPerson[] GetAuthors()
            => GetInternalAuthors().ToArray<IPerson>();

        internal Person[] GetInternalAuthors()
            => _authors;

        /// <summary>
        /// Get the contact of the mod.
        /// </summary>
        /// <returns></returns>
        public IContact GetContact()
            => GetInternalContact();

        internal Contact GetInternalContact()
            => _contact;

        /// <summary>
        /// Get the contributors of the mod.
        /// </summary>
        /// <returns></returns>
        public IPerson[] GetContributors()
            => GetInternalContributors().ToArray<IPerson>();

        internal Person[] GetInternalContributors()
            => _contributors;

        /// <summary>
        /// Get the license of the mod.
        /// </summary>
        /// <returns></returns>
        public string GetLicense()
            => _license;


        // Relations


        /// <summary>
        /// Get relations of the mod.
        /// </summary>
        /// <returns></returns>
        public IRelation[] GetRelations()
            => GetInternalRelations().ToArray<IRelation>();

        internal Relation[] GetInternalRelations()
            => _relations;

        /// <summary>
        /// Get relation of the mod that breaks.
        /// </summary>
        /// <returns></returns>
        public IRelation[] GetBreaks()
            => GetInternalBreaks().ToArray<IRelation>();

        internal Relation[] GetInternalBreaks()
            => _relations
               .Where(r => r.GetRelationType() == RelationType.Breaks)
               .ToArray();

        /// <summary>
        /// Get relation of the mod that conflicts.
        /// </summary>
        /// <returns></returns>
        public IRelation[] GetConflicts()
            => GetInternalConflicts().ToArray<IRelation>();

        internal Relation[] GetInternalConflicts()
            => _relations
               .Where(r => r.GetRelationType() == RelationType.Conflicts)
               .ToArray();

        /// <summary>
        /// Get relation of the mod that depends.
        /// </summary>
        /// <returns></returns>
        public IRelation[] GetDepends()
            => GetInternalDepends().ToArray<IRelation>();

        internal Relation[] GetInternalDepends()
            => _relations
               .Where(r => r.GetRelationType() == RelationType.Depends)
               .ToArray();

        /// <summary>
        /// Get relation of the mod that recommends.
        /// </summary>
        /// <returns></returns>
        public IRelation[] GetRecommends()
            => GetInternalRecommends().ToArray<IRelation>();

        internal Relation[] GetInternalRecommends()
            => _relations
               .Where(r => r.GetRelationType() == RelationType.Recommends)
               .ToArray();

        /// <summary>
        /// Get relation of the mod that suggests.
        /// </summary>
        /// <returns></returns>
        public IRelation[] GetSuggests()
            => GetInternalSuggests().ToArray<IRelation>();

        internal Relation[] GetInternalSuggests()
            => _relations
               .Where(r => r.GetRelationType() == RelationType.Suggests)
               .ToArray();


        // Load Order Constraints


        /// <summary>
        /// Get the load order constraints of the mod.
        /// </summary>
        /// <returns></returns>
        public LoadConstraint[] GetConstraints()
            => _constraints ?? Array.Empty<LoadConstraint>();


        // Functionality


        /// <summary>
        /// Get the data type of the mod.
        /// </summary>
        /// <returns></returns>
        public string GetDataType()
            => "mod";

        /// <summary>
        /// Get the permissions of the mod.
        /// </summary>
        /// <returns></returns>
        public string[] GetPermissions()
            => _permissions;

        /// <summary>
        /// Get the entry points of the mod.
        /// </summary>
        /// <returns></returns>
        public IEntries GetEntryPoints()
            => GetInternalEntryPoints() as IEntries;

        internal Entries GetInternalEntryPoints()
            => _entryPoints;

        /// <summary>
        /// Get the references of the mod.
        /// </summary>
        /// <returns></returns>
        public CCK.Mods.Metadata.Reference[] GetReferences()
            => GetInternalReferences().Cast<CCK.Mods.Metadata.Reference>().ToArray();

        internal Reference[] GetInternalReferences()
            => _references;

        // Custom data


        /// <summary>
        /// Get the custom objects of the mod.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, object> GetCustoms()
            => _customs.ToObject<Dictionary<string, object>>();

        /// <summary>
        /// Get the custom object of the mod.
        /// </summary>
        /// <typeparam name="T">Output type</typeparam>
        /// <param name="key">Key of the custom object</param>
        /// <returns>Value of the custom object</returns>
        public T GetCustom<T>(string key, T defaultValue = default)
            => _customs.TryGetValue(key, out var value) ? value.ToObject<T>() : defaultValue;

        /// <summary>
        /// Check if the mod has a custom object.
        /// </summary>
        /// <typeparam name="T">Output type</typeparam>
        /// <param name="key">Key of the custom object</param>
        /// <returns>True if the mod has the custom object</returns>
        public bool HasCustom<T>(string key)
            => _customs.ContainsKey(key);


        // Matching


        /// <summary>
        /// Check if the mod matches the metadata.
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        public bool Match(IModMetadata req)
            => (req.GetId() == GetId() || req.GetProvides().Contains(GetId())) && req.GetVersion() == GetVersion();

        /// <summary>
        /// Check if the mod matches the id.
        /// </summary>
        /// <param name="id">Id or alternative id of another mod</param>
        /// <returns></returns>
        public bool Match(string id)
            => id == GetId() || GetProvides().Contains(id);

        /// <summary>
        /// Check if the mod matches the relation.
        /// </summary>
        /// <param name="relation"></param>
        /// <returns></returns>
        public bool Match(IRelation relation) {
            if (!Match(relation.GetId())) return false;
            return relation.GetVersion() == null
                || relation.GetVersion().Matches(GetVersion());
        }

        // Serialization


        /// <summary>
        /// Convert the metadata to a json string.
        /// </summary>
        /// <returns></returns>
        public string ToJson()
            => ToObject().ToString();

        /// <summary>
        /// Convert the metadata to a json object.
        /// </summary>
        /// <returns></returns>
        public JObject ToObject() {
            var json = new JObject {
                ["type"] = GetDataType(), ["id"] = GetId(), ["provides"] = new JArray(GetProvides().Select(p => p)), ["name"] = GetName(),
                ["version"] = GetVersion().ToString(), ["description"] = GetDescription(), ["license"] = GetLicense(), ["icon"] = GetIcon(),
                ["contact"] = GetInternalContact()?.ToJson(), ["authors"] = new JArray(GetInternalAuthors().Select(a => a.ToJson())), ["contributors"] = new JArray(GetInternalContributors().Select(c => c.ToJson())), ["relations"] = new JArray(GetInternalRelations().Select(r => r.ToJson())),
                ["entrypoints"] = new JObject(GetInternalEntryPoints().ToJson()), ["references"] = new JArray(GetInternalReferences().Select(r => r.ToJson())), ["permissions"] = new JArray(GetPermissions().Select(p => p))
            };
            foreach (var custom in GetCustoms()) json[custom.Key] = JToken.FromObject(custom.Value);
            return json;
        }

        #if UNITY_EDITOR
        public void SetCustom<T>(string key, T value) {
            if (Application.isPlaying) throw new UnauthorizedAccessException("Cannot set custom data in play mode");

            if (_customs.ContainsKey(key)) {
                if (value == null) _customs.Remove(key);
                else _customs[key] = JToken.FromObject(value);
            } else if (value != null) _customs.Add(key, JToken.FromObject(value));
        }

        public bool Save(string path) {
            try {
                System.IO.File.WriteAllText(path, ToJson());
                return true;
            } catch (Exception e) {
                CCK.Utils.Logger.LogError("Failed to save mod metadata to json");
                CCK.Utils.Logger.LogException(e);
            }

            return false;
        }
        #endif
    }

}