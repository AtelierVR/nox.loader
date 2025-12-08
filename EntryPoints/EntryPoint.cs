using System;
using System.Data;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Mods.Initializers;
using Nox.ModLoader.Mods;

namespace Nox.ModLoader.EntryPoints {
	// ReSharper disable MethodHasAsyncOverload
	public class EntryPoint : IDisposable {
		public const string MainEntry   = "main";
		public const string ClientEntry = "client";
		public const string ServerEntry = "server";
		public const string EditorEntry = "editor";

		internal readonly Mod              Mod;
		internal readonly string           Name;
		private           bool             _enabled;
		private           Instance[]       _instances;
		private           InitializerState _state = InitializerState.None;

		public EntryPoint(Mod mod, string entry) {
			Mod  = mod;
			Name = entry;
		}

		public bool IsEnabled()
			=> _enabled;

		public TI GetInstance<TI>()
			=> _instances != null
				? _instances
					.Select(e => e.Reference)
					.OfType<TI>()
					.FirstOrDefault()
				: default;

		public TI[] GetInstances<TI>()
			=> _instances != null
				? _instances
					.Select(e => e.Reference)
					.OfType<TI>()
					.ToArray()
				: Array.Empty<TI>();

		public void Disable() {
			if (!_enabled) return;
			_enabled = false;
			Mod.CoreAPI.EventAPI.Emit("mod_disabled", Mod, Name);
		}

		public bool HasUpdate      = true;
		public bool HasFixedUpdate = true;
		public bool HasLateUpdate  = true;

		public void Enable() {
			if (_enabled) return;

			_instances ??= this.Instantiate<IModInitializer>()
				.Select(e => new Instance(this, e))
				.ToArray();

			HasUpdate      = _instances.Any(i => i.HasUpdate);
			HasFixedUpdate = _instances.Any(i => i.HasFixedUpdate);
			HasLateUpdate  = _instances.Any(i => i.HasLateUpdate);

			_enabled = true;
			Mod.CoreAPI.EventAPI.Emit("mod_enabled", Mod, Name);
		}

		// ReSharper disable Unity.PerformanceAnalysis
		public void Dispose() {
			Disable();
			if (_instances != null)
				foreach (var instance in _instances)
					if (instance.Reference is IDisposable disposable)
						disposable.Dispose();
			_instances = null;
		}

		public async UniTask OnInitialize() {
			if (!IsEnabled() || _state != InitializerState.None)
				return;

			var eventCtx = Mod.CoreAPI.EventAPI;
			var profiler = Mod.Profiler;
			_state = InitializerState.Initialized;

			eventCtx.Emit(new ModEventContext("mod_initialize", Mod, Name, ExecutionEventStatus.Pre));
			profiler.Set("initialize", Name, Profiler.At.Start, DateTime.UtcNow);

			for (var i = 0; i < _instances.Length; i++) {
				var instance = _instances[i].Reference;

				profiler.Set("initialize", Name, i.ToString(), Profiler.At.Start, DateTime.UtcNow);
				eventCtx.Emit("mod_initialize", Mod, Name, ExecutionEventStatus.Start, instance);

				try {
					instance.OnInitialize(Mod.CoreAPI);
					await instance.OnInitializeAsync(Mod.CoreAPI);

					if (instance is IMainModInitializer m) {
						m.OnInitializeMain(Mod.CoreAPI);
						await m.OnInitializeMainAsync(Mod.CoreAPI);
					}

					if (instance is IEditorModInitializer e) {
						e.OnInitializeEditor(Mod.CoreAPI);
						await e.OnInitializeEditorAsync(Mod.CoreAPI);
					}

					if (instance is IServerModInitializer s) {
						s.OnInitializeServer(Mod.CoreAPI);
						await s.OnInitializeServerAsync(Mod.CoreAPI);
					}

					if (instance is IClientModInitializer c) {
						c.OnInitializeClient(Mod.CoreAPI);
						await c.OnInitializeClientAsync(Mod.CoreAPI);
					}

					eventCtx.Emit("mod_initialize", Mod, Name, ExecutionEventStatus.Success, instance);
				} catch (Exception e) {
					Mod.CoreAPI.LoggerAPI.LogException(new Exception($"Failed to initialize mod {Mod.Metadata.GetId()}@{Mod.Metadata.GetVersion()}", e));
					eventCtx.Emit("mod_initialize", Mod, Name, ExecutionEventStatus.Error, instance, e);
				}

				profiler.Set("initialize", Name, i.ToString(), Profiler.At.End, DateTime.UtcNow);
			}

			profiler.Set("initialize", Name, Profiler.At.End, DateTime.UtcNow);
		}

