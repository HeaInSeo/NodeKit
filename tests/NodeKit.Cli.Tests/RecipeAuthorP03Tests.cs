using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli;
using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P03.author_core / P03.author_source (NodeKit v0.9.1 contract S1-03,
    /// S1-04, S1-05): pin selection must match the saved/rendered Recipe and
    /// pass full L1 after the candidate is applied; source structured is the
    /// default source path; Dockerfile input is Path or Content (one-of), with
    /// Path frozen into Content and no build-context transfer.
    /// </summary>
    public class RecipeAuthorP03Tests : IDisposable
    {
        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string ImageRefWithDigest = "condaforge/miniforge3:24.3.0-0@" + Digest;

        private const string DockerfileText = "FROM " + ImageRefWithDigest + "\nRUN echo ok\nUSER 1000\n";

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p03-author-tests-" + Guid.NewGuid());
        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public RecipeAuthorP03Tests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        // ── S1-03 · install clue and pin selection ────────────────────────────

        [Fact]
        public void S1_03_C01_ParserKeepsEveryEqualsOfTheFullPin()
        {
            var parsed = InstallCommandParser.Parse("conda install -c bioconda bwa=0.7.17=h5bf99c6_8 -y");

            Assert.Equal(InstallCommandParseStatus.Parsed, parsed.Status);
            Assert.Equal("conda", parsed.PackageEngine);
            Assert.Equal(new[] { "bioconda" }, parsed.Channels);
            Assert.Equal(new[] { "bwa=0.7.17=h5bf99c6_8" }, parsed.Packages);
        }

        [Fact]
        public void S1_03_C02_VersionOnlyPin_DefaultValidateIsLocalOkOnly()
        {
            var recipePath = WriteRecipe(PackageRecipe("bwa=0.7.17"));

            var exitCode = RunCli(out var stdout, out var stderr, "validate", recipePath);

            Assert.Equal(0, exitCode);
            Assert.Equal("OK", stdout.Trim());
            Assert.Empty(stderr);
            Assert.DoesNotContain("재현", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("서버", stdout, StringComparison.Ordinal);
        }

        [Fact]
        public void S1_03_C03_VersionOnlyPin_StrictReproducibleIsInvalid()
        {
            var recipePath = WriteRecipe(PackageRecipe("bwa=0.7.17"));

            var exitCode = RunCli(out _, out var stderr, "validate", recipePath, "--strict-reproducible");

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-RCP-016", stderr, StringComparison.Ordinal);
        }

        [Fact]
        public void S1_03_C04_UnsupportedInstaller_FailsWithoutPackages()
        {
            var parsed = InstallCommandParser.Parse("apt install bwa");

            Assert.Equal(InstallCommandParseStatus.Failed, parsed.Status);
            Assert.Empty(parsed.Packages);
        }

        [Fact]
        public void S1_03_C05_SelectedCandidate_IsSavedAndRenderedVerbatim()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var resolveResult = Candidates(
                new BuildStringCandidate("h5bf99c6_8", "bwa=0.7.17=h5bf99c6_8", "bioconda"),
                new BuildStringCandidate("h6a6fa10_8", "bwa=0.7.17=h6a6fa10_8", "conda-forge"));

            var exitCode = RunInteractivePackage(outPath, resolveResult, out _, out var stderr, "1");

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            var saved = JsonSerializer.Deserialize<RecipeDocument>(File.ReadAllText(outPath), RecipeCreateCommand.JsonOptions)!;
            Assert.Equal(new[] { "bwa=0.7.17=h5bf99c6_8" }, saved.Packages);

            var rawSpecPath = Path.Join(_workDir, "raw-spec.json");
            Assert.Equal(0, RunCli(out _, out _, "render", outPath, "--out", rawSpecPath, "--format", "raw-spec"));
            using var rawSpec = JsonDocument.Parse(File.ReadAllText(rawSpecPath));
            Assert.Contains(
                "RUN conda install -y bwa=0.7.17=h5bf99c6_8\n",
                rawSpec.RootElement.GetProperty("dockerfile_content").GetString(),
                StringComparison.Ordinal);
        }

        [Fact]
        public void S1_03_C06_MalformedCandidate_IsBlockedByFullL1AfterItIsApplied()
        {
            // The candidate arrives after the first full L1 pass — before P03 it
            // was applied and saved without being validated again.
            var outPath = Path.Join(_workDir, "recipe.json");
            var resolveResult = Candidates(
                new BuildStringCandidate("x", "bwa=0.7.17;echo bad", "bioconda"));

            var exitCode = RunInteractivePackage(outPath, resolveResult, out _, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("선택한 값을 적용한 뒤 최종 검증을 통과하지 못해 저장하지 않습니다.", stderr, StringComparison.Ordinal);
            Assert.Contains("L1-", stderr, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("bwa=0.7.17=h5bf99c6_8")]
        [InlineData("bwa=0.7.17")]
        public void S1_03_C07_ResolveUnavailable_KeepsTheTypedPinAndPromisesNoSubmitReResolve(string typedPin)
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var client = new ThrowingResolveRecipeClient(new RpcException(new Status(StatusCode.Unavailable, "down")));

            var exitCode = RunInteractivePackage(outPath, client, out var stdout, out _, typedPin: typedPin);

            Assert.Equal(0, exitCode);
            var saved = JsonSerializer.Deserialize<RecipeDocument>(File.ReadAllText(outPath), RecipeCreateCommand.JsonOptions)!;
            Assert.Equal(new[] { typedPin }, saved.Packages);
            Assert.Contains("패키지 빌드 문자열을 조회하지 못했습니다", stdout, StringComparison.Ordinal);
            Assert.Contains("입력한 패키지 pin을 그대로 저장합니다", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("submit 시점에 다시 해소", stdout, StringComparison.Ordinal);
        }

        // ── S1-04 · source intent / profile / checksum ────────────────────────

        [Fact]
        public void S1_04_C01_StructuredSourceIsNotLabelledOptIn()
        {
            var label = RecipeMethodCatalog.For(RecipeMethodId.SourceStructured).Label;

            Assert.DoesNotContain("고급", label.Get("ko"), StringComparison.Ordinal);
            Assert.DoesNotContain("advanced", label.Get("en"), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("opt-in", label.Get("en"), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void S1_04_C02_NonInteractiveStructured_RendersTwoStageDockerfile()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var exitCode = RunCli(
                out _,
                out _,
                "recipe", "create", outPath, "--non-interactive", "--method", "source-structured",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", "BuildProfile=generic",
                "--field", "SourceUri=https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                "--field", $"SourceChecksum={Digest}",
                "--field", "SourceBuildCommands=make install DESTDIR=/nodekit/output",
                "--field", "RuntimeProfile=minimal");
            Assert.Equal(0, exitCode);

            var rawSpecPath = Path.Join(_workDir, "raw-spec.json");
            Assert.Equal(0, RunCli(out var renderStdout, out _, "render", outPath, "--out", rawSpecPath, "--format", "raw-spec"));
            using var rawSpec = JsonDocument.Parse(File.ReadAllText(rawSpecPath));
            var dockerfile = rawSpec.RootElement.GetProperty("dockerfile_content").GetString()!;

            Assert.Matches("^FROM \\S+ AS builder\n", dockerfile);
            Assert.Contains("sha256sum -c -", dockerfile, StringComparison.Ordinal);
            Assert.Contains("mkdir -p /nodekit/output", dockerfile, StringComparison.Ordinal);
            Assert.Contains("COPY --from=builder /nodekit/output/ /\n", dockerfile, StringComparison.Ordinal);
            Assert.EndsWith("USER 1000\n", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("빌드 완료", renderStdout, StringComparison.Ordinal);
        }

        [Fact]
        public void S1_04_C03_EmptySourceChecksum_IsL1Src001()
        {
            var recipe = StructuredRecipe();
            recipe.SourceChecksum = string.Empty;

            var result = RecipeValidationPipeline.ValidateRecipe(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-SRC-001");
        }

        [Fact]
        public void S1_04_C04_AdvancedProfileWithTagOnlyImage_IsL1Rcp017()
        {
            var recipe = StructuredRecipe();
            recipe.BuildProfile = SourceBuildProfileCatalog.AdvancedKey;
            recipe.BuildProfileImage = "ubuntu:latest";

            var result = RecipeValidationPipeline.ValidateRecipe(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-017");
        }

        [Fact]
        public void S1_04_C05_BuildCommandWithRealLineFeed_IsL1Rcp015()
        {
            // JSON fixture text ["make\nUSER root"] — after deserialization the
            // value holds a real U+000A, not a backslash followed by n.
            var recipe = JsonSerializer.Deserialize<RecipeDocument>(
                "{\"SourceBuildCommands\": [\"make\\nUSER root\"]}", RecipeCreateCommand.JsonOptions)!;
            Assert.Equal("make\nUSER root", recipe.SourceBuildCommands[0]);
            var structured = StructuredRecipe();
            structured.SourceBuildCommands = recipe.SourceBuildCommands;

            var result = RecipeValidationPipeline.ValidateRecipe(structured);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-015");
        }

        [Fact]
        public void S1_04_C06_LegacySourceBuildDependencies_AdvisoryOnlyAndNotInstalled()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var exitCode = RunCli(
                out _,
                out var stderr,
                "recipe", "create", outPath, "--non-interactive", "--method", "source",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", $"BaseImage={ImageRefWithDigest}",
                "--field", "SourceUri=https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                "--field", $"SourceChecksum={Digest}",
                "--field", "SourceBuildCommands=make",
                "--field", "BuildDependencies=make",
                "--field", "BuildDependencies=gcc");

            Assert.Equal(0, exitCode);
            Assert.Contains("BuildDependencies는 현재 자동으로 설치되지 않습니다", stderr, StringComparison.Ordinal);
            Assert.Contains("실제 빌드 서버가 이 Recipe를 수용하는지 확인하지 않습니다", stderr, StringComparison.Ordinal);
            var saved = JsonSerializer.Deserialize<RecipeDocument>(File.ReadAllText(outPath), RecipeCreateCommand.JsonOptions)!;
            Assert.Equal(new[] { "make", "gcc" }, saved.BuildDependencies);

            saved.BuildKind = RecipeKind.SourceBuild;
            var dockerfile = RecipeRenderer.Render(saved).DockerfileContent;
            Assert.DoesNotContain("gcc", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("apt", dockerfile, StringComparison.Ordinal);
        }

        // ── S1-05 · Dockerfile Path or Content ───────────────────────────────

        [Fact]
        public void S1_05_C01_ContentOnly_SavesWithoutPath()
        {
            var outPath = Path.Join(_workDir, "recipe.json");

            var exitCode = RunDockerfileCreate(outPath, out _, out var stderr, $"DockerfileContent={DockerfileText}");

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            var saved = JsonSerializer.Deserialize<RecipeDocument>(File.ReadAllText(outPath), RecipeCreateCommand.JsonOptions)!;
            Assert.Equal(DockerfileText, saved.DockerfileContent);
            Assert.Equal(string.Empty, saved.DockerfilePath);
        }

        [Fact]
        public void S1_05_C02_PathOnly_FreezesFileBytesIntoContent()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var dockerfilePath = Path.Join(_workDir, "Dockerfile");
            File.WriteAllBytes(dockerfilePath, Encoding.UTF8.GetBytes(DockerfileText));

            var exitCode = RunDockerfileCreate(outPath, out _, out var stderr, $"DockerfilePath={dockerfilePath}");

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            var savedBytes = File.ReadAllBytes(outPath);
            var saved = JsonSerializer.Deserialize<RecipeDocument>(savedBytes, RecipeCreateCommand.JsonOptions)!;
            Assert.Equal(DockerfileText, saved.DockerfileContent);
            Assert.Equal(dockerfilePath, saved.DockerfilePath);

            // The source file changes and then disappears: the saved Recipe and
            // its raw_spec do not depend on it any more.
            File.WriteAllText(dockerfilePath, "FROM ubuntu:latest\n");
            var rawSpecBefore = RenderRawSpec(outPath);
            File.Delete(dockerfilePath);
            var rawSpecAfter = RenderRawSpec(outPath);

            Assert.Equal(savedBytes, File.ReadAllBytes(outPath));
            Assert.Equal(rawSpecBefore, rawSpecAfter);
            using var rawSpec = JsonDocument.Parse(rawSpecAfter);
            Assert.Equal(DockerfileText, rawSpec.RootElement.GetProperty("dockerfile_content").GetString());
        }

        [Fact]
        public void S1_05_C02_PathAndContentInput_ProduceTheSameRawSpec()
        {
            var dockerfilePath = Path.Join(_workDir, "Dockerfile");
            File.WriteAllText(dockerfilePath, DockerfileText);
            var fromPath = Path.Join(_workDir, "from-path.json");
            var fromContent = Path.Join(_workDir, "from-content.json");

            Assert.Equal(0, RunDockerfileCreate(fromPath, out _, out _, $"DockerfilePath={dockerfilePath}"));
            Assert.Equal(0, RunDockerfileCreate(fromContent, out _, out _, $"DockerfileContent={DockerfileText}"));

            Assert.Equal(RenderRawSpec(fromContent), RenderRawSpec(fromPath));
        }

        [Fact]
        public void S1_05_C02_Utf8BomIsNotPartOfTheContent()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var dockerfilePath = Path.Join(_workDir, "Dockerfile");
            File.WriteAllBytes(dockerfilePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble());
            File.AppendAllText(dockerfilePath, DockerfileText);

            Assert.Equal(0, RunDockerfileCreate(outPath, out _, out _, $"DockerfilePath={dockerfilePath}"));

            var saved = JsonSerializer.Deserialize<RecipeDocument>(File.ReadAllText(outPath), RecipeCreateCommand.JsonOptions)!;
            Assert.Equal(DockerfileText, saved.DockerfileContent);
        }

        [Fact]
        public void S1_05_C03_PathAndContentTogether_IsAnInputConflict()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var dockerfilePath = Path.Join(_workDir, "Dockerfile");
            File.WriteAllText(dockerfilePath, "FROM other@" + Digest + "\nUSER 1000\n");

            var exitCode = RunDockerfileCreate(
                outPath, out _, out var stderr, $"DockerfilePath={dockerfilePath}", $"DockerfileContent={DockerfileText}");

            Assert.Equal(2, exitCode);
            Assert.Contains("[DOCKERFILE_INPUT_CONFLICT]", stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void S1_05_C04_MissingPath_IsReadFailureWithoutStackTrace()
        {
            var outPath = Path.Join(_workDir, "recipe.json");

            var exitCode = RunDockerfileCreate(
                outPath, out _, out var stderr, $"DockerfilePath={Path.Join(_workDir, "missing", "Dockerfile")}");

            Assert.Equal(2, exitCode);
            Assert.Contains("[DOCKERFILE_READ_FAILED] Dockerfile을 찾을 수 없습니다", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void S1_05_C04_DirectoryPath_IsReadFailure()
        {
            var outPath = Path.Join(_workDir, "recipe.json");

            var exitCode = RunDockerfileCreate(outPath, out _, out var stderr, $"DockerfilePath={_workDir}");

            Assert.Equal(2, exitCode);
            Assert.Contains("[DOCKERFILE_READ_FAILED]", stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void S1_05_C04_NonUtf8File_IsReadFailure()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var dockerfilePath = Path.Join(_workDir, "Dockerfile");
            File.WriteAllBytes(dockerfilePath, new byte[] { 0x46, 0x52, 0x4F, 0x4D, 0x20, 0xFF, 0xFE, 0x0A });

            var exitCode = RunDockerfileCreate(outPath, out _, out var stderr, $"DockerfilePath={dockerfilePath}");

            Assert.Equal(2, exitCode);
            Assert.Contains("UTF-8 텍스트가 아닙니다", stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void S1_05_C05_NoUserInstruction_IsBlockedByFullL1(bool viaPath)
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var content = "FROM " + ImageRefWithDigest + "\n";
            string field;
            if (viaPath)
            {
                var dockerfilePath = Path.Join(_workDir, "Dockerfile");
                File.WriteAllText(dockerfilePath, content);
                field = $"DockerfilePath={dockerfilePath}";
            }
            else
            {
                field = $"DockerfileContent={content}";
            }

            var exitCode = RunDockerfileCreate(outPath, out _, out var stderr, field);

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-RCP-009", stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void S1_05_C06_NonDefaultBuildContext_IsUnsupported()
        {
            var outPath = Path.Join(_workDir, "recipe.json");

            var exitCode = RunDockerfileCreate(
                outPath, out _, out var stderr, $"DockerfileContent={DockerfileText}", "BuildContext=./app");

            Assert.Equal(2, exitCode);
            Assert.Contains("[BUILD_CONTEXT_UNSUPPORTED]", stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void S1_05_C06_BuildContextHelpSaysNoLocalTransfer()
        {
            var field = Assert.Single(
                RecipeFieldCatalog.FieldsFor(RecipeMethodId.Dockerfile), f => f.Name == "BuildContext");

            Assert.Contains("전송하지 않으므로", field.Help.Get("ko"), StringComparison.Ordinal);
            Assert.Contains("does not transfer", field.Help.Get("en"), StringComparison.Ordinal);
        }

        [Fact]
        public void S1_05_DefaultBuildContextField_IsAccepted()
        {
            var outPath = Path.Join(_workDir, "recipe.json");

            var exitCode = RunDockerfileCreate(
                outPath, out _, out _, $"DockerfileContent={DockerfileText}", "BuildContext=.");

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void S1_05_NeitherPathNorContent_NamesBothChoices()
        {
            var outPath = Path.Join(_workDir, "recipe.json");

            var exitCode = RunDockerfileCreate(outPath, out _, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("DockerfilePath=<파일> 또는 --field DockerfileContent=<내용>", stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void S1_05_QuickInteractive_PathPromptImportsTheFile()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var dockerfilePath = Path.Join(_workDir, "Dockerfile");
            File.WriteAllText(dockerfilePath, DockerfileText);
            var transcript = new[]
            {
                "2", "n", "n", "n", "n", "n", "y", "", // quick mode → dockerfile recommended
                "y",                                    // confirm dockerfile warning
                "bwa-mem", "0.7.17", "run.sh",
                ImageRefWithDigest,                     // BaseImage
                Path.Join(_workDir, "missing"),         // DockerfilePath: unreadable → asked again
                dockerfilePath,                         // DockerfilePath: imported, Content not asked
            };

            using var stdin = new StringReader(string.Join("\n", transcript));
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, stdin, stdout, stderr);

            Assert.Equal(0, exitCode);
            var saved = JsonSerializer.Deserialize<RecipeDocument>(File.ReadAllText(outPath), RecipeCreateCommand.JsonOptions)!;
            Assert.Equal(DockerfileText, saved.DockerfileContent);
            Assert.Equal(dockerfilePath, saved.DockerfilePath);
        }

        // ── session one-of contract ──────────────────────────────────────────

        [Fact]
        public void Session_ContentAfterImportedPath_IsOneOfViolation()
        {
            var session = DockerfileSession();
            Assert.Empty(session.SetImportedDockerfile("./Dockerfile", DockerfileText));

            var violations = session.SetField("DockerfileContent", "FROM other\n");

            Assert.Equal("AUTHORING-DOCKERFILE-ONEOF-001", Assert.Single(violations).RuleId);
        }

        [Fact]
        public void Session_ImportAfterTypedContent_IsOneOfViolation()
        {
            var session = DockerfileSession();
            Assert.Empty(session.SetField("DockerfileContent", DockerfileText));

            var violations = session.SetImportedDockerfile("./Dockerfile", "FROM other\n");

            Assert.Equal("AUTHORING-DOCKERFILE-ONEOF-001", Assert.Single(violations).RuleId);
        }

        [Fact]
        public void Session_DirectDockerfilePathSetField_IsRejected()
        {
            var session = DockerfileSession();

            Assert.Throws<InvalidOperationException>(() => session.SetField("DockerfilePath", "./Dockerfile"));
        }

        [Fact]
        public void Session_ClearingImportedPath_ForgetsItsContentToo()
        {
            var session = DockerfileSession();
            Assert.Empty(session.SetImportedDockerfile("./Dockerfile", DockerfileText));

            session.ClearField("DockerfilePath");

            Assert.Contains("DockerfileContent", session.Snapshot().MissingRequiredFields);
            session.SkipOptionalField("DockerfilePath");
            Assert.Empty(session.SetField("DockerfileContent", "FROM typed@" + Digest + "\nUSER 1000\n"));
            var document = session.Build();
            Assert.Equal(string.Empty, document.DockerfilePath);
            Assert.Equal("FROM typed@" + Digest + "\nUSER 1000\n", document.DockerfileContent);
        }

        private static RecipeAuthoringSession DockerfileSession()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Dockerfile);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("BaseImage", ImageRefWithDigest);
            return session;
        }

        private static RecipeDocument PackageRecipe(string pin) => new()
        {
            BuildKind = RecipeKind.Conda,
            ToolName = "bwa-mem",
            Version = "0.7.17",
            Script = "run.sh",
            BaseImage = ImageRefWithDigest,
            PackageEngine = "conda",
            Channels = new List<string> { "bioconda" },
            Packages = new List<string> { pin },
        };

        private static RecipeDocument StructuredRecipe() => new()
        {
            BuildKind = RecipeKind.SourceBuildStructured,
            ToolName = "bwa-mem",
            Version = "0.7.17",
            Script = "run.sh",
            BuildProfile = "generic",
            RuntimeProfile = "minimal",
            SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
            SourceChecksum = Digest,
            SourceBuildCommands = new List<string> { "make install DESTDIR=/nodekit/output" },
        };

        private static ResolveRecipeResult Candidates(params BuildStringCandidate[] candidates) => new(
            RecipeResolutionSource.ExternalSource,
            new[] { new PackageResolution("bwa", "0.7.17", candidates) });

        private static int RunCli(out string stdout, out string stderr, params string[] args)
        {
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var exitCode = CliApp.Run(args, TextReader.Null, stdoutWriter, stderrWriter);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
        }

        private static int RunInteractivePackage(
            string outPath,
            ResolveRecipeResult resolveResult,
            out string stdout,
            out string stderr,
            params string[] candidateAnswers) =>
            RunInteractivePackage(outPath, new FixedResolveRecipeClient(resolveResult), out stdout, out stderr, "bwa=0.7.17", candidateAnswers);

        private static int RunInteractivePackage(
            string outPath,
            IResolveRecipeClient client,
            out string stdout,
            out string stderr,
            string typedPin,
            params string[] candidateAnswers)
        {
            var transcript = new List<string>
            {
                "2", "n", "n", "n", "y", "n", "n", "", // quick mode → package recommended
                "bioconda", "",                         // channels
                "0",                                    // base image: direct entry
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                typedPin, "",                           // Packages
            };
            transcript.AddRange(candidateAnswers);

            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdoutWriter),
                stderrWriter,
                new NeverCancelled(),
                resolveClient: client);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
        }

        private int RunDockerfileCreate(string outPath, out string stdout, out string stderr, params string[] fields)
        {
            var args = new List<string>
            {
                "recipe", "create", outPath, "--non-interactive", "--method", "dockerfile", "--accept-dockerfile-warning",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", $"BaseImage={ImageRefWithDigest}",
            };
            foreach (var field in fields)
            {
                args.Add("--field");
                args.Add(field);
            }

            return RunCli(out stdout, out stderr, args.ToArray());
        }

        private string RenderRawSpec(string recipePath)
        {
            var rawSpecPath = Path.Join(_workDir, "raw-spec-" + Guid.NewGuid() + ".json");
            Assert.Equal(0, RunCli(out _, out _, "render", recipePath, "--out", rawSpecPath, "--format", "raw-spec"));
            return File.ReadAllText(rawSpecPath);
        }

        private string WriteRecipe(RecipeDocument recipe)
        {
            var path = Path.Join(_workDir, "input-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, RecipeCreateCommand.JsonOptions));
            return path;
        }

        private sealed class NeverCancelled : IRecipeCreateCancellationSource
        {
            public bool IsCancellationRequested => false;
        }

        private sealed class FixedResolveRecipeClient : IResolveRecipeClient
        {
            private readonly ResolveRecipeResult _result;

            internal FixedResolveRecipeClient(ResolveRecipeResult result) => _result = result;

            public Task<ResolveRecipeResult> ResolveAsync(
                string toolName,
                string version,
                IReadOnlyList<string> packages,
                CancellationToken cancellationToken,
                RecipeKind? buildKind = null,
                string? packageMirrorUri = null) => Task.FromResult(_result);
        }

        private sealed class ThrowingResolveRecipeClient : IResolveRecipeClient
        {
            private readonly Exception _exception;

            internal ThrowingResolveRecipeClient(Exception exception) => _exception = exception;

            public Task<ResolveRecipeResult> ResolveAsync(
                string toolName,
                string version,
                IReadOnlyList<string> packages,
                CancellationToken cancellationToken,
                RecipeKind? buildKind = null,
                string? packageMirrorUri = null) => throw _exception;
        }
    }
}
