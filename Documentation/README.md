# Nox Mod Loader - Archive & Runtime Loading System

## Overview

The Nox Mod Loader supports loading mods from:
- **Folders**: Traditional folder-based mods with `nox.mod.json`
- **Archives**: ZIP or `.noxmod` files containing mods
- **Managed DLLs**: .NET assemblies (Mono or ILRuntime for IL2CPP)
- **Native Plugins**: C++ DLLs that can be loaded/unloaded at runtime

## Configuration

Add the following to your `config.json`:

```json
{
  "mod_folders": [
    "C:/MyMods",
    "D:/Games/NoxMods"
  ],
  "archive_folders": [
    "C:/MyMods/Archives"
  ]
}
```

## Mod Archive Structure

A mod archive (`.zip` or `.noxmod`) should contain:

```
my-mod.noxmod
├── nox.mod.json        # Required: Mod metadata
├── managed/            # Managed .NET DLLs
│   ├── MyMod.dll
│   └── MyMod.dll.bytes # ILRuntime bytecode (for IL2CPP)
├── native/             # Native C++ plugins
│   ├── win-x64/
│   │   └── myplugin.dll
│   ├── linux-x64/
│   │   └── libmyplugin.so
│   └── osx-x64/
│       └── libmyplugin.dylib
└── assets/             # Asset bundles
    └── myassets.bundle
```

## nox.mod.json Example

```json
{
  "id": "com.example.mymod",
  "version": "1.0.0",
  "name": "My Mod",
  "description": "An example mod",
  "references": [
    {
      "name": "MyMod",
      "file": "managed/MyMod.dll",
      "engine": { "name": "mono", "version": ">=0.0.0" }
    },
    {
      "name": "MyModNative",
      "file": "native/win-x64/myplugin.dll",
      "platform": "windows"
    }
  ],
  "entrypoints": {
    "main": ["MyMod.ModMain"]
  }
}
```

## IL2CPP Support

For IL2CPP builds, managed code must be pre-compiled to ILRuntime bytecode:

1. Convert your DLL to `.dll.bytes` format
2. Include both `.dll` (for Mono) and `.dll.bytes` (for IL2CPP) in your archive
3. The loader will automatically use the appropriate version

### Creating ILRuntime Bytecode

```bash
# Use the ILRuntime compiler to generate bytecode
ilrt compile MyMod.dll -o MyMod.dll.bytes
```

## Native Plugin Interface

Native C++ plugins should export these functions:

```cpp
extern "C" {
    // Called when the mod is loaded
    // Return 0 for success, non-zero for error
    __declspec(dllexport) int NoxMod_Initialize();
    
    // Called when the mod is unloaded
    __declspec(dllexport) void NoxMod_Shutdown();
    
    // Called every frame (optional)
    __declspec(dllexport) void NoxMod_Update(float deltaTime);
    
    // Returns the plugin version string (optional)
    __declspec(dllexport) const char* NoxMod_GetVersion();
}
```

## API Usage

### Loading Mods

```csharp
// Load all discovered mods
var result = await ModManager.LoadMods();

// Load specific mods
var result = await ModManager.LoadMods(new[] { "com.example.mymod" });
```

### Unloading Mods

```csharp
// Unload a specific mod
var result = await ModManager.UnloadMod("com.example.mymod");

// Unload multiple mods
var result = await ModManager.UnloadMods(new[] { "mod1", "mod2" });

// Unload all mods
var result = await ModManager.UnloadAllMods();
```

### Reloading Mods

```csharp
// Reload a mod (unload then load)
bool success = await ModManager.ReloadMod("com.example.mymod");
```

### Events

```csharp
ModManager.OnModLoaded += (mod) => {
    Debug.Log($"Mod loaded: {mod.GetMetadata().GetId()}");
};

ModManager.OnModUnloaded += (mod) => {
    Debug.Log($"Mod unloaded: {mod.GetMetadata().GetId()}");
};
```

## Limitations

### Mono Runtime
- Assemblies loaded from bytes cannot be fully unloaded
- GC will clean up references but the assembly metadata remains in memory
- Use AppDomain isolation for true unloading (not available in Unity)

### IL2CPP Runtime
- Dynamic code loading requires ILRuntime interpretation
- Performance may be lower than native execution
- Some reflection features may be limited

### Native Plugins
- Platform-specific binaries required
- Must be properly signed on some platforms (macOS, iOS)
- iOS does not support dynamic native library loading
