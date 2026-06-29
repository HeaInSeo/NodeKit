using System;
using System.IO;
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

        private readonly string _workDir = Path.Combine(Path.GetTempPath(), "nodekit-recipe-create-tests-" + Guid.NewGuid());

        public RecipeCreateCommandTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
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
            var outPath = Path.Combine(_workDir, "recipe.json");
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
            var outPath = Path.Combine(_workDir, "recipe.json");
            var args = new[]
            {
                "--non-interactive", "--method", "package",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", $"ImageRef={ImageRefWithDigest}",
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
            var outPath = Path.Combine(_workDir, "recipe.json");
            var exitCode = RunCreate(PackageArgs(engine: null), outPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"PackageEngine\": \"conda\"", File.ReadAllText(outPath));
        }

        [Fact]
        public void Container_ImageDigestMissing_FailsAsRequired()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var stderr = new StringWriter();
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
            var outPath = Path.Combine(_workDir, "recipe.json");
            var stderr = new StringWriter();
            var exitCode = RunCreate(SourceArgs(includeBuildDependencies: false), outPath, stderr: stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("BuildDependencies", stderr.ToString());
            Assert.True(File.Exists(outPath));
        }

        [Fact]
        public void Source_SourceChecksumMissing_FailsAsRequired()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var stderr = new StringWriter();
            var exitCode = RunCreate(RemoveField(SourceArgs(includeBuildDependencies: true), "SourceChecksum"), outPath, stderr: stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("SourceChecksum", stderr.ToString());
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void Dockerfile_BuildContextMissing_DefaultsToCurrentDirectory()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var exitCode = RunCreate(DockerfileArgs(), outPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"BuildContext\": \".\"", File.ReadAllText(outPath));
        }

        [Fact]
        public void Mirror_MirrorKindMissing_ContinuesWithoutWarning()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var stderr = new StringWriter();
            var exitCode = RunCreate(MirrorArgs(), outPath, stderr: stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
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
                    "--field", $"ImageRef={ImageRefWithDigest}",
                    "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
                    "--field", "Channels=bioconda",
                }
                : new[]
                {
                    "--non-interactive", "--method", "package", "--engine", engine,
                    "--field", "ToolName=bwa-mem",
                    "--field", "ToolVersion=0.7.17",
                    "--field", "Script=run.sh",
                    "--field", $"ImageRef={ImageRefWithDigest}",
                    "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
                    "--field", "Channels=bioconda",
                };

        private static string[] DockerfileArgs() => new[]
        {
            "--non-interactive", "--method", "dockerfile", "--accept-dockerfile-warning",
            "--field", "ToolName=bwa-mem",
            "--field", "ToolVersion=0.7.17",
            "--field", "Script=run.sh",
            "--field", $"ImageRef={ImageRefWithDigest}",
            "--field", $"DockerfileContent=FROM {ImageRefWithDigest}\nRUN echo ok\n",
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
                "--field", $"ImageRef={ImageRefWithDigest}",
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
            "--field", $"ImageRef={ImageRefWithDigest}",
            "--field", "MirrorUri=https://mirror.internal/conda-channel",
            "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
        };

        private int Run(params string[] options)
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            return RunCreate(options, outPath);
        }

        private int RunCreate(string[] options, string? outPath = null, StringWriter? stderr = null)
        {
            outPath ??= Path.Combine(_workDir, "recipe.json");
            var fullArgs = new System.Collections.Generic.List<string> { "recipe", "create", outPath };
            fullArgs.AddRange(options);

            var stdout = new StringWriter();
            stderr ??= new StringWriter();
            return CliApp.Run(fullArgs.ToArray(), stdout, stderr);
        }
    }
}
