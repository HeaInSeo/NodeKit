using System;
using System.IO;
using System.Linq;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// nodekit recipe create --non-interactive: CLI option validation
    /// (design doc Section 31.5) and per-tier RecipeFieldRequirement
    /// handling (design doc Section 31.2).
    /// </summary>
    public class RecipeCreateCommandTests : IDisposable
    {
        private const string ImageRefWithDigest =
            "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string DigestOnly =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-recipe-create-tests-" + Guid.NewGuid());
        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public RecipeCreateCommandTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        // --- 31.5 CLI option tests ---

        [Fact]
        public void Engine_WithPackageMethod_IsAccepted()
        {
            var exitCode = RunCreate(PackageArgs(engine: "micromamba"));

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void Engine_WithContainerMethod_IsRejected()
        {
            var exitCode = Run("--non-interactive", "--method", "container", "--engine", "micromamba");

            Assert.Equal(2, exitCode);
        }

        [Fact]
        public void Engine_WithSourceMethod_IsRejected()
        {
            var exitCode = Run("--non-interactive", "--method", "source", "--engine", "conda");

            Assert.Equal(2, exitCode);
        }

        [Fact]
        public void Method_InternalBuildKindNameConda_IsRejected()
        {
            var exitCode = Run("--non-interactive", "--method", "conda");

            Assert.Equal(2, exitCode);
        }

        [Fact]
        public void Method_InternalBuildKindNameMicromamba_IsRejected()
        {
            var exitCode = Run("--non-interactive", "--method", "micromamba");

            Assert.Equal(2, exitCode);
        }

        [Fact]
        public void Method_ValueLooksLikeAnotherOption_ReturnsTwoWithExplicitError()
        {
            // Regression test (external review): --method's next token being another
            // flag (typo'd or a genuine mistake, e.g. forgetting the value) used to be
            // silently accepted as the method's literal value -- "알 수 없는
            // method입니다: --non-interactive" was at least a somewhat legible error,
            // but the real bug was the side effect: TryTakeNext's ref i increment
            // consumed the "--non-interactive" token as a value, so the outer loop
            // never saw it as a flag and --non-interactive was silently never set.
            using var stderr = new StringWriter();

            var exitCode = RunCreate(
                new[] { "--method", "--non-interactive" }, stderr: stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--method 옵션에는 값이 필요합니다", stderr.ToString());
        }

        [Fact]
        public void Dockerfile_WithAcceptWarningAndNonInteractive_IsAccepted()
        {
            var exitCode = RunCreate(DockerfileArgs());

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void AcceptDockerfileWarning_WithNonDockerfileMethod_IsRejected()
        {
            var exitCode = Run("--non-interactive", "--method", "package", "--accept-dockerfile-warning");

            Assert.Equal(2, exitCode);
        }

        [Fact]
        public void UnknownOption_AcceptImageTagWarning_IsRejected()
        {
            var exitCode = Run("--non-interactive", "--method", "package", "--accept-image-tag-warning");

            Assert.Equal(2, exitCode);
        }

        // --- Section 23: --field parsing contract regression tests ---

        [Fact]
        public void Field_EmbeddedEquals_PreservedAsValue_NotSplitAgain()
        {
            // TrySplitOnce uses IndexOf('='), so "Packages=bwa=0.7.17=h5bf99c6_8"
            // splits into Name="Packages", Value="bwa=0.7.17=h5bf99c6_8".
            var outPath = Path.Join(_workDir, "recipe.json");
            var exitCode = RunCreate(PackageArgs(engine: null), outPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", File.ReadAllText(outPath));
        }

        [Fact]
        public void Field_StringList_RepeatedField_AccumulatesAllValues()
        {
            // Repeated --field for StringList fields (Packages, Channels,
            // SourceBuildCommands, BuildDependencies, Command) accumulates;
            // the last value does not overwrite earlier ones.
            var outPath = Path.Join(_workDir, "recipe.json");
            var args = new[]
            {
                "--non-interactive", "--method", "package",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", $"BaseImage={ImageRefWithDigest}",
                "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
                "--field", "Packages=samtools=1.18=h50ea8bc_1",
                "--field", "Channels=bioconda",
                "--field", "Channels=conda-forge",
            };
            var exitCode = RunCreate(args, outPath);

            Assert.Equal(0, exitCode);
            var json = File.ReadAllText(outPath);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", json);
            Assert.Contains("samtools=1.18=h50ea8bc_1", json);
            Assert.Contains("bioconda", json);
            Assert.Contains("conda-forge", json);
        }

        // --- 31.2 non-interactive requirement tier tests ---

        [Fact]
        public void Package_PackageEngineMissing_DefaultsToConda()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var exitCode = RunCreate(PackageArgs(engine: null), outPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"PackageEngine\": \"conda\"", File.ReadAllText(outPath));
        }

        [Fact]
        public void Container_ImageDigestMissing_FailsAsRequired()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stderr = new StringWriter();
            var exitCode = RunCreate(
                new[]
                {
                    "--non-interactive", "--method", "container",
                    "--field", "ToolName=bwa-mem",
                    "--field", "ToolVersion=0.7.17",
                    "--field", "Script=run.sh",
                    "--field", "ImageRef=condaforge/miniforge3:24.3.0-0",
                },
                outPath,
                stderr: stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("ImageDigest", stderr.ToString());
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void Source_BuildDependenciesMissing_WarnsAndContinues()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stderr = new StringWriter();
            var exitCode = RunCreate(SourceArgs(includeBuildDependencies: false), outPath, stderr: stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("BuildDependencies", stderr.ToString());
            Assert.True(File.Exists(outPath));
        }

        [Fact]
        public void Source_BuildDependenciesPresent_WarnsTheyAreNotInstalled()
        {
            // §13 R21: BuildDependencies exists on the recipe surface but
            // RecipeRenderer never actually installs it — this only makes
            // that limitation visible, it doesn't add install logic.
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stderr = new StringWriter();
            var exitCode = RunCreate(SourceArgs(includeBuildDependencies: true), outPath, stderr: stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("BuildDependencies는 현재 자동으로 설치되지 않습니다", stderr.ToString());
            Assert.True(File.Exists(outPath));
        }

        // §13 R22-B (docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md).
        // Wizard-integrated as option 6 since Issue #42 (2026-07-14) — these
        // non-interactive tests still cover the CLI-level contract directly.

        [Fact]
        public void SourceStructured_CuratedProfiles_CreatesRecipeThatPassesValidate()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var exitCode = RunCreate(SourceStructuredArgs(), outPath);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));

            using var validateStdout = new StringWriter();
            using var validateStderr = new StringWriter();
            var validateExitCode = CliApp.Run(new[] { "validate", outPath }, validateStdout, validateStderr);

            Assert.Equal(0, validateExitCode);
            Assert.Contains("OK", validateStdout.ToString());
        }

        [Fact]
        public void SourceStructured_AdvancedRuntimeProfileWithFetchOnlyImage_WarnsButStillSaves()
        {
            // §13 R22-D (Issue #39): RuntimeProfileHygieneAdvisor is
            // non-blocking — a risky-looking RuntimeProfileImage gets a
            // warning on stderr, not a rejection.
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stderr = new StringWriter();
            var args = RemoveField(SourceStructuredArgs(), "RuntimeProfile")
                .Concat(new[]
                {
                    "--field", "RuntimeProfile=advanced",
                    "--field",
                    "RuntimeProfileImage=docker.io/library/buildpack-deps:bookworm@sha256:4efddd9a54ddc095e672b2fdf514f1ee4d3bb6e1f6ffc988b022c75e6ea99383",
                })
                .ToArray();

            var exitCode = RunCreate(args, outPath, stderr: stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("추정", stderr.ToString());
            Assert.True(File.Exists(outPath));
        }

        [Fact]
        public void SourceStructured_UnknownMethodName_StillRejected()
        {
            // "source-build-structured" is the *internal* RecipeBuildKind
            // name, not the CLI --method value ("source-structured") —
            // confirms the friendly-error guard covers the new kind too.
            var exitCode = Run("--non-interactive", "--method", "source-build-structured");

            Assert.Equal(2, exitCode);
        }

        private static string[] SourceStructuredArgs() => new[]
        {
            "--non-interactive", "--method", "source-structured",
            "--field", "ToolName=bwa-mem",
            "--field", "ToolVersion=0.7.17",
            "--field", "Script=run.sh",
            "--field", "BuildProfile=generic",
            "--field", "SourceUri=https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
            "--field", $"SourceChecksum={DigestOnly}",
            "--field", "SourceBuildCommands=make",
            "--field", "RuntimeProfile=minimal",
        };

        [Fact]
        public void Source_SourceChecksumMissing_FailsAsRequired()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stderr = new StringWriter();
            var exitCode = RunCreate(RemoveField(SourceArgs(includeBuildDependencies: true), "SourceChecksum"), outPath, stderr: stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("SourceChecksum", stderr.ToString());
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void Dockerfile_BuildContextMissing_DefaultsToCurrentDirectory()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var exitCode = RunCreate(DockerfileArgs(), outPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"BuildContext\": \".\"", File.ReadAllText(outPath));
        }

        [Fact]
        public void Mirror_MirrorKindMissing_ContinuesWithoutWarning()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stderr = new StringWriter();
            var exitCode = RunCreate(MirrorArgs(), outPath, stderr: stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
        }

        // --- #15/#16 후속: non-interactive free-text BaseImage/engine 불일치 경고 ---

        [Fact]
        public void Package_MicromambaBaseImage_EngineFlagOmitted_WarnsButStillSaves()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stderr = new StringWriter();
            var args = new[]
            {
                "--non-interactive", "--method", "package",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", "BaseImage=mambaorg/micromamba:1.5.8@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
                "--field", "Channels=bioconda",
            };

            var exitCode = RunCreate(args, outPath, stderr: stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("micromamba 전용 이미지로 보이는데 PackageEngine은 conda", stderr.ToString());
        }

        // §13 R20: SourceBuild's BaseImage doubles as both fetch and final
        // runtime image, so a fetch-only-looking image (curlimages/curl —
        // confirmed as a live-test workaround, docs §13) gets a warning.

        [Fact]
        public void Source_CurlBaseImage_WarnsButStillSaves()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stderr = new StringWriter();
            var args = new[]
            {
                "--non-interactive", "--method", "source",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", "BaseImage=docker.io/curlimages/curl:8.8.0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "--field", "SourceUri=https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                "--field", $"SourceChecksum={DigestOnly}",
                "--field", "SourceBuildCommands=make",
            };

            var exitCode = RunCreate(args, outPath, stderr: stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("fetch 전용 이미지로 보입니다", stderr.ToString());
        }

        [Fact]
        public void Mirror_MicromambaBaseImage_WarnsButStillSaves()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stderr = new StringWriter();
            var args = new[]
            {
                "--non-interactive", "--method", "mirror",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", "BaseImage=mambaorg/micromamba:1.5.8@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "--field", "MirrorUri=https://mirror.internal/conda-channel",
                "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
            };

            var exitCode = RunCreate(args, outPath, stderr: stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("mirror 방식은 항상 conda로 렌더링됩니다", stderr.ToString());
        }

        private static string[] RemoveField(string[] args, string fieldName)
        {
            var result = new System.Collections.Generic.List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--field" && args[i + 1].StartsWith(fieldName + "=", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                result.Add(args[i]);
            }

            return result.ToArray();
        }

        private static string[] PackageArgs(string? engine) =>
            engine is null
                ? new[]
                {
                    "--non-interactive", "--method", "package",
                    "--field", "ToolName=bwa-mem",
                    "--field", "ToolVersion=0.7.17",
                    "--field", "Script=run.sh",
                    "--field", $"BaseImage={ImageRefWithDigest}",
                    "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
                    "--field", "Channels=bioconda",
                }
                : new[]
                {
                    "--non-interactive", "--method", "package", "--engine", engine,
                    "--field", "ToolName=bwa-mem",
                    "--field", "ToolVersion=0.7.17",
                    "--field", "Script=run.sh",
                    "--field", $"BaseImage={ImageRefWithDigest}",
                    "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
                    "--field", "Channels=bioconda",
                };

        private static string[] DockerfileArgs() => new[]
        {
            "--non-interactive", "--method", "dockerfile", "--accept-dockerfile-warning",
            "--field", "ToolName=bwa-mem",
            "--field", "ToolVersion=0.7.17",
            "--field", "Script=run.sh",
            "--field", $"BaseImage={ImageRefWithDigest}",
            "--field", $"DockerfileContent=FROM {ImageRefWithDigest}\nRUN echo ok\nUSER 1000\n",
            "--field", "DockerfilePath=./Dockerfile",
        };

        private static string[] SourceArgs(bool includeBuildDependencies)
        {
            var args = new System.Collections.Generic.List<string>
            {
                "--non-interactive", "--method", "source",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", $"BaseImage={ImageRefWithDigest}",
                "--field", "SourceUri=https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                "--field", $"SourceChecksum={DigestOnly}",
                "--field", "SourceBuildCommands=make",
                "--field", "SourceBuildCommands=make install",
            };

            if (includeBuildDependencies)
            {
                args.Add("--field");
                args.Add("BuildDependencies=zlib1g-dev");
            }

            return args.ToArray();
        }

        private static string[] MirrorArgs() => new[]
        {
            "--non-interactive", "--method", "mirror",
            "--field", "ToolName=bwa-mem",
            "--field", "ToolVersion=0.7.17",
            "--field", "Script=run.sh",
            "--field", $"BaseImage={ImageRefWithDigest}",
            "--field", "MirrorUri=https://mirror.internal/conda-channel",
            "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
        };

        private int Run(params string[] options)
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            return RunCreate(options, outPath);
        }

        private int RunCreate(string[] options, string? outPath = null, StringWriter? stderr = null)
        {
            outPath ??= Path.Join(_workDir, "recipe.json");
            var fullArgs = new System.Collections.Generic.List<string> { "recipe", "create", outPath };
            fullArgs.AddRange(options);

            using var stdout = new StringWriter();

            // 호출자가 stderr를 넘기면 그쪽이 내용을 검사한 뒤 자신의 using으로 소유·해제한다 —
            // 여기서 만든 경우(ownedStderr != null)에만 스코프 종료 시 dispose된다.
            using var ownedStderr = stderr is null ? new StringWriter() : null;
            return CliApp.Run(fullArgs.ToArray(), stdout, stderr ?? ownedStderr!);
        }
    }
}
