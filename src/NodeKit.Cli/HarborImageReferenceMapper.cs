using System;
using System.Collections.Generic;

namespace NodeKit.Cli
{
    /// <summary>
    /// Maps a public image reference (as returned by <see cref="BaseImageCatalog"/>)
    /// onto the concrete Harbor pull reference this environment is configured to
    /// use, so <see cref="HarborImageDigestResolver"/> — which only ever parses and
    /// looks up an already-complete "harbor-host/project/repository:tag" — can
    /// resolve it. The mapping is never guessed (e.g. no assumed "dockerhub-proxy"
    /// project name); it always comes from <c>NODEKIT_HARBOR_IMAGE_MAP</c>, since
    /// Harbor project layout differs per deployment.
    /// </summary>
    internal static class HarborImageReferenceMapper
    {
        // Format: comma-separated "origin=harborPrefix" pairs, e.g.
        // "docker.io=harbor.lab.local/dockerhub-proxy". harborPrefix must be a
        // full pull prefix (host + Harbor project), not just a host. origin is
        // the reference's registry host, or "docker.io" for a host-less
        // reference (Docker's own implicit-registry convention).
        private const string EnvVarName = "NODEKIT_HARBOR_IMAGE_MAP";
        private const string DefaultOrigin = "docker.io";

        internal static bool HasAnyMapping() => HasAnyMapping(Environment.GetEnvironmentVariable(EnvVarName));

        internal static bool HasAnyMapping(string? rawMap) => ParseMap(rawMap).Count > 0;

        internal static string? TryMapToHarbor(string publicReference) =>
            TryMapToHarbor(publicReference, Environment.GetEnvironmentVariable(EnvVarName));

        internal static string? TryMapToHarbor(string publicReference, string? rawMap)
        {
            var map = ParseMap(rawMap);
            if (map.Count == 0)
            {
                return null;
            }

            var (origin, repositoryAndTag) = SplitOrigin(publicReference);
            return map.TryGetValue(origin, out var harborPrefix)
                ? $"{harborPrefix}/{repositoryAndTag}"
                : null;
        }

        private static Dictionary<string, string> ParseMap(string? rawMap)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(rawMap))
            {
                return map;
            }

            foreach (var entry in rawMap.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eqIdx = entry.IndexOf('=', StringComparison.Ordinal);
                if (eqIdx <= 0 || eqIdx == entry.Length - 1)
                {
                    continue;
                }

                var origin = entry[..eqIdx].Trim();
                var prefix = entry[(eqIdx + 1)..].Trim().TrimEnd('/');
                if (origin.Length > 0 && prefix.Length > 0)
                {
                    map[origin] = prefix;
                }
            }

            return map;
        }

        // Mirrors HarborImageDigestResolver's own host-detection rule (a first
        // path component containing '.' or ':' is a registry host); a host-less
        // reference defaults to docker.io, matching Docker's own convention.
        private static (string Origin, string RepositoryAndTag) SplitOrigin(string reference)
        {
            var slashIdx = reference.IndexOf('/', StringComparison.Ordinal);
            if (slashIdx > 0)
            {
                var firstComponent = reference[..slashIdx];
                if (firstComponent.Contains('.', StringComparison.Ordinal) || firstComponent.Contains(':', StringComparison.Ordinal))
                {
                    return (firstComponent, reference[(slashIdx + 1)..]);
                }
            }

            return (DefaultOrigin, reference);
        }
    }
}
