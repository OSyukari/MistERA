using System;
using System.Collections.Generic;
using System.Linq;

namespace MistEra.Updates
{
    public enum UpdateOperationState
    {
        Queued, Downloading, Verified, Staged, Installing, Installed, Failed, Cancelled
    }

    public static class UpdatePlanning
    {
        // Returns dependency-first download candidates only; never changes enable state.
        // installedModIds is an explicitly trusted installed snapshot, not a remote assertion.
        public static List<ModPackage> Dependencies(ModCatalog catalog, IEnumerable<string> requestedPackageIds,
            long buildNumber, string channel, IEnumerable<string> installedModIds = null)
        {
            UpdateValidation.ValidateCatalog(catalog);
            UpdateValidation.Require(buildNumber > 0 && (channel == "full" || channel == "safe"), "Invalid target build");
            if (requestedPackageIds == null) throw new ArgumentNullException(nameof(requestedPackageIds));
            var packages = catalog.packages.ToDictionary(p => p.id, StringComparer.OrdinalIgnoreCase);
            var mods = catalog.packages.Where(p => p.kind == "mod").ToDictionary(p => p.modId, StringComparer.Ordinal);
            var installed = new HashSet<string>(installedModIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var requested = new HashSet<string>(requestedPackageIds, StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<ModPackage>();
            foreach (string id in requested.OrderBy(s => s, StringComparer.Ordinal))
            {
                UpdateValidation.Require(id != null && packages.ContainsKey(id), "Unknown requested package: " + id);
                Visit(packages[id]);
            }
            return result;

            void Visit(ModPackage package)
            {
                if (visited.Contains(package.id)) return;
                UpdateValidation.Require(visiting.Add(package.id), "Dependency cycle: " + package.id);
                UpdateValidation.Require((package.minimumBuild == 0 || buildNumber >= package.minimumBuild) &&
                    (package.maximumBuild == 0 || buildNumber <= package.maximumBuild) &&
                    (string.IsNullOrEmpty(package.channel) || package.channel == channel), "Incompatible package: " + package.id);
                foreach (string dependency in package.dependencies.OrderBy(s => s, StringComparer.Ordinal))
                {
                    if (installed.Contains(dependency) && (!mods.TryGetValue(dependency, out ModPackage selected) || !requested.Contains(selected.id))) continue;
                    UpdateValidation.Require(mods.TryGetValue(dependency, out ModPackage target), "Missing dependency: " + dependency);
                    Visit(target);
                }
                visiting.Remove(package.id); visited.Add(package.id); result.Add(package);
            }
        }
    }
}
