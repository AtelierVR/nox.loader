using System.Collections.Generic;
using System.Linq;

namespace Nox.ModLoader.Permissions
{
    /// <summary>
    /// Manages permissions for a specific mod.
    /// </summary>
    public class ModPermissionContext
    {
        private readonly HashSet<string> _grantedPermissions;
        private readonly HashSet<string> _resolvedPermissions;
        private readonly string _modId;
        private readonly bool _isKernel;

        /// <summary>
        /// Create a new permission context for a mod.
        /// </summary>
        /// <param name="modId">The mod ID</param>
        /// <param name="declaredPermissions">Permissions declared in nox.mod.json</param>
        /// <param name="isKernel">Whether this is a kernel mod</param>
        public ModPermissionContext(string modId, IEnumerable<string> declaredPermissions, bool isKernel = false)
        {
            _modId = modId;
            _isKernel = isKernel;
            _grantedPermissions = new HashSet<string>(declaredPermissions ?? Enumerable.Empty<string>());

            // Initialize registry if needed
            PermissionRegistry.Initialize();

            // Resolve all permissions (including parents and children)
            _resolvedPermissions = PermissionRegistry.ResolvePermissions(_grantedPermissions);

            // Kernel mods have all permissions
            if (_isKernel)
            {
                _resolvedPermissions.Add("kernel");
            }
        }

        /// <summary>
        /// Get the mod ID.
        /// </summary>
        public string ModId => _modId;

        /// <summary>
        /// Check if this is a kernel mod (bypasses all checks).
        /// </summary>
        public bool IsKernel => _isKernel;

        /// <summary>
        /// Check if the mod has a specific permission.
        /// </summary>
        public bool HasPermission(string permissionId)
        {
            if (string.IsNullOrEmpty(permissionId)) return false;
            if (_isKernel) return true;
            return _resolvedPermissions.Contains(permissionId);
        }

        /// <summary>
        /// Check if a type is allowed based on granted permissions.
        /// </summary>
        public bool IsTypeAllowed(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName)) return true;
            if (_isKernel) return true;

            foreach (var permissionId in _resolvedPermissions)
            {
                var permission = PermissionRegistry.Get(permissionId);
                if (permission?.AllowsType(typeFullName) == true)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a namespace is allowed based on granted permissions.
        /// </summary>
        public bool IsNamespaceAllowed(string namespaceName)
        {
            if (string.IsNullOrEmpty(namespaceName)) return true;
            if (_isKernel) return true;

            foreach (var permissionId in _resolvedPermissions)
            {
                var permission = PermissionRegistry.Get(permissionId);
                if (permission?.AllowsNamespace(namespaceName) == true)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if an assembly is allowed based on granted permissions.
        /// </summary>
        public bool IsAssemblyAllowed(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return true;
            if (_isKernel) return true;

            foreach (var permissionId in _resolvedPermissions)
            {
                var permission = PermissionRegistry.Get(permissionId);
                if (permission?.AllowsAssembly(assemblyName) == true)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get all granted permissions.
        /// </summary>
        public IReadOnlyCollection<string> GetGrantedPermissions()
            => _grantedPermissions;

        /// <summary>
        /// Get all resolved permissions (including inherited).
        /// </summary>
        public IReadOnlyCollection<string> GetResolvedPermissions()
            => _resolvedPermissions;

        /// <summary>
        /// Get the permission that allows a specific type (if any).
        /// </summary>
        public ModPermission GetPermissionForType(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName)) return null;

            foreach (var permissionId in _resolvedPermissions)
            {
                var permission = PermissionRegistry.Get(permissionId);
                if (permission?.AllowsType(typeFullName) == true)
                    return permission;
            }

            return null;
        }

        /// <summary>
        /// Find which permission would be required to allow a type.
        /// </summary>
        public static ModPermission FindRequiredPermissionForType(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName)) return null;

            foreach (var permission in PermissionRegistry.GetAll())
            {
                if (permission.AllowsType(typeFullName))
                    return permission;
            }

            return null;
        }

        /// <summary>
        /// Get a summary of permission status for logging.
        /// </summary>
        public string GetSummary()
        {
            var granted = string.Join(", ", _grantedPermissions);
            var resolved = string.Join(", ", _resolvedPermissions);
            return $"Mod '{_modId}' - Kernel: {_isKernel}, Granted: [{granted}], Resolved: [{resolved}]";
        }
    }
}
