using System.Linq;

namespace NodeKit.Validation.Recipes
{
    internal enum PackagePinStatus
    {
        FullPin,
        VersionOnly,
        Malformed,
    }

    /// <summary>
    /// Classifies a conda-style package pin string by its "=" count. Only
    /// meaningful for values that already passed RecipeValidator's
    /// name=version[=build] allowlist (L1-RCP-011) — this doesn't re-validate
    /// format, it distinguishes "fully pinned" from "version-only" among
    /// values that are already syntactically valid. See
    /// docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md §13 R19: NodeVault's final gate
    /// rejects version-only pins (name=version) even though NodeKit's L1
    /// allows them during authoring, so NodeKit needs to know which pins are
    /// "weak" to warn about that mismatch before submit.
    /// </summary>
    internal static class PackagePinClassifier
    {
        public static PackagePinStatus Classify(string package)
        {
            if (string.IsNullOrWhiteSpace(package))
            {
                return PackagePinStatus.Malformed;
            }

            var equalsCount = package.Count(c => c == '=');

            return equalsCount switch
            {
                2 => PackagePinStatus.FullPin,
                1 => PackagePinStatus.VersionOnly,
                _ => PackagePinStatus.Malformed,
            };
        }
    }
}
