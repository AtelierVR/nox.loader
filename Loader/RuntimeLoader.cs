using System;
using Cysharp.Threading.Tasks;
using Nox.ModLoader.EntryPoints;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Loader {
	public class RuntimeLoader : MonoBehaviour {
		private static RuntimeLoader _instance;
		private        bool          _initialized;

		#if !UNITY_EDITOR
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void OnBeforeSceneLoad() => Enable();
		#endif

		public static void Enable() {
			if (IsLoaded()) {
				Logger.LogWarning($"{nameof(RuntimeLoader)} is already loaded.", _instance);
				return;
			}

			var go = new GameObject($"[{nameof(RuntimeLoader)}]");
			var loader = go.AddComponent<RuntimeLoader>();
			DontDestroyOnLoad(go);
			_instance = loader;
			Logger.Log($"{nameof(RuntimeLoader)} initialized.", loader);
		}

		public static bool IsLoaded()
			=> _instance;

		public static bool IsInitialized()
			=> IsLoaded() && _instance._initialized;

		public static void Disable() {
			if (!IsLoaded()) {
				Logger.LogWarning($"{nameof(RuntimeLoader)} is not loaded.");
				return;
			}

			Destroy(_instance.gameObject);
		}

		private void Awake() {
			if (!_instance || _instance == this) return;
			Logger.LogWarning($"An instance of {nameof(RuntimeLoader)} already exists. Destroying duplicate.", this);
			Destroy(gameObject);
			return;
		}

		private void Start()
			=> StartAsync().Forget();

		private async UniTask StartAsync() {
			Logger.Log($"{nameof(RuntimeLoader)} starting.", this);
			#if UNITY_EDITOR
			LoaderManager.Enable(Application.isBatchMode ? EntryPoint.ServerEntry : EntryPoint.ClientEntry);
			#else
			await LoaderManager.Discover();
			LoaderManager.Enable(EntryPoint.MainEntry, Application.isBatchMode ? EntryPoint.ServerEntry : EntryPoint.ClientEntry);
			#endif
			await LoaderManager.Initialize();
			_initialized = true;
		}

		private void Update()
			=> LoaderManager.OnUpdate();

		private void FixedUpdate()
		=> LoaderManager.OnFixedUpdate();
		
		private void LateUpdate()
			=> LoaderManager.OnLateUpdate();

		private void OnDestroy()
			=> OnDestroyAsync().Forget();

		private async UniTask OnDestroyAsync() {
			if (_instance != this) return;
			Logger.Log($"{nameof(RuntimeLoader)} shutting down.", this);
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