		public async UniTask OnPostInitialize() {
			if (!IsEnabled() || _state != InitializerState.Initialized)
				return;
			_state = InitializerState.PostInitialized;

			var eventCtx = Mod.CoreAPI.EventAPI;
			var profiler = Mod.Profiler;
			eventCtx.Emit(new ModEventContext("mod_post_initialize", Mod, Name, ExecutionEventStatus.Pre));
			profiler.Set("post_initialize", Name, Profiler.At.Start, DateTime.UtcNow);

			for (var i = 0; i < _instances.Length; i++) {
				var instance = _instances[i].Reference;

				profiler.Set("post_initialize", Name, i.ToString(), Profiler.At.Start, DateTime.UtcNow);
				eventCtx.Emit("mod_post_initialize", Mod, Name, ExecutionEventStatus.Start, instance);

				try {
					instance.OnPostInitialize();
					await instance.OnPostInitializeAsync();

					if (instance is IMainModInitializer m) {
						m.OnPostInitializeMain();
						await m.OnPostInitializeMainAsync();
					}

					if (instance is IEditorModInitializer e) {
						e.OnPostInitializeEditor();
						await e.OnPostInitializeEditorAsync();
					}

					if (instance is IServerModInitializer s) {
						s.OnPostInitializeServer();
						await s.OnPostInitializeServerAsync();
					}

					if (instance is IClientModInitializer c) {
						c.OnPostInitializeClient();
						await c.OnPostInitializeClientAsync();
					}

					eventCtx.Emit("mod_post_initialize", Mod, Name, ExecutionEventStatus.Success, instance);
				} catch (Exception e) {
					Mod.CoreAPI.LoggerAPI.LogException(new Exception($"Failed to post-initialize mod {Mod.Metadata.GetId()}@{Mod.Metadata.GetVersion()}", e));
					eventCtx.Emit("mod_post_initialize", Mod, Name, ExecutionEventStatus.Error, instance, e);
				}

				profiler.Set("post_initialize", Name, i.ToString(), Profiler.At.End, DateTime.UtcNow);
			}

			profiler.Set("post_initialize", Name, Profiler.At.End, DateTime.UtcNow);
		}

		public async UniTask OnPreDispose() {
			// Ne dispose que les entrées qui ont été désactivées ou qui n'ont jamais été initialisées
			if (IsEnabled() || _state != InitializerState.PostInitialized)
				return;
			_state = InitializerState.PreDisposed;
			var eventCtx = Mod.CoreAPI.EventAPI;
			var profiler = Mod.Profiler;

			eventCtx.Emit(new ModEventContext("mod_pre_dispose", Mod, Name, ExecutionEventStatus.Pre));
			profiler.Set("pre_dispose", Name, Profiler.At.Start, DateTime.UtcNow);

			for (var i = 0; i < _instances.Length; i++) {
				var instance = _instances[i].Reference;

				profiler.Set("pre_dispose", Name, i.ToString(), Profiler.At.Start, DateTime.UtcNow);
				eventCtx.Emit("mod_pre_dispose", Mod, Name, ExecutionEventStatus.Start, instance);

				try {
					if (instance is IClientModInitializer c) {
						c.OnPreDisposeClient();
						await c.OnPreDisposeClientAsync();
					}

					if (instance is IServerModInitializer s) {
						s.OnPreDisposeServer();
						await s.OnPreDisposeServerAsync();
					}

					if (instance is IEditorModInitializer e) {
						e.OnPreDisposeEditor();
						await e.OnPreDisposeEditorAsync();
					}

					if (instance is IMainModInitializer m) {
						m.OnPreDisposeMain();
						await m.OnPreDisposeMainAsync();
					}

					instance.OnPreDispose();
					await instance.OnPreDisposeAsync();

					eventCtx.Emit("mod_pre_dispose", Mod, Name, ExecutionEventStatus.Success, instance);
				} catch (Exception e) {
					Mod.CoreAPI.LoggerAPI.LogException(new Exception($"Failed to pre-dispose mod {Mod.Metadata.GetId()}@{Mod.Metadata.GetVersion()}", e));
					eventCtx.Emit("mod_pre_dispose", Mod, Name, ExecutionEventStatus.Error, instance, e);
				}

				profiler.Set("pre_dispose", Name, i.ToString(), Profiler.At.End, DateTime.UtcNow);
			}

			profiler.Set("pre_dispose", Name, Profiler.At.End, DateTime.UtcNow);
		}


