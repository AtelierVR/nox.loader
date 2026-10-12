using System;
using System.Collections.Generic;
using Nox.CCK.Mods;
using UnityEngine;

namespace Nox.ModLoader {
	public class Profiler {
		/// <summary>
		/// Gate of the instrumentation: timestamps are only recorded while somebody actually reads them
		/// (the Performances window polls <see cref="GetAllProfiles"/> every tick, cf. nox.editor).
		/// <para>
		/// Without the gate, <see cref="Mods.Mod.Update"/> / <c>FixedUpdate</c> / <c>LateUpdate</c>
		/// recorded two timestamps for every mod on every frame whether or not anything looked at the
		/// result: ~300 <see cref="Set"/> calls per frame in this project, each hashing three strings
		/// and touching the dictionary (measured ~0.3-0.4 ms and ~1000 write barriers per frame).
		/// </para>
		/// </summary>
		private static float _lastReadTime = float.NegativeInfinity;

		/// <summary>Whether a reader has asked for the profiles recently.</summary>
		public static bool IsRecording
			=> Time.unscaledTime - _lastReadTime < 1f;

		public void Set(string type, At at)
			=> Set(type, null, null, at);

		public void Set(string type, string entry, At at)
			=> Set(type, entry, null, at);

		/// <summary>
		/// Same as the string-key overload with an index key.
		/// <para>
		/// The index is converted here, after the gate: <c>EntryPoint</c> instruments every instance of
		/// every callback with <c>i.ToString()</c>, which allocated one string per instance per frame
		/// (measured 35 per frame) whether or not the recorder was on.
		/// </para>
		/// </summary>
		public void Set(string type, string entry, int key, At at) {
			if (!IsRecording)
				return;

			Set(type, entry, key.ToString(), at, DateTime.UtcNow);
		}

		/// <summary>
		/// Records the start or the end of a scope, timestamped now.
		/// <para>
		/// The timestamp is taken here rather than at the call site on purpose: every caller passed
		/// <c>DateTime.UtcNow</c>, which was therefore evaluated even when the recorder is off
		/// (<see cref="IsRecording"/>) — this keeps the gate in one place and the call sites short.
		/// </para>
		/// </summary>
		public void Set(string type, string entry, string key, At at) {
			if (!IsRecording)
				return;

			Set(type, entry, key, at, DateTime.UtcNow);
		}

		public void Set(string type, At at, DateTime value)
			=> Set(type, null, at, value);

		public void Set(string type, string entry, At at, DateTime value)
			=> Set(type, entry, null, at, value);

		public void Set(string type, string entry, string key, At at, DateTime value) {
			if (!IsRecording)
				return;

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

		public IEnumerable<Profile> GetAllProfiles() {
			// A reader is listening: keep recording for the coming second (see IsRecording).
			_lastReadTime = Time.unscaledTime;
			return _profiles.Values;
		}

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