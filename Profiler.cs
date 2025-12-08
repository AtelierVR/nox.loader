using System;
using System.Collections.Generic;
using Nox.CCK.Mods;

namespace Nox.ModLoader {
	public class Profiler {
		public void Set(string type, At at, DateTime value)
			=> Set(type, null, at, value);

		public void Set(string type, string entry, At at, DateTime value)
			=> Set(type, entry, null, at, value);

		public void Set(string type, string entry, string key, At at, DateTime value) {
			var profileKey = new ProfileKey(type, entry, key);
			
			if (!_profiles.TryGetValue(profileKey, out var profile)) {
				profile = new Profile {
					Type  = type,
					Entry = entry,
					Key   = key,
					Start = DateTime.MinValue,
					End   = DateTime.MaxValue
				};
				_profiles[profileKey] = profile;
			}
			
			if (at == At.Start) profile.Start = value;
			else profile.End = value;
		}

		private readonly Dictionary<ProfileKey, Profile> _profiles = new();

		public Profile Get(string type, string entry, string key) {
			var profileKey = new ProfileKey(type, entry, key);
			_profiles.TryGetValue(profileKey, out var profile);
			return profile;
		}

		public IEnumerable<Profile> GetAllProfiles()
			=> _profiles.Values;

		public enum At {
			Start,
			End
		}

		private readonly struct ProfileKey : IEquatable<ProfileKey> {
			private readonly string _type;
			private readonly string _entry;
			private readonly string _key;
			private readonly int _hashCode;

			public ProfileKey(string type, string entry, string key) {
				_type = type ?? string.Empty;
				_entry = entry ?? string.Empty;
				_key = key ?? string.Empty;
				
				// Calcul du hashcode une seule fois
				unchecked {
					_hashCode = _type.GetHashCode();
					_hashCode = (_hashCode * 397) ^ _entry.GetHashCode();
					_hashCode = (_hashCode * 397) ^ _key.GetHashCode();
				}
			}

			public bool Equals(ProfileKey other)
				=> _type == other._type && _entry == other._entry && _key == other._key;

			public override bool Equals(object obj)
				=> obj is ProfileKey other && Equals(other);

			public override int GetHashCode() => _hashCode;

			public static bool operator ==(ProfileKey left, ProfileKey right) => left.Equals(right);
			public static bool operator !=(ProfileKey left, ProfileKey right) => !left.Equals(right);
		}
	}
}