		public async UniTask OnDispose() {
			// Ne dispose que les entrées qui ont été désactivées
			if (IsEnabled() || _state != InitializerState.PreDisposed)
				return;
			_state = InitializerState.Disposed;
			var eventCtx = Mod.CoreAPI.EventAPI;
			var profiler = Mod.Profiler;

			eventCtx.Emit(new ModEventContext("mod_dispose", Mod, Name, ExecutionEventStatus.Pre));
			profiler.Set("dispose", Name, Profiler.At.Start, DateTime.UtcNow);

			for (var i = 0; i < _instances.Length; i++) {
				var instance = _instances[i].Reference;

				profiler.Set("dispose", Name, i.ToString(), Profiler.At.Start, DateTime.UtcNow);
				eventCtx.Emit("mod_dispose", Mod, Name, ExecutionEventStatus.Start, instance);

				try {
					if (instance is IClientModInitializer c) {
						c.OnDisposeClient();
						await c.OnDisposeClientAsync();
					}

					if (instance is IServerModInitializer s) {
						s.OnDisposeServer();
						await s.OnDisposeServerAsync();
					}

					if (instance is IEditorModInitializer e) {
						e.OnDisposeEditor();
						await e.OnDisposeEditorAsync();
					}

					if (instance is IMainModInitializer m) {
						m.OnDisposeMain();
						await m.OnDisposeMainAsync();
					}

					instance.OnDispose();
					await instance.OnDisposeAsync();

					eventCtx.Emit("mod_dispose", Mod, Name, ExecutionEventStatus.Success, instance);
				} catch (Exception e) {
					Mod.CoreAPI.LoggerAPI.LogException(new Exception($"Failed to dispose mod {Mod.Metadata.GetId()}@{Mod.Metadata.GetVersion()}", e));
					eventCtx.Emit("mod_dispose", Mod, Name, ExecutionEventStatus.Error, instance, e);
				}

				profiler.Set("dispose", Name, i.ToString(), Profiler.At.End, DateTime.UtcNow);
			}

			profiler.Set("dispose", Name, Profiler.At.End, DateTime.UtcNow);
		}

		public void OnUpdate() {
			if (!IsEnabled() || _state != InitializerState.PostInitialized || !HasUpdate)
				return;

			var profiler = Mod.Profiler;
			profiler.Set("update", Name, Profiler.At.Start, DateTime.UtcNow);

			for (var i = 0; i < _instances.Length; i++) {
				var instance = _instances[i].Reference;
				profiler.Set("update", Name, i.ToString(), Profiler.At.Start, DateTime.UtcNow);

				try {
					instance.OnUpdate();

					if (instance is IMainModInitializer m)
						m.OnUpdateMain();

					if (instance is IEditorModInitializer e)
						e.OnUpdateEditor();

					if (instance is IServerModInitializer s)
						s.OnUpdateServer();

					if (instance is IClientModInitializer c)
						c.OnUpdateClient();
				} catch (Exception e) {
					Mod.CoreAPI.LoggerAPI.LogException(new Exception($"Failed to update mod {Mod.Metadata.GetId()}@{Mod.Metadata.GetVersion()}", e));
				}

				profiler.Set("update", Name, i.ToString(), Profiler.At.End, DateTime.UtcNow);
			}

			profiler.Set("update", Name, Profiler.At.End, DateTime.UtcNow);
		}

		public void OnFixedUpdate() {
			if (!IsEnabled() || _state != InitializerState.PostInitialized || !HasFixedUpdate)
				return;

			var profiler = Mod.Profiler;
			profiler.Set("fixed_update", Name, Profiler.At.Start, DateTime.UtcNow);

			for (var i = 0; i < _instances.Length; i++) {
				var instance = _instances[i].Reference;
				profiler.Set("fixed_update", Name, i.ToString(), Profiler.At.Start, DateTime.UtcNow);

				try {
					instance.OnFixedUpdate();

					if (instance is IMainModInitializer m)
						m.OnFixedUpdateMain();

					if (instance is IEditorModInitializer e)
						e.OnFixedUpdateEditor();

					if (instance is IServerModInitializer s)
						s.OnFixedUpdateServer();

					if (instance is IClientModInitializer c)
						c.OnFixedUpdateClient();
				} catch (Exception e) {
					Mod.CoreAPI.LoggerAPI.LogException(new Exception($"Failed to fixed-update mod {Mod.Metadata.GetId()}@{Mod.Metadata.GetVersion()}", e));
				}

				profiler.Set("fixed_update", Name, i.ToString(), Profiler.At.End, DateTime.UtcNow);
			}

			profiler.Set("fixed_update", Name, Profiler.At.End, DateTime.UtcNow);
		}

		public void OnLateUpdate() {
			if (!IsEnabled() || _state != InitializerState.PostInitialized || !HasLateUpdate)
				return;

			var profiler = Mod.Profiler;
			profiler.Set("late_update", Name, Profiler.At.Start, DateTime.UtcNow);

			for (var i = 0; i < _instances.Length; i++) {
				var instance = _instances[i].Reference;
				profiler.Set("late_update", Name, i.ToString(), Profiler.At.Start, DateTime.UtcNow);

				try {
					instance.OnLateUpdate();

					if (instance is IMainModInitializer m)
						m.OnLateUpdateMain();

					if (instance is IEditorModInitializer e)
						e.OnLateUpdateEditor();

					if (instance is IServerModInitializer s)
						s.OnLateUpdateServer();

					if (instance is IClientModInitializer c)
						c.OnLateUpdateClient();
				} catch (Exception e) {
					Mod.CoreAPI.LoggerAPI.LogException(new Exception($"Failed to late-update mod {Mod.Metadata.GetId()}@{Mod.Metadata.GetVersion()}", e));
				}

				profiler.Set("late_update", Name, i.ToString(), Profiler.At.End, DateTime.UtcNow);
			}

			profiler.Set("late_update", Name, Profiler.At.End, DateTime.UtcNow);
		}
	}
}