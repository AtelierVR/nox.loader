using System;
using System.Reflection;

public class AssemblyLoader : IDisposable {
	private AppDomain _appDomain;
	private Assembly  _loadedAssembly;

	public AssemblyLoader() {
		// Create a new AppDomain
		_appDomain = AppDomain.CreateDomain("AssemblyLoaderDomain");

		// Subscribe to the AssemblyResolve event to load the assembly from the specified path
		_appDomain.AssemblyResolve += (sender, args) => {
			var assemblyName = new AssemblyName(args.Name);
			var assemblyPath = $"{AppDomain.CurrentDomain.BaseDirectory}{assemblyName.Name}.dll";
			return Assembly.LoadFrom(assemblyPath);
		};

		Console.WriteLine("AppDomain created successfully.");
	}

	public void LoadAssembly(string assemblyPath) {
		if (_appDomain == null)
			throw new InvalidOperationException("AppDomain has already been unloaded.");

		try {
			// Load the assembly in the new AppDomain
			_loadedAssembly = _appDomain.Load(AssemblyName.GetAssemblyName(assemblyPath));
			Console.WriteLine($"Assembly '{_loadedAssembly.FullName}' loaded successfully.");
		} catch (Exception ex) {
			Console.WriteLine($"Failed to load assembly: {ex.Message}");
		}
	}

	public void InvokeMethod(string typeName, string methodName) {
		if (_loadedAssembly == null)
			throw new InvalidOperationException("Assembly not loaded.");

		try {
			var type   = _loadedAssembly.GetType(typeName);
			var method = type?.GetMethod(methodName);
			method?.Invoke(null, null);
		} catch (Exception ex) {
			Console.WriteLine($"Failed to invoke method '{methodName}': {ex.Message}");
		}
	}

	public void Unload() {
		if (_appDomain == null) return;
		AppDomain.Unload(_appDomain);
		_appDomain      = null;
		_loadedAssembly = null;
		Console.WriteLine("AppDomain and assembly successfully unloaded.");
	}

	public void Dispose() {
		Unload();
	}
}