using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Nox.CCK.Mods;
using Nox.CCK.Mods.Events;
using Nox.CCK.Mods.Metadata;
using Nox.CCK.Utils;
using Nox.ModLoader.Cores.Assets;
using Nox.ModLoader.EntryPoints;

namespace Nox.ModLoader.Mods {
	public abstract class Mod : IMod {
		internal CoreAPI            CoreAPI;
		internal IAssetAPI          AssetAPI;
		internal Typing.ModMetadata Metadata;

		public readonly Profiler Profiler;

		public abstract AppDomain GetAppDomain();

		public abstract Assembly[] GetAssemblies();

		public IEnumerable<Profile> GetProfiler()
			=> Profiler.GetAllProfiles();

		public ModMetadata GetMetadata()
			=> Metadata;

		internal Mod() {
			CoreAPI  = new CoreAPI(this);
			Profiler = new Profiler();
		}

		#region Data Storage

		public T GetData<T>(string key, T defaultValue = default)
			=> HasData<T>(key) ? (T)GetDatas()[key] : defaultValue;

		public bool SetData<T>(string key, T value) {
			GetDatas()[key] = value;
			return true;
		}

		public bool HasData<T>(string key)
			=> GetDatas().ContainsKey(key);

		public Dictionary<string, object> GetDatas()
			=> Metadata.InternalData;

		#endregion

		#region Entrypoints Instances

		private EntryPoint[] _entryPoints = Array.Empty<EntryPoint>();

		public T GetInstance<T>() {
			T instance = default;

			foreach (var entry in _entryPoints) {
				instance = entry.GetInstance<T>();
				if (instance != null) break;
			}

			return instance;
		}

		public T[] GetInstances<T>() {
			var instances = new List<T>();

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

		public virtual bool IsLoaded()
			=> true;

		public bool HasUpdate;
		public bool HasFixedUpdate;
		public bool HasLateUpdate;

		public virtual UniTask<bool> Load() {
			CoreAPI.EventAPI.Emit(new ModEventContext("mod_loaded", this));

			var entryPointKeys = Metadata.GetEntryPoints().GetAll().Keys;
			_entryPoints = new EntryPoint[entryPointKeys.Count];
			var index = 0;
			foreach (var entry in entryPointKeys)
				_entryPoints[index++] = new EntryPoint(this, entry);

			HasUpdate      = false;
			HasFixedUpdate = false;
			HasLateUpdate  = false;

			foreach (var entry in _entryPoints) {
				if (entry.HasUpdate) HasUpdate           = true;
				if (entry.HasFixedUpdate) HasFixedUpdate = true;
				if (entry.HasLateUpdate) HasLateUpdate   = true;
			}

			return UniTask.FromResult(true);
		}

		public virtual async UniTask<bool> Unload() {
			Logger.LogDebug($"Unloading {Metadata.GetId()}@{Metadata.GetVersion()}");

			// if not disabled, disable
			foreach (var entry in _entryPoints)
				entry.Disable();

			// send pre-dispose and dispose
			await PreDispose();
			await Dispose();

			// clear all states

			foreach (var entry in _entryPoints)
				entry.Dispose();

			_entryPoints = Array.Empty<EntryPoint>();
			CoreAPI.EventAPI.Emit(new ModEventContext("mod_unloaded", this));
			return true;
		}

		public async UniTask Initialize() {
			Profiler.Set("initialize", Profiler.At.Start, DateTime.UtcNow);
			var promises = new UniTask[_entryPoints.Length];
			for (var i = 0; i < _entryPoints.Length; i++)
				promises[i] = _entryPoints[i].OnInitialize();
			await UniTask.WhenAll(promises);
			Profiler.Set("initialized", Profiler.At.Start, DateTime.UtcNow);
		}

		public async UniTask PostInitialize() {
			Profiler.Set("post_initialize", Profiler.At.Start, DateTime.UtcNow);
			var promises = new UniTask[_entryPoints.Length];
			for (var i = 0; i < _entryPoints.Length; i++)
				promises[i] = _entryPoints[i].OnPostInitialize();
			await UniTask.WhenAll(promises);
			Profiler.Set("post_initialized", Profiler.At.Start, DateTime.UtcNow);
		}

		public async UniTask PreDispose() {
			Profiler.Set("pre_dispose", Profiler.At.Start, DateTime.UtcNow);
			var promises = new UniTask[_entryPoints.Length];
			for (var i = 0; i < _entryPoints.Length; i++)
				promises[i] = _entryPoints[i].OnPreDispose();
			await UniTask.WhenAll(promises);
			Profiler.Set("pre_disposed", Profiler.At.Start, DateTime.UtcNow);
		}

		public void Update() {
			if (!HasUpdate) return;
			Profiler.Set("update", Profiler.At.Start, DateTime.UtcNow);
			foreach (var entry in _entryPoints)
				entry.OnUpdate();
			Profiler.Set("updated", Profiler.At.Start, DateTime.UtcNow);
		}

		public void FixedUpdate() {
			if (!HasFixedUpdate) return;
			Profiler.Set("fixed_update", Profiler.At.Start, DateTime.UtcNow);
			foreach (var entry in _entryPoints)
				entry.OnFixedUpdate();
			Profiler.Set("fixed_updated", Profiler.At.Start, DateTime.UtcNow);
		}

		public void LateUpdate() {
			if (!HasLateUpdate) return;
			Profiler.Set("late_update", Profiler.At.Start, DateTime.UtcNow);
			foreach (var entry in _entryPoints)
				entry.OnLateUpdate();
			Profiler.Set("late_updated", Profiler.At.Start, DateTime.UtcNow);
		}

		public async UniTask Dispose() {
			Profiler.Set("dispose", Profiler.At.Start, DateTime.UtcNow);
			var promises = new UniTask[_entryPoints.Length];
			for (var i = 0; i < _entryPoints.Length; i++)
				promises[i] = _entryPoints[i].OnDispose();
			await UniTask.WhenAll(promises);
			Profiler.Set("disposed", Profiler.At.Start, DateTime.UtcNow);
		}

		public override string ToString()
			=> $"{GetType().Name}[id={Metadata.GetId()}, version={Metadata.GetVersion()}]";
	}

	public enum InitializerState : byte {
		None            = 0,
		Initialized     = 1,
		PostInitialized = 2,

		PreDisposed = 4,
		Disposed    = 8,

		Ready = PostInitialized & Disposed,
		Done  = Initialized     & Disposed
	}

	public enum ExecutionEventStatus : byte {
		None    = 0,
		Pre     = 1,
		Start   = 2,
		Success = 3,
		Error   = 4,
	}


	public class ModEventContext : EventContext {
		private readonly object[] _data;
		private readonly string   _eventName;

		public ModEventContext(string eventName, params object[] data) {
			_eventName = eventName;
			_data      = data;
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