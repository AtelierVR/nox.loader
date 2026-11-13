using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Nox.CCK.Mods;
using Nox.CCK.Mods.Events;
using Nox.CCK.Mods.Metadata;
using Nox.CCK.Utils;
using Nox.ModLoader.Cores.Assets;
using ModProfiler = Nox.ModLoader.Profiler;
using Nox.ModLoader.EntryPoints;

namespace Nox.ModLoader.Mods {

	public abstract class Mod : IMod {
		public const string MOD_FOLDER_TYPE = "default";

		internal CoreAPI CoreAPI;
		internal IAssetAPI AssetAPI;
		internal Typing.ModMetadata _metadata;

		/// <summary>Profiler of the mod, fed by the loader (exposed as <see cref="Profiler"/>).</summary>
		internal readonly ModProfiler _profiler;

		public abstract AppDomain AppDomain { get; }

		public abstract Assembly[] GetAssemblies();

		public IEnumerable<Profile> Profiler
			=> _profiler.GetAllProfiles();

		public virtual string ModType
			=> MOD_FOLDER_TYPE;


		public IModMetadata Metadata
			=> _metadata;

		internal Mod() {
			CoreAPI = new CoreAPI(this);
			_profiler = new ModProfiler();
		}


		#region Data Storage

		public T GetData<T>(string key, T defaultValue = default)
			=> HasData<T>(key) ? (T)Datas[key] : defaultValue;

		public bool SetData<T>(string key, T value) {
			Datas[key] = value;
			return true;
		}

		public bool HasData<T>(string key)
			=> Datas.ContainsKey(key);

		public Dictionary<string, object> Datas
			=> _metadata.InternalData;

		#endregion


		#region Entrypoints Instances

		private EntryPoint[] _entryPoints = Array.Empty<EntryPoint>();

		public T GetInstance<T>() {

			foreach (var entry in _entryPoints) {
				var instance = entry.GetInstance<T>();
				if (instance != null)
					return instance;
			}

			if (_entryPoints.Length > 0)
				Logger.LogWarning($"Mod {Metadata.Id} does not have an instance of type {typeof(T).FullName}");
			return default;
		}

		public T[] GetInstances<T>() {
			var instances = new List<T>();

			if (_entryPoints.Length == 0)
				return instances.ToArray();

			foreach (var entry in _entryPoints)
				instances.AddRange(entry.GetInstances<T>());

			return instances.ToArray();
		}

		public EntryPoint GetEntry(string name) {
			foreach (var entry in _entryPoints)
				if (entry.Name == name)
					return entry;
			return null;
		}

		#endregion


		public virtual bool IsLoaded => true;

		public bool HasUpdate;
		public bool HasFixedUpdate;
		public bool HasLateUpdate;

		public virtual UniTask<bool> Load() {
			CoreAPI.EventAPI.Emit(new ModEventContext("mod_loaded", this));

			var entryPointKeys = _metadata.EntryPoints.All.Keys;
			_entryPoints = new EntryPoint[entryPointKeys.Count()];
			var index = 0;
			foreach (var entry in entryPointKeys) _entryPoints[index++] = new EntryPoint(this, entry);

			HasUpdate = false;
			HasFixedUpdate = false;
			HasLateUpdate = false;

			foreach (var entry in _entryPoints) {
				if (entry.HasUpdate) HasUpdate = true;
				if (entry.HasFixedUpdate) HasFixedUpdate = true;
				if (entry.HasLateUpdate) HasLateUpdate = true;
			}

			return UniTask.FromResult(true);
		}

		public virtual async UniTask<bool> Unload() {
			Logger.LogDebug($"Unloading {_metadata.Id}@{_metadata.Version}");

			// if not disabled, disable
			foreach (var entry in _entryPoints) entry.Disable();

			// send pre-dispose and dispose
			await PreDispose();
			await Dispose();

			// clear all states

			// Unload any native libraries loaded by this mod
			CoreAPI?.LibAPI?.Unload();

			foreach (var entry in _entryPoints) entry.Dispose();

			_entryPoints = Array.Empty<EntryPoint>();
			CoreAPI.EventAPI.Emit(new ModEventContext("mod_unloaded", this));
			return true;
		}

		public async UniTask Initialize() {
			_profiler.Set("initialize", ModProfiler.At.Start, DateTime.UtcNow);
			var promises = new UniTask[_entryPoints.Length];
			for (var i = 0; i < _entryPoints.Length; i++) promises[i] = _entryPoints[i].OnInitialize();
			await UniTask.WhenAll(promises);
			_profiler.Set("initialized", ModProfiler.At.Start, DateTime.UtcNow);
		}

