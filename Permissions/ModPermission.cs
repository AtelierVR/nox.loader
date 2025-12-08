using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nox.ModLoader.Permissions
{
    /// <summary>
    /// Defines a mod permission with associated security patterns.
    /// </summary>
    public class ModPermission
    {
        /// <summary>
        /// Permission identifier (e.g., "io.read", "socket", "reflection").
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Human-readable name for display.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Description of what this permission allows.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Risk level of this permission.
        /// </summary>
        public PermissionRisk Risk { get; }

        /// <summary>
        /// Type patterns that are allowed when this permission is granted.
        /// </summary>
        public IReadOnlyList<Regex> AllowedTypePatterns { get; }

        /// <summary>
        /// Namespace patterns that are allowed when this permission is granted.
        /// </summary>
        public IReadOnlyList<Regex> AllowedNamespacePatterns { get; }

        /// <summary>
        /// Assembly patterns that are allowed when this permission is granted.
        /// </summary>
        public IReadOnlyList<Regex> AllowedAssemblyPatterns { get; }

        /// <summary>
        /// Parent permission ID (for hierarchical permissions like "io" -> "io.read").
        /// </summary>
        public string ParentId { get; }

        public ModPermission(
            string id,
            string displayName,
            string description,
            PermissionRisk risk,
            IEnumerable<string> allowedTypePatterns = null,
            IEnumerable<string> allowedNamespacePatterns = null,
            IEnumerable<string> allowedAssemblyPatterns = null,
            string parentId = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? id;
            Description = description ?? "";
            Risk = risk;
            ParentId = parentId;

            AllowedTypePatterns = (allowedTypePatterns ?? Enumerable.Empty<string>())
                .Select(p => new Regex(p, RegexOptions.Compiled))
                .ToList();

            AllowedNamespacePatterns = (allowedNamespacePatterns ?? Enumerable.Empty<string>())
                .Select(p => new Regex(p, RegexOptions.Compiled))
                .ToList();

            AllowedAssemblyPatterns = (allowedAssemblyPatterns ?? Enumerable.Empty<string>())
                .Select(p => new Regex(p, RegexOptions.Compiled))
                .ToList();
        }

        /// <summary>
        /// Checks if this permission allows the specified type.
        /// </summary>
        public bool AllowsType(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName)) return false;
            return AllowedTypePatterns.Any(p => p.IsMatch(typeFullName));
        }

        /// <summary>
        /// Checks if this permission allows the specified namespace.
        /// </summary>
        public bool AllowsNamespace(string namespaceName)
        {
            if (string.IsNullOrEmpty(namespaceName)) return false;
            return AllowedNamespacePatterns.Any(p => p.IsMatch(namespaceName));
        }

        /// <summary>
        /// Checks if this permission allows the specified assembly.
        /// </summary>
        public bool AllowsAssembly(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return false;
            return AllowedAssemblyPatterns.Any(p => p.IsMatch(assemblyName));
        }
    }

    /// <summary>
    /// Risk level associated with a permission.
    /// </summary>
    public enum PermissionRisk
    {
        /// <summary>Safe permission with minimal risk.</summary>
        Low,
        /// <summary>Medium risk, user should be aware.</summary>
        Medium,
        /// <summary>High risk, can affect system security.</summary>
        High,
        /// <summary>Critical risk, can cause system damage.</summary>
        Critical
    }
}
