using System;
using Cysharp.Threading.Tasks;
using Nox.ModLoader.EntryPoints;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Loader {
	public class RuntimeLoader : MonoBehaviour {
		private static RuntimeLoader _instance;
		private bool _initialized;

		#if !UNITY_EDITOR
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void OnBeforeSceneLoad() => Enable();
		#endif

		public static void Enable() {
			if (IsLoaded()) {
				Logger.LogWarning($"Cannot enable because it is already loaded.", _instance, nameof(RuntimeLoader));
				return;
			}

			var go     = new GameObject($"[{nameof(RuntimeLoader)}]");
			var loader = go.AddComponent<RuntimeLoader>();
			DontDestroyOnLoad(go);
			_instance = loader;
			Logger.Log("Initializing", loader, nameof(RuntimeLoader));
		}

		public static bool IsLoaded()
			=> _instance;

		public static bool IsInitialized()
			=> IsLoaded() && _instance._initialized;

		public static void Disable() {
			if (!IsLoaded()) {
				Logger.LogWarning($"Cannot disable because it is not loaded.", null, nameof(RuntimeLoader));
				return;
			}

			Destroy(_instance.gameObject);
		}

		private void Awake() {
			if (!_instance || _instance == this)
				return;
			Logger.LogWarning("A duplicate instance was created. This should not happen. Destroying duplicate.", this, nameof(RuntimeLoader));
			Destroy(gameObject);
		}

		private void Start()
			=> StartAsync().Forget();

		private async UniTask StartAsync() {
			Logger.Log("Starting up", this, nameof(RuntimeLoader));
			try {
				#if UNITY_EDITOR
				LoaderManager.Enable(Application.isBatchMode ? EntryPoint.ServerEntry : EntryPoint.ClientEntry);
				#else
			await LoaderManager.Discover();
			LoaderManager.Enable(EntryPoint.MainEntry, Application.isBatchMode ? EntryPoint.ServerEntry : EntryPoint.ClientEntry);
				#endif
				await LoaderManager.Initialize();
				_initialized = true;
			} catch (Exception e) {
				Logger.LogException(new Exception("Failed to start", e), this, nameof(RuntimeLoader));
				_initialized = false;
			}
		}

		private void Update() {
			if (!_initialized)
				return;
			try {
				LoaderManager.OnUpdate();
			} catch (Exception e) {
				Logger.LogException(new Exception("Error in Update", e), this, nameof(RuntimeLoader));
			}
		}

		private void FixedUpdate() {
			if (!_initialized)
				return;
			try {
				LoaderManager.OnFixedUpdate();
			} catch (Exception e) {
				Logger.LogException(new Exception("Error in FixedUpdate", e), this, nameof(RuntimeLoader));
			}
		}

		private void LateUpdate() {
			if (!_initialized)
				return;
			try {
				LoaderManager.OnLateUpdate();
			} catch (Exception e) {
				Logger.LogException(new Exception("Error in LateUpdate", e), this, nameof(RuntimeLoader));
			}
		}

		private void OnDestroy()
			=> OnDestroyAsync().Forget();

		private async UniTask OnDestroyAsync() {
			if (_instance != this)
				return;
			Logger.Log($"Shutting down", this, nameof(RuntimeLoader));
			#if UNITY_EDITOR
			LoaderManager.Disable(EntryPoint.ServerEntry, EntryPoint.ClientEntry);
			#else
			LoaderManager.Disable(EntryPoint.MainEntry, EntryPoint.ServerEntry, EntryPoint.ClientEntry);
			#endif
			await LoaderManager.Dispose();
			_initialized = false;
			_instance    = null;
		}
	}
}