using System.Linq;
using System.Reflection;
using Nox.CCK.Mods.Initializers;

namespace Nox.ModLoader.EntryPoints {
	public class Instance {
		private static readonly string[] UpdateMethods = {
			nameof(IModInitializer.OnUpdate),
			nameof(IEditorModInitializer.OnUpdateEditor),
			nameof(IMainModInitializer.OnUpdateMain),
			nameof(IClientModInitializer.OnUpdateClient),
			nameof(IServerModInitializer.OnUpdateServer),
		};

		private static readonly string[] FixedUpdateMethods = {
			nameof(IModInitializer.OnFixedUpdate),
			nameof(IEditorModInitializer.OnFixedUpdateEditor),
			nameof(IMainModInitializer.OnFixedUpdateMain),
			nameof(IClientModInitializer.OnFixedUpdateClient),
			nameof(IServerModInitializer.OnFixedUpdateServer),
		};

		private static readonly string[] LateUpdateMethods = {
			nameof(IModInitializer.OnLateUpdate),
			nameof(IEditorModInitializer.OnLateUpdateEditor),
			nameof(IMainModInitializer.OnLateUpdateMain),
			nameof(IClientModInitializer.OnLateUpdateClient),
			nameof(IServerModInitializer.OnLateUpdateServer),
		};

		public readonly IModInitializer Reference;

		public readonly bool HasUpdate;
		public readonly bool HasFixedUpdate;
		public readonly bool HasLateUpdate;

		public Instance(EntryPoint _, IModInitializer reference) {
			Reference      = reference;
			HasUpdate      = HasOneMethod(UpdateMethods);
			HasFixedUpdate = HasOneMethod(FixedUpdateMethods);
			HasLateUpdate  = HasOneMethod(LateUpdateMethods);
		}

		private bool HasOneMethod(string[] names) {
			var type = Reference.GetType();
			return names
				.Select(methodName => type.GetMethod(methodName))
				.Any(method => method != null && !IsMethodEmpty(method));
		}

		private static bool IsMethodEmpty(MethodInfo method) {
			try {
				if (method == null) return true;
				var body = method.GetMethodBody();
				if (body == null) return true;
				var il = body.GetILAsByteArray();
				return il.Length == 1 && il[0] == 0x2A;
			} catch {
				return false;
			}
		}
	}
}