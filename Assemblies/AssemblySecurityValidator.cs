#if !ENABLE_IL2CPP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Nox.ModLoader.Permissions;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.ModLoader.Assemblies
{
    /// <summary>
    /// Validates mod assemblies before loading to ensure they don't reference blacklisted types.
    /// Uses Mono.Cecil to scan IL code without loading the assembly into the AppDomain.
    /// </summary>
    public static class AssemblySecurityValidator
    {
        #region Blacklist Patterns

        private static readonly List<Regex> TypeBlacklistPatterns = new()
        {
            // Process execution
            new(@"^System\.Diagnostics\.Process(StartInfo)?$", RegexOptions.Compiled),

            // Raw networking
            new(@"^System\.Net\.Sockets\.(Socket|TcpClient|TcpListener|UdpClient)$", RegexOptions.Compiled),

            // Dynamic code generation
            new(@"^System\.Reflection\.Emit\..+$", RegexOptions.Compiled),
            new(@"^System\.CodeDom\.Compiler\..+$", RegexOptions.Compiled),
            new(@"^Microsoft\.CSharp\.CSharpCodeProvider$", RegexOptions.Compiled),
            new(@"^Microsoft\.VisualBasic\.VBCodeProvider$", RegexOptions.Compiled),

            // Security manipulation
            new(@"^System\.Security\.SecurityManager$", RegexOptions.Compiled),
            new(@"^System\.Security\.Policy\..+$", RegexOptions.Compiled),
            new(@"^System\.Security\.Principal\.Windows(Identity|ImpersonationContext)$", RegexOptions.Compiled),

            // Windows Registry
            new(@"^Microsoft\.Win32\.Registry(Key)?$", RegexOptions.Compiled),

            // Native interop
            new(@"^System\.Runtime\.InteropServices\.(Marshal|DllImportAttribute)$", RegexOptions.Compiled),

            // Unity Editor (runtime only)
            new(@"^UnityEditor\..+$", RegexOptions.Compiled),

            // System access
            new(@"^System\.Environment$", RegexOptions.Compiled),
            new(@"^System\.AppDomain$", RegexOptions.Compiled),
        };

        private static readonly List<Regex> NamespaceBlacklistPatterns = new()
        {
            new(@"^System\.Reflection\.Emit$", RegexOptions.Compiled),
            new(@"^System\.CodeDom\.Compiler$", RegexOptions.Compiled),
            new(@"^Microsoft\.CSharp$", RegexOptions.Compiled),
            new(@"^Microsoft\.Win32$", RegexOptions.Compiled),
            new(@"^UnityEditor$", RegexOptions.Compiled),
        };

        private static readonly object BlacklistLock = new();

        #endregion

        #region Result Classes

        public class SecurityViolation
        {
            public string ViolationType { get; set; }
            public string TypeName { get; set; }
            public string MemberName { get; set; }
            public string Location { get; set; }
            public string MatchedPattern { get; set; }
            public string RequiredPermission { get; set; }
        }

        public class ValidationResult
        {
            public bool IsValid { get; set; } = true;
            public List<SecurityViolation> Violations { get; } = new();
            public string AssemblyPath { get; set; }
            public ModPermissionContext PermissionContext { get; set; }

            public string GetSummary()
            {
                var fileName = Path.GetFileName(AssemblyPath);
                if (IsValid)
                    return $"Assembly '{fileName}' passed security validation.";

                var violations = string.Join("\n", Violations.Select(v =>
                {
                    var permInfo = string.IsNullOrEmpty(v.RequiredPermission) 
                        ? "" 
                        : $" [requires: {v.RequiredPermission}]";
                    return $"  - [{v.ViolationType}] {v.TypeName}.{v.MemberName}{permInfo}";
                }));
                return $"Assembly '{fileName}' FAILED with {Violations.Count} violation(s):\n{violations}";
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Validates an assembly file without loading it into the AppDomain.
        /// Uses default blacklist (no permissions granted).
        /// </summary>
        public static ValidationResult ValidateAssembly(string assemblyPath)
            => ValidateAssembly(assemblyPath, null);

        /// <summary>
        /// Validates an assembly file with permission context.
        /// Types allowed by granted permissions will not be flagged as violations.
        /// </summary>
        public static ValidationResult ValidateAssembly(string assemblyPath, ModPermissionContext permissionContext)
        {
            var result = new ValidationResult 
            { 
                AssemblyPath = assemblyPath,
                PermissionContext = permissionContext
            };

            // Kernel mods bypass all security checks
            if (permissionContext?.IsKernel == true)
            {
                Logger.LogDebug($"[SecurityValidator] Kernel mod '{permissionContext.ModId}' - bypassing security checks");
                return result;
            }

            if (!File.Exists(assemblyPath))
            {
                AddViolation(result, "FileNotFound", assemblyPath, "", assemblyPath, "N/A");
                return result;
            }

            try
            {
                using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
                {
                    ReadSymbols = false,
                    ReadWrite = false
                });

                foreach (var module in assembly.Modules)
                {
                    ScanModuleReferences(module, result, permissionContext);
                    ScanModuleTypes(module, result, permissionContext);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SecurityValidator] Error reading assembly '{assemblyPath}': {ex.Message}");
                AddViolation(result, "ReadError", ex.GetType().Name, ex.Message, assemblyPath, "N/A");
            }

            return result;
        }

        /// <summary>
        /// Validates all DLL files in a directory with permission context.
        /// </summary>
        public static List<ValidationResult> ValidateDirectory(string directoryPath, ModPermissionContext permissionContext = null)
        {
            if (!Directory.Exists(directoryPath))
                return new List<ValidationResult>();

            return Directory.GetFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly)
                .Select(f => ValidateAssembly(f, permissionContext))
                .ToList();
        }

        /// <summary>
        /// Checks if a type full name is blacklisted (considering permissions).
        /// </summary>
        public static bool IsTypeBlacklisted(string typeFullName, out string matchedPattern, ModPermissionContext permissionContext = null)
        {
            matchedPattern = null;
            if (string.IsNullOrEmpty(typeFullName)) return false;

            // Check if allowed by permissions
            if (permissionContext?.IsTypeAllowed(typeFullName) == true)
                return false;

            lock (BlacklistLock)
            {
                foreach (var regex in TypeBlacklistPatterns)
                {
                    if (!regex.IsMatch(typeFullName)) continue;
                    matchedPattern = regex.ToString();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if a type full name is blacklisted (legacy overload).
        /// </summary>
        public static bool IsTypeBlacklisted(string typeFullName, out string matchedPattern)
            => IsTypeBlacklisted(typeFullName, out matchedPattern, null);

        /// <summary>
        /// Checks if a namespace is blacklisted (considering permissions).
        /// </summary>
        public static bool IsNamespaceBlacklisted(string namespaceName, out string matchedPattern, ModPermissionContext permissionContext = null)
        {
            matchedPattern = null;
            if (string.IsNullOrEmpty(namespaceName)) return false;

            // Check if allowed by permissions
            if (permissionContext?.IsNamespaceAllowed(namespaceName) == true)
                return false;

            lock (BlacklistLock)
            {
                foreach (var regex in NamespaceBlacklistPatterns)
                {
                    if (!regex.IsMatch(namespaceName)) continue;
                    matchedPattern = regex.ToString();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if a namespace is blacklisted (legacy overload).
        /// </summary>
        public static bool IsNamespaceBlacklisted(string namespaceName, out string matchedPattern)
            => IsNamespaceBlacklisted(namespaceName, out matchedPattern, null);

        /// <summary>
        /// Adds a type pattern to the blacklist.
        /// </summary>
        public static void AddTypeToBlacklist(string pattern)
            => AddPatternToList(pattern, TypeBlacklistPatterns, "type");

        /// <summary>
        /// Adds a namespace pattern to the blacklist.
        /// </summary>
        public static void AddNamespaceToBlacklist(string pattern)
            => AddPatternToList(pattern, NamespaceBlacklistPatterns, "namespace");

        public static IReadOnlyList<string> GetTypeBlacklistPatterns()
        {
            lock (BlacklistLock)
            {
                return TypeBlacklistPatterns.Select(r => r.ToString()).ToList();
            }
        }

        public static IReadOnlyList<string> GetNamespaceBlacklistPatterns()
        {
            lock (BlacklistLock)
            {
                return NamespaceBlacklistPatterns.Select(r => r.ToString()).ToList();
            }
        }

        #endregion

        #region Private Scanning Methods

        private static void ScanModuleReferences(ModuleDefinition module, ValidationResult result, ModPermissionContext permissionContext)
        {
            foreach (var assemblyRef in module.AssemblyReferences)
            {
                // Check if allowed by permission
                if (permissionContext?.IsAssemblyAllowed(assemblyRef.Name) == true)
                    continue;

                if (!ModAssemblyLoadContext.IsBlacklisted(assemblyRef.Name)) continue;
                AddViolation(result, "BlacklistedAssemblyReference", assemblyRef.Name, "", module.Name,
                    ModAssemblyLoadContext.GetMatchingPattern(assemblyRef.Name));
            }

            foreach (var typeRef in module.GetTypeReferences())
                CheckTypeReference(typeRef, module.Name, result, permissionContext);
        }

        private static void ScanModuleTypes(ModuleDefinition module, ValidationResult result, ModPermissionContext permissionContext)
        {
            foreach (var type in module.Types)
                ScanType(type, result, permissionContext);
        }

        private static void ScanType(TypeDefinition type, ValidationResult result, ModPermissionContext permissionContext)
        {
            CheckType(type.BaseType?.FullName, "BlacklistedBaseType", type.FullName, type.FullName, result, permissionContext);

            foreach (var iface in type.Interfaces)
                CheckType(iface.InterfaceType.FullName, "BlacklistedInterface", type.FullName, type.FullName, result, permissionContext);

            foreach (var field in type.Fields)
                CheckType(field.FieldType.FullName, "BlacklistedFieldType", field.Name, type.FullName, result, permissionContext);

            foreach (var method in type.Methods)
                ScanMethod(method, type.FullName, result, permissionContext);

            foreach (var nested in type.NestedTypes)
                ScanType(nested, result, permissionContext);
        }

        private static void ScanMethod(MethodDefinition method, string containingType, ValidationResult result, ModPermissionContext permissionContext)
        {
            CheckType(method.ReturnType?.FullName, "BlacklistedReturnType", method.Name, containingType, result, permissionContext);

            foreach (var param in method.Parameters)
                CheckType(param.ParameterType.FullName, "BlacklistedParameterType",
                    $"{method.Name}({param.Name})", containingType, result, permissionContext);

            if (!method.HasBody) return;

            var location = $"{containingType}.{method.Name}";
            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case MethodReference methodRef:
                        CheckType(methodRef.DeclaringType?.FullName, "BlacklistedMethodCall",
                            methodRef.Name, location, result, permissionContext);
                        break;
                    case TypeReference typeRef:
                        CheckType(typeRef.FullName, "BlacklistedTypeUsage",
                            instruction.OpCode.ToString(), location, result, permissionContext);
                        break;
                    case FieldReference fieldRef:
                        CheckType(fieldRef.DeclaringType?.FullName, "BlacklistedFieldAccess",
                            fieldRef.Name, location, result, permissionContext);
                        break;
                }
            }
        }

        #endregion

        #region Helper Methods

        private static void CheckTypeReference(TypeReference typeRef, string location, ValidationResult result, ModPermissionContext permissionContext)
        {
            if (IsTypeBlacklisted(typeRef.FullName, out var typePattern, permissionContext))
            {
                var requiredPerm = ModPermissionContext.FindRequiredPermissionForType(typeRef.FullName);
                AddViolation(result, "BlacklistedTypeReference", typeRef.FullName, "", location, typePattern, requiredPerm?.Id);
            }

            if (!string.IsNullOrEmpty(typeRef.Namespace) &&
                IsNamespaceBlacklisted(typeRef.Namespace, out var nsPattern, permissionContext))
            {
                AddViolation(result, "BlacklistedNamespace", typeRef.Namespace, typeRef.FullName, location, nsPattern);
            }
        }

        private static void CheckType(string typeFullName, string violationType, string memberName,
            string location, ValidationResult result, ModPermissionContext permissionContext)
        {
            if (string.IsNullOrEmpty(typeFullName)) return;
            if (!IsTypeBlacklisted(typeFullName, out var pattern, permissionContext)) return;
            
            var requiredPerm = ModPermissionContext.FindRequiredPermissionForType(typeFullName);
            AddViolation(result, violationType, typeFullName, memberName, location, pattern, requiredPerm?.Id);
        }

        private static void AddViolation(ValidationResult result, string violationType, string typeName,
            string memberName, string location, string matchedPattern, string requiredPermission = null)
        {
            result.IsValid = false;
            result.Violations.Add(new SecurityViolation
            {
                ViolationType = violationType,
                TypeName = typeName,
                MemberName = memberName,
                Location = location,
                MatchedPattern = matchedPattern,
                RequiredPermission = requiredPermission
            });
        }

        private static void AddPatternToList(string pattern, List<Regex> list, string patternType)
        {
            if (string.IsNullOrEmpty(pattern)) return;

            lock (BlacklistLock)
            {
                try
                {
                    list.Add(new Regex(pattern, RegexOptions.Compiled));
                    Logger.LogDebug($"[SecurityValidator] Added {patternType} blacklist pattern: {pattern}");
                }
                catch (ArgumentException ex)
                {
                    Logger.LogError($"[SecurityValidator] Invalid regex pattern '{pattern}': {ex.Message}");
                }
            }
        }

        #endregion
    }
}
#endif
