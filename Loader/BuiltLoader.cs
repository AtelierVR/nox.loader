#if !UNITY_EDITOR
using UnityEngine;

namespace Nox.ModLoader.Loader {
	public static class BuiltLoader {
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void OnInitialize() {
			Debug.Log("Initializing Mod Loader...");
			RuntimeLoader.Enable();
		}
	}
}
#endif
