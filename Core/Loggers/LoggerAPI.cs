using System;
using Nox.CCK.Mods.Loggers;
using Nox.CCK.Utils;
using Object = UnityEngine.Object;

namespace Nox.ModLoader.Cores.Loggers {
	public class LoggerAPI : ILoggerAPI {
		private readonly ModLoader.Mods.Mod _mod;

		public LoggerAPI(ModLoader.Mods.Mod mod)
			=> _mod = mod;
		
		public void Log(string message)
			=> Logger.Log(message, tag: _mod.Metadata.GetId());

		public void LogWarning(string message)
			=> Logger.LogWarning(message, tag: _mod.Metadata.GetId());

		public void LogError(string message)
			=> Logger.LogError(message, tag: _mod.Metadata.GetId());

		public void LogDebug(string message)
			=> Logger.LogDebug(message, tag: _mod.Metadata.GetId());

		public void LogException(Exception exception)
			=> Logger.LogException(exception, tag: _mod.Metadata.GetId());

		public void Log(string message, Object context)
			=> Logger.Log(message, context, tag: _mod.Metadata.GetId());

		public void LogWarning(string message, Object context)
			=> Logger.LogWarning(message, context, tag: _mod.Metadata.GetId());

		public void LogError(string message, Object context)
			=> Logger.LogError(message, context, tag: _mod.Metadata.GetId());

		public void LogDebug(string message, Object context)
			=> Logger.LogDebug(message, context, tag: _mod.Metadata.GetId());

		public void LogException(Exception exception, Object context)
			=> Logger.LogException(exception, context, tag: _mod.Metadata.GetId());
	}
}