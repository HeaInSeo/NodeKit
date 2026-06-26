using System;
using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Parses a user-supplied install command string into a structured result
    /// used by BeginnerGuideFlow to pre-populate Package/Mirror recipe fields.
    /// Supports conda and micromamba install commands only.
    /// See docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Section 9.2/9.3.
    ///
    /// Design decisions (see Section 9.2 for full contract):
    /// - mamba is not supported (not conda/micromamba) → Failed; user should
    ///   re-enter as "conda install" or "micromamba install".
    /// - "conda install" without -c flag → PartiallyParsed, Missing=[Channels];
    ///   the implicit "defaults" channel is not assumed for reproducibility.
    /// - "conda create" → PartiallyParsed with a semantic warning.
    /// - Wrapped commands (/bin/bash -c "...") → Failed.
    /// </summary>
    internal static class InstallCommandParser
    {
        private static readonly HashSet<string> _supportedEngines =
            new(StringComparer.Ordinal) { "conda", "micromamba" };

        private static readonly HashSet<string> _ignoredFlags =
            new(StringComparer.Ordinal)
            {
                "-y", "--yes", "-q", "--quiet", "-v", "--verbose",
                "--no-deps", "--no-update-deps", "--override-channels",
                "--no-channel-priority", "--strict-channel-priority",
                "--repodata-fn", "--experimental",
            };

        private static readonly HashSet<string> _valueFlags =
            new(StringComparer.Ordinal) { "-n", "--name", "-p", "--prefix" };

        public static InstallCommandParseResult Parse(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return Failed(command, "명령이 비어 있습니다.");
            }

            var tokens = command.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return Failed(command, "명령이 비어 있습니다.");
            }

            var engine = tokens[0];
            if (!_supportedEngines.Contains(engine))
            {
                return Failed(command, $"지원하지 않는 명령입니다: {engine}. conda 또는 micromamba install 명령을 사용하세요.");
            }

            if (tokens.Length < 2)
            {
                return Failed(command, $"{engine} 명령에 subcommand가 없습니다.");
            }

            var subcommand = tokens[1];
            bool isCreate;
            switch (subcommand)
            {
                case "install":
                    isCreate = false;
                    break;
                case "create":
                    isCreate = true;
                    break;
                default:
                    return Failed(command, $"{engine} {subcommand}는 recipe 생성에 지원하지 않는 subcommand입니다. conda install 또는 micromamba install을 사용하세요.");
            }

            var channels = new List<string>();
            var packages = new List<string>();
            var warnings = new List<string>();

            var i = 2;
            while (i < tokens.Length)
            {
                var token = tokens[i];

                if (token == "-c" || token == "--channel")
                {
                    i++;
                    if (i < tokens.Length)
                    {
                        channels.Add(tokens[i]);
                    }

                    i++;
                    continue;
                }

                if (token.StartsWith("--channel=", StringComparison.Ordinal))
                {
                    channels.Add(token["--channel=".Length..]);
                    i++;
                    continue;
                }

                if (_valueFlags.Contains(token))
                {
                    i += 2;
                    continue;
                }

                if (token.StartsWith("-n=", StringComparison.Ordinal) ||
                    token.StartsWith("--name=", StringComparison.Ordinal) ||
                    token.StartsWith("-p=", StringComparison.Ordinal) ||
                    token.StartsWith("--prefix=", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                if (_ignoredFlags.Contains(token))
                {
                    i++;
                    continue;
                }

                if (token.StartsWith('-'))
                {
                    i++;
                    continue;
                }

                packages.Add(token);
                ClassifyPackageSpec(token, warnings);
                i++;
            }

            if (isCreate)
            {
                warnings.Insert(0, $"{engine} create는 환경 생성 명령입니다. recipe에는 {engine} install 명령을 권장합니다.");
                return new InstallCommandParseResult(
                    InstallCommandParseStatus.PartiallyParsed,
                    PackageEngine: engine,
                    Channels: channels,
                    Packages: packages,
                    Missing: Array.Empty<string>(),
                    Warnings: warnings,
                    OriginalCommand: command);
            }

            return BuildResult(engine, channels, packages, warnings, command);
        }

        private static void ClassifyPackageSpec(string spec, List<string> warnings)
        {
            var parts = spec.Split('=');
            if (parts.Length == 1)
            {
                warnings.Add($"{spec}: 버전이 고정되어 있지 않습니다. validate에서 실패할 수 있습니다.");
            }
            else if (parts.Length == 2 && !string.IsNullOrEmpty(parts[1]))
            {
                warnings.Add($"{spec}: build string이 고정되어 있지 않습니다.");
            }
        }

        private static InstallCommandParseResult BuildResult(
            string engine,
            List<string> channels,
            List<string> packages,
            List<string> warnings,
            string command)
        {
            var missing = new List<string>();

            if (packages.Count == 0)
            {
                missing.Add("Packages");
            }

            if (channels.Count == 0)
            {
                missing.Add("Channels");
            }

            var status = missing.Count > 0
                ? InstallCommandParseStatus.PartiallyParsed
                : InstallCommandParseStatus.Parsed;

            return new InstallCommandParseResult(
                status,
                PackageEngine: engine,
                Channels: channels,
                Packages: packages,
                Missing: missing,
                Warnings: warnings,
                OriginalCommand: command);
        }

        private static InstallCommandParseResult Failed(string command, string reason) =>
            new(
                InstallCommandParseStatus.Failed,
                PackageEngine: null,
                Channels: Array.Empty<string>(),
                Packages: Array.Empty<string>(),
                Missing: Array.Empty<string>(),
                Warnings: new[] { reason },
                OriginalCommand: string.IsNullOrWhiteSpace(command) ? null : command);
    }
}
