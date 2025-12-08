using System.Collections.Generic;
using System.Linq;

namespace Nox.ModLoader.Permissions
{
    /// <summary>
    /// Registry of all available mod permissions.
    /// </summary>
    public static class PermissionRegistry
    {
        private static readonly Dictionary<string, ModPermission> Permissions = new();
        private static bool _initialized;

        /// <summary>
        /// Initialize the registry with default permissions.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            RegisterDefaultPermissions();
        }

        private static void RegisterDefaultPermissions()
        {
            // === File I/O Permissions ===
            Register(new ModPermission(
                id: "io",
                displayName: "File Access",
                description: "Base permission for file system access.",
                risk: PermissionRisk.Medium
            ));

            Register(new ModPermission(
                id: "io.read",
                displayName: "Read Files",
                description: "Allows reading files from the file system.",
                risk: PermissionRisk.Low,
                allowedTypePatterns: new[]
                {
                    @"^System\.IO\.(File|FileInfo|Directory|DirectoryInfo|Path)$",
                    @"^System\.IO\.(StreamReader|BinaryReader|FileStream)$",
                    @"^System\.IO\.TextReader$"
                },
                allowedNamespacePatterns: new[] { @"^System\.IO$" },
                parentId: "io"
            ));

            Register(new ModPermission(
                id: "io.write",
                displayName: "Write Files",
                description: "Allows writing files to the file system.",
                risk: PermissionRisk.Medium,
                allowedTypePatterns: new[]
                {
                    @"^System\.IO\.(File|FileInfo|Directory|DirectoryInfo)$",
                    @"^System\.IO\.(StreamWriter|BinaryWriter|FileStream)$",
                    @"^System\.IO\.TextWriter$"
                },
                allowedNamespacePatterns: new[] { @"^System\.IO$" },
                parentId: "io"
            ));

            // === Network Permissions ===
            Register(new ModPermission(
                id: "network",
                displayName: "Network Access",
                description: "Base permission for network operations.",
                risk: PermissionRisk.High
            ));

            Register(new ModPermission(
                id: "network.http",
                displayName: "HTTP Requests",
                description: "Allows making HTTP/HTTPS requests.",
                risk: PermissionRisk.Medium,
                allowedTypePatterns: new[]
                {
                    @"^System\.Net\.Http\.(HttpClient|HttpRequestMessage|HttpResponseMessage)$",
                    @"^System\.Net\.WebClient$",
                    @"^System\.Net\.HttpWebRequest$",
                    @"^UnityEngine\.Networking\.UnityWebRequest$"
                },
                allowedNamespacePatterns: new[] { @"^System\.Net\.Http$" },
                parentId: "network"
            ));

            Register(new ModPermission(
                id: "network.socket",
                displayName: "Raw Sockets",
                description: "Allows raw socket connections (TCP/UDP).",
                risk: PermissionRisk.High,
                allowedTypePatterns: new[]
                {
                    @"^System\.Net\.Sockets\.(Socket|TcpClient|TcpListener|UdpClient)$",
                    @"^System\.Net\.Sockets\.NetworkStream$"
                },
                allowedNamespacePatterns: new[] { @"^System\.Net\.Sockets$" },
                parentId: "network"
            ));

            Register(new ModPermission(
                id: "network.websocket",
                displayName: "WebSocket",
                description: "Allows WebSocket connections.",
                risk: PermissionRisk.Medium,
                allowedTypePatterns: new[]
                {
                    @"^System\.Net\.WebSockets\.(ClientWebSocket|WebSocket)$"
                },
                allowedNamespacePatterns: new[] { @"^System\.Net\.WebSockets$" },
                parentId: "network"
            ));

            // === Reflection Permissions ===
            Register(new ModPermission(
                id: "reflection",
                displayName: "Reflection",
                description: "Base permission for reflection operations.",
                risk: PermissionRisk.High
            ));

            Register(new ModPermission(
                id: "reflection.read",
                displayName: "Read Reflection",
                description: "Allows reading type information via reflection.",
                risk: PermissionRisk.Low,
                allowedTypePatterns: new[]
                {
                    @"^System\.Type$",
                    @"^System\.Reflection\.(MethodInfo|PropertyInfo|FieldInfo|MemberInfo)$",
                    @"^System\.Reflection\.(ConstructorInfo|EventInfo|ParameterInfo)$",
                    @"^System\.Reflection\.Assembly$"
                },
                parentId: "reflection"
            ));

            Register(new ModPermission(
                id: "reflection.invoke",
                displayName: "Invoke via Reflection",
                description: "Allows invoking methods via reflection.",
                risk: PermissionRisk.Medium,
                allowedTypePatterns: new[]
                {
                    @"^System\.Reflection\.MethodBase$",
                    @"^System\.Activator$"
                },
                parentId: "reflection"
            ));

            Register(new ModPermission(
                id: "reflection.emit",
                displayName: "Dynamic Code Generation",
                description: "Allows generating code at runtime via Reflection.Emit.",
                risk: PermissionRisk.Critical,
                allowedTypePatterns: new[]
                {
                    @"^System\.Reflection\.Emit\..+$"
                },
                allowedNamespacePatterns: new[] { @"^System\.Reflection\.Emit$" },
                parentId: "reflection"
            ));

            // === Process Permissions ===
            Register(new ModPermission(
                id: "process",
                displayName: "Process Execution",
                description: "Allows executing external processes.",
                risk: PermissionRisk.Critical,
                allowedTypePatterns: new[]
                {
                    @"^System\.Diagnostics\.Process(StartInfo)?$"
                },
                allowedNamespacePatterns: new[] { @"^System\.Diagnostics$" }
            ));

            // === Environment Permissions ===
            Register(new ModPermission(
                id: "environment",
                displayName: "Environment Access",
                description: "Allows access to environment variables and system info.",
                risk: PermissionRisk.Medium,
                allowedTypePatterns: new[]
                {
                    @"^System\.Environment$"
                }
            ));

            // === Registry Permissions (Windows) ===
            Register(new ModPermission(
                id: "registry",
                displayName: "Windows Registry",
                description: "Allows access to Windows Registry.",
                risk: PermissionRisk.Critical,
                allowedTypePatterns: new[]
                {
                    @"^Microsoft\.Win32\.Registry(Key)?$"
                },
                allowedNamespacePatterns: new[] { @"^Microsoft\.Win32$" }
            ));

            // === Native Interop Permissions ===
            Register(new ModPermission(
                id: "native",
                displayName: "Native Interop",
                description: "Allows calling native code via P/Invoke.",
                risk: PermissionRisk.Critical,
                allowedTypePatterns: new[]
                {
                    @"^System\.Runtime\.InteropServices\.(Marshal|DllImportAttribute)$"
                },
                allowedNamespacePatterns: new[] { @"^System\.Runtime\.InteropServices$" }
            ));

            // === AppDomain Permissions ===
            Register(new ModPermission(
                id: "appdomain",
                displayName: "AppDomain Access",
                description: "Allows access to AppDomain functionality.",
                risk: PermissionRisk.Critical,
                allowedTypePatterns: new[]
                {
                    @"^System\.AppDomain$"
                }
            ));

            // === Config Permissions (Nox-specific) ===
            Register(new ModPermission(
                id: "config",
                displayName: "Configuration",
                description: "Base permission for configuration access.",
                risk: PermissionRisk.Low
            ));

            Register(new ModPermission(
                id: "config.read",
                displayName: "Read Configuration",
                description: "Allows reading mod configuration.",
                risk: PermissionRisk.Low,
                parentId: "config"
            ));

            Register(new ModPermission(
                id: "config.write",
                displayName: "Write Configuration",
                description: "Allows writing mod configuration.",
                risk: PermissionRisk.Low,
                parentId: "config"
            ));

            Register(new ModPermission(
                id: "config.global",
                displayName: "Global Configuration",
                description: "Allows access to global application configuration.",
                risk: PermissionRisk.Medium,
                parentId: "config"
            ));

            // === Unsafe Permissions ===
            Register(new ModPermission(
                id: "unsafe",
                displayName: "Unsafe Code",
                description: "Allows use of unsafe code and pointers.",
                risk: PermissionRisk.Critical,
                allowedTypePatterns: new[]
                {
                    @"^System\.IntPtr$",
                    @"^System\.UIntPtr$"
                }
            ));

            // === Kernel Permission (full access) ===
            Register(new ModPermission(
                id: "kernel",
                displayName: "Kernel Access",
                description: "Full system access for core mods. Bypasses all security checks.",
                risk: PermissionRisk.Critical
            ));
        }

