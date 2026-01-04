using System;
using Newtonsoft.Json.Linq;

namespace Nox.ModLoader.Typing {
	/// <summary>
	/// Type of load constraint for mod ordering
	/// </summary>
	public enum LoadConstraintType {
		/// <summary>
		/// Load this mod as early as possible (before most other mods)
		/// </summary>
		First,
		
		/// <summary>
		/// Load this mod as late as possible (after most other mods)
		/// </summary>
		Last,
		
		/// <summary>
		/// Load this mod before a specific mod
		/// </summary>
		Before,
		
		/// <summary>
		/// Load this mod after a specific mod
		/// </summary>
		After
	}

	/// <summary>
	/// Represents a constraint on when a mod should be loaded in the load order
	/// </summary>
	public class LoadConstraint {
		/// <summary>
		/// Type of constraint
		/// </summary>
		public LoadConstraintType Type { get; private set; }
		
		/// <summary>
		/// Target mod ID (only used for Before/After types)
		/// </summary>
		public string TargetModId { get; private set; }

		public LoadConstraint(LoadConstraintType type, string targetModId = null) {
			Type = type;
			TargetModId = targetModId;
		}

		/// <summary>
		/// Load a LoadConstraint from a JSON token
		/// </summary>
		public static LoadConstraint LoadFromJson(JToken json) {
			try {
				if (json == null) {
					CCK.Utils.Logger.LogError("Cannot load constraint from null JSON token");
					return null;
				}

				if (json is not JObject obj) {
					CCK.Utils.Logger.LogError($"Constraint JSON must be an object, got: {json.Type}. Content: {json}");
					return null;
				}

				var typeStr = obj.TryGetValue("type", out var typeToken)
					? typeToken.Value<string>()?.ToLower()
					: null;

				if (string.IsNullOrEmpty(typeStr)) {
					CCK.Utils.Logger.LogError($"Constraint 'type' field is required. Constraint object: {obj}");
					return null;
				}

				LoadConstraintType type = typeStr switch {
					"first" => LoadConstraintType.First,
					"last" => LoadConstraintType.Last,
					"before" => LoadConstraintType.Before,
					"after" => LoadConstraintType.After,
					_ => throw new ArgumentException($"Unknown load constraint type: {typeStr}")
				};

				string targetId = null;
				if (type == LoadConstraintType.Before || type == LoadConstraintType.After) {
					// Try both "constraint" and "id" fields for compatibility
					if (obj.TryGetValue("constraint", out var constraintToken)) {
						targetId = constraintToken.Value<string>();
						if (!string.IsNullOrEmpty(targetId)) {
							CCK.Utils.Logger.LogDebug($"Found 'constraint' field with value: {targetId}");
						}
					}
					
					if (string.IsNullOrEmpty(targetId) && obj.TryGetValue("id", out var idToken)) {
						targetId = idToken.Value<string>();
						if (!string.IsNullOrEmpty(targetId)) {
							CCK.Utils.Logger.LogDebug($"Found 'id' field with value: {targetId}");
						}
					}

					if (string.IsNullOrEmpty(targetId)) {
						CCK.Utils.Logger.LogError($"Load constraint type '{typeStr}' requires 'id' or 'constraint' field. Constraint object: {obj}");
						throw new ArgumentException($"Load constraint type '{typeStr}' requires 'id' or 'constraint' field");
					}
				}

				return new LoadConstraint(type, targetId);
			} catch (Exception e) {
				CCK.Utils.Logger.LogError($"Failed to load constraint from json: {e.Message}");
				return null;
			}
		}

		public override string ToString() {
			return Type switch {
				LoadConstraintType.First => "first",
				LoadConstraintType.Last => "last",
				LoadConstraintType.Before => $"before:{TargetModId}",
				LoadConstraintType.After => $"after:{TargetModId}",
				_ => "unknown"
			};
		}
	}
}