		public async UniTask PostInitialize() {
			_profiler.Set("post_initialize", ModProfiler.At.Start, DateTime.UtcNow);
			var promises = new UniTask[_entryPoints.Length];
			for (var i = 0; i < _entryPoints.Length; i++) promises[i] = _entryPoints[i].OnPostInitialize();
			await UniTask.WhenAll(promises);
			_profiler.Set("post_initialized", ModProfiler.At.Start, DateTime.UtcNow);
		}

		public async UniTask PreDispose() {
			_profiler.Set("pre_dispose", ModProfiler.At.Start, DateTime.UtcNow);
			var promises = new UniTask[_entryPoints.Length];
			for (var i = 0; i < _entryPoints.Length; i++) promises[i] = _entryPoints[i].OnPreDispose();
			await UniTask.WhenAll(promises);
			_profiler.Set("pre_disposed", ModProfiler.At.Start, DateTime.UtcNow);
		}

		public void Update() {
			if (!HasUpdate) return;
			_profiler.Set("update", ModProfiler.At.Start);
			foreach (var entry in _entryPoints) entry.OnUpdate();
			_profiler.Set("updated", ModProfiler.At.Start);
		}

		public void FixedUpdate() {
			if (!HasFixedUpdate) return;
			_profiler.Set("fixed_update", ModProfiler.At.Start);
			foreach (var entry in _entryPoints) entry.OnFixedUpdate();
			_profiler.Set("fixed_updated", ModProfiler.At.Start);
		}

		public void LateUpdate() {
			if (!HasLateUpdate) return;
			_profiler.Set("late_update", ModProfiler.At.Start);
			foreach (var entry in _entryPoints) entry.OnLateUpdate();
			_profiler.Set("late_updated", ModProfiler.At.Start);
		}

#if UNITY_EDITOR
		public void EnterPlayMode() {
			_profiler.Set("enter_play_mode", ModProfiler.At.Start, DateTime.UtcNow);
			foreach (var entry in _entryPoints) entry.OnEnterPlayMode();
			_profiler.Set("enter_play_mode_done", ModProfiler.At.Start, DateTime.UtcNow);
		}

		public void ExitPlayMode() {
			_profiler.Set("exit_play_mode", ModProfiler.At.Start, DateTime.UtcNow);
			foreach (var entry in _entryPoints) entry.OnExitPlayMode();
			_profiler.Set("exit_play_mode_done", ModProfiler.At.Start, DateTime.UtcNow);
		}
#endif

		public async UniTask Dispose() {
			_profiler.Set("dispose", ModProfiler.At.Start, DateTime.UtcNow);
			var promises = new UniTask[_entryPoints.Length];
			for (var i = 0; i < _entryPoints.Length; i++) promises[i] = _entryPoints[i].OnDispose();
			await UniTask.WhenAll(promises);
			_profiler.Set("disposed", ModProfiler.At.Start, DateTime.UtcNow);
		}

		/// <summary>
		/// Démonte le mod de façon <b>synchrone</b> : désactive les entrées, puis appelle la
		/// partie synchrone de PreDispose/Dispose.
		/// <para>
		/// À utiliser avant un reload de domaine, où aucun <c>await</c> ne peut être pompé.
		/// Idempotent : une entrée déjà démontée est ignorée par ses propres gardes d'état.
		/// </para>
		/// </summary>
		public void DisposeSync() {
			Logger.LogDebug($"Disposing (sync) {_metadata.Id}@{_metadata.Version}");

			// Comme Unload() : une entrée n'est démontée que si elle a été désactivée.
			foreach (var entry in _entryPoints)
				entry.Disable();

			foreach (var entry in _entryPoints)
				entry.OnPreDisposeSync();

			foreach (var entry in _entryPoints)
				entry.OnDisposeSync();

			// Libère les éventuelles bibliothèques natives chargées par le mod
			CoreAPI?.LibAPI?.Unload();
		}

		public override string ToString()
			=> $"{GetType().Name}[id={_metadata.Id}, version={_metadata.Version}]";
	}

	public enum InitializerState : byte {
		None = 0,
		Initialized = 1,
		PostInitialized = 2,

		PreDisposed = 4,
		Disposed = 8,

		Ready = PostInitialized & Disposed,
		Done = Initialized & Disposed
	}

	public enum ExecutionEventStatus : byte {
		None = 0,
		Pre = 1,
		Start = 2,
		Success = 3,
		Error = 4,
	}


	public class ModEventContext : EventContext {
		private readonly object[] _data;
		private readonly string _eventName;

		public ModEventContext(string eventName, params object[] data) {
			_eventName = eventName;
			_data = data;
		}

		public object[] Data
			=> _data;

		public string Destination
			=> null;

		public string EventName
			=> _eventName;

		public EventEntryFlags Channel
			=> EventEntryFlags.Client | EventEntryFlags.Main | EventEntryFlags.Editor;

		public Action<object[]> Callback { get; }
	}

}