        /// <summary>
        /// Register a new permission.
        /// </summary>
        public static void Register(ModPermission permission)
        {
            if (permission == null) return;
            Permissions[permission.Id] = permission;
        }

        /// <summary>
        /// Get a permission by ID.
        /// </summary>
        public static ModPermission Get(string permissionId)
        {
            if (string.IsNullOrEmpty(permissionId)) return null;
            return Permissions.TryGetValue(permissionId, out var permission) ? permission : null;
        }

        /// <summary>
        /// Get all registered permissions.
        /// </summary>
        public static IReadOnlyCollection<ModPermission> GetAll()
            => Permissions.Values;

        /// <summary>
        /// Get all child permissions for a parent permission.
        /// </summary>
        public static IEnumerable<ModPermission> GetChildren(string parentId)
            => Permissions.Values.Where(p => p.ParentId == parentId);

        /// <summary>
        /// Check if a permission ID exists.
        /// </summary>
        public static bool Exists(string permissionId)
            => !string.IsNullOrEmpty(permissionId) && Permissions.ContainsKey(permissionId);

        /// <summary>
        /// Resolve all permissions including parent permissions.
        /// For example, if a mod has "io.read", this also includes "io".
        /// </summary>
        public static HashSet<string> ResolvePermissions(IEnumerable<string> permissionIds)
        {
            var resolved = new HashSet<string>();
            if (permissionIds == null) return resolved;

            foreach (var id in permissionIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                resolved.Add(id);

                // Add parent permissions
                var permission = Get(id);
                if (permission?.ParentId != null)
                {
                    var parentId = permission.ParentId;
                    while (!string.IsNullOrEmpty(parentId))
                    {
                        resolved.Add(parentId);
                        var parent = Get(parentId);
                        parentId = parent?.ParentId;
                    }
                }

                // If this is a parent permission, add all children
                foreach (var child in GetChildren(id))
                    resolved.Add(child.Id);
            }

            return resolved;
        }
    }
}
