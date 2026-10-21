using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NodeKit.Cli;
using NodeKit.Cli.Localization;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// UF01 (#96) U01/U02: CLI 언어 선택(--lang &gt; NODEKIT_LANG &gt; 기본 ko), ko/en resource key·placeholder 정합, 사용 오류,
    /// 반복·병렬 호출 사이 언어 누출 없음, 언어와 무관한 파일 bytes·stdout payload·exit code.
    /// </summary>
    public sealed class CliLocalizationTests : IDisposable
    {
        private static readonly Regex _hangul = new("[가-힣]", RegexOptions.CultureInvariant);
        private static readonly Regex _placeholder = new(@"\{(\d+)\}", RegexOptions.CultureInvariant);

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-cli-l10n-" + Guid.NewGuid());
        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public CliLocalizationTests() => Directory.CreateDirectory(_workDir);

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        private const string ValidRecipeJson = """
        {
            "SchemaVersion": "draft-1",
            "BuildKind": "DockerfileFallback",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\nUSER 1000\n",
            "Script": "bwa mem",
            "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
            "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
        }
        """;

        private const string InvalidRecipeJson = """
        {
            "SchemaVersion": "draft-1",
            "BuildKind": "SourceBuild",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "SourceUri": "https://example.com/bwa-0.7.17.tar.gz",
            "SourceBuildCommands": [ "make", "make install" ],
            "Script": "bwa mem",
            "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
            "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
        }
        """;

        private sealed record RunResult(int Exit, string Stdout, string Stderr);

        private static RunResult Run(IReadOnlyDictionary<string, string>? environment, params string[] args)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exit = CliApp.Run(
                args,
                TextReader.Null,
                stdout,
                stderr,
                name => environment is not null && environment.TryGetValue(name, out var value) ? value : null);
            return new RunResult(exit, stdout.ToString(), stderr.ToString());
        }

        private static RunResult Run(params string[] args) => Run(null, args);

        private static Dictionary<string, string> Env(string value) => new() { [CliLanguageSelector.EnvironmentVariable] = value };

        private static Dictionary<string, string> ReadAll(ResourceManager manager)
        {
            var set = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
                ?? throw new InvalidOperationException("resource set missing");
            return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        }

        private static SortedSet<string> Placeholders(string text) =>
            new(_placeholder.Matches(text).Select(m => m.Value), StringComparer.Ordinal);

        // U01: 두 resource는 같은 key를 갖고 key마다 같은 {n} 집합을 쓴다. 영어 값은 한글을 담지 않는다.
        [Fact]
        public void Resources_HaveSameKeysAndPlaceholders()
        {
            var ko = ReadAll(CliText.Ko);
            var en = ReadAll(CliText.En);

            Assert.NotEmpty(ko);
            Assert.Equal(ko.Keys.OrderBy(k => k, StringComparer.Ordinal), en.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var (key, koValue) in ko)
            {
                Assert.True(Placeholders(koValue).SetEquals(Placeholders(en[key])), $"placeholder mismatch: {key}");
                Assert.False(_hangul.IsMatch(en[key]), $"English value contains Hangul: {key}");
                Assert.True(_hangul.IsMatch(koValue), $"Korean value has no Hangul: {key}");
            }
        }

        // U01: 언어를 지정하지 않으면 기존 한국어 사용법이다.
        [Fact]
        public void Help_DefaultsToKorean()
        {
            var result = Run("--help");

            Assert.Equal(0, result.Exit);
            Assert.StartsWith("사용법:", result.Stdout, StringComparison.Ordinal);
            Assert.Contains("각 명령의 자세한 옵션은", result.Stdout, StringComparison.Ordinal);
            Assert.Empty(result.Stderr);
        }

        // U01: --lang은 명령 앞·뒤 어디서든, = 형식으로도 받는다. 하위 명령 파서에는 닿지 않는다.
        [Theory]
        [InlineData("Usage:", "--lang", "en", "--help")]
        [InlineData("Usage:", "--lang=en", "--help")]
        [InlineData("Usage: nodekit validate", "validate", "--help", "--lang", "en")]
        [InlineData("Usage: nodekit render", "render", "--lang=EN", "--help")]
        [InlineData("Usage: nodekit recipe create", "recipe", "create", "--help", "--lang", "en-US")]
        [InlineData("Usage: nodekit function-recipe validate", "function-recipe", "validate", "--help", "--lang", "en_US.UTF-8")]
        public void LangOption_SelectsEnglish(string expected, params string[] args)
        {
            var result = Run(args);

            Assert.Equal(0, result.Exit);
            Assert.StartsWith(expected, result.Stdout, StringComparison.Ordinal);
            Assert.False(_hangul.IsMatch(result.Stdout), result.Stdout);
            Assert.Empty(result.Stderr);
        }

        // U01: 우선순위 --lang > NODEKIT_LANG > ko. 빈 환경 변수는 지정하지 않은 것과 같다.
        [Theory]
        [InlineData("en", null, "Usage:")]
        [InlineData("EN-gb", null, "Usage:")]
        [InlineData("ko_KR.UTF-8", null, "사용법:")]
        [InlineData("en", "ko", "사용법:")]
        [InlineData("ko", "en", "Usage:")]
        [InlineData("  ", null, "사용법:")]
        public void EnvironmentAndOption_Precedence(string environmentValue, string? option, string expectedStart)
        {
            var args = option is null ? new[] { "--help" } : new[] { "--lang", option, "--help" };

            var result = Run(Env(environmentValue), args);

            Assert.Equal(0, result.Exit);
            Assert.StartsWith(expectedStart, result.Stdout, StringComparison.Ordinal);
            Assert.Empty(result.Stderr);
        }

        // U01: 지원하지 않는 NODEKIT_LANG은 경고 한 줄 후 한국어로 계속한다(exit은 명령 자체의 값).
        [Fact]
        public void UnsupportedEnvironment_WarnsAndFallsBackToKorean()
        {
            var result = Run(Env("fr_FR.UTF-8"), "--help");

            Assert.Equal(0, result.Exit);
            Assert.StartsWith("사용법:", result.Stdout, StringComparison.Ordinal);
            Assert.Equal(
                "경고: NODEKIT_LANG 값 'fr_FR.UTF-8'은(는) 지원하지 않습니다 (ko 또는 en). 한국어로 표시합니다." + Environment.NewLine,
                result.Stderr);
        }

        // U01: 명시한 --lang의 미지원값·값 누락·중복은 exit 2 사용 오류이고 명령을 실행하지 않는다.
        [Theory]
        [InlineData("--lang 옵션 값이 올바르지 않습니다: fr (ko 또는 en)", "--lang", "fr", "--help")]
        [InlineData("--lang 옵션 값이 올바르지 않습니다:  (ko 또는 en)", "--lang=", "--help")]
        [InlineData("--lang 옵션에는 값이 필요합니다.", "--help", "--lang")]
        [InlineData("--lang 옵션에는 값이 필요합니다.", "--lang", "--help")]
        [InlineData("--lang 옵션이 여러 번 지정되었습니다.", "--lang", "en", "--lang=ko", "--help")]
        public void InvalidLangOption_IsUsageError(string expectedStderr, params string[] args)
        {
            var result = Run(args);

            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Stdout);
            Assert.Equal(expectedStderr + Environment.NewLine, result.Stderr);
        }

        // U01: --lang 사용 오류 문구는 NODEKIT_LANG 언어를 따른다.
        [Fact]
        public void InvalidLangOption_UsesEnvironmentLanguageForError()
        {
            var result = Run(Env("en"), "--lang", "xx", "--help");

            Assert.Equal(2, result.Exit);
            Assert.Equal("Invalid --lang value: xx (ko or en)" + Environment.NewLine, result.Stderr);
        }

        // U01/U03: 이전된 오류 문구가 영어로 나오고, 사용자 값은 그대로 둔다.
        [Fact]
        public void MigratedErrors_AreEnglishAndKeepUserValues()
        {
            var recipePath = Path.Join(_workDir, "그대로-recipe.json");
            File.WriteAllText(recipePath, ValidRecipeJson);

            var unknownCommand = Run("--lang", "en", "frobnicate");
            Assert.Equal(2, unknownCommand.Exit);
            Assert.Equal(
                "Unknown command: frobnicate (supported: validate | render | submit | recipe | function-recipe | receipt)" + Environment.NewLine,
                unknownCommand.Stderr);

            var unknownOption = Run("--lang", "en", "validate", recipePath, "--보기");
            Assert.Equal(2, unknownOption.Exit);
            Assert.Equal("Unknown option: --보기 (supported: --strict-reproducible)" + Environment.NewLine, unknownOption.Stderr);

            var missingOut = Run("--lang", "en", "render", recipePath);
            Assert.Equal(2, missingOut.Exit);
            Assert.Equal("The --out <build-request.json> option is required." + Environment.NewLine, missingOut.Stderr);

            var badFormat = Run("--lang", "en", "render", recipePath, "--out", "-", "--format", "yaml");
            Assert.Equal(2, badFormat.Exit);
            Assert.Equal("Invalid --format value: yaml (build-request or raw-spec)" + Environment.NewLine, badFormat.Stderr);
        }

        // U02: 같은 입력이면 언어와 무관하게 render 파일 bytes, stdout payload, exit code가 같다.
        [Theory]
        [InlineData("raw-spec")]
        [InlineData("build-request")]
        public void Render_OutputBytesAndExitDoNotDependOnLanguage(string format)
        {
            var recipePath = Path.Join(_workDir, "recipe.json");
            File.WriteAllText(recipePath, ValidRecipeJson);
            var koOut = Path.Join(_workDir, "ko.json");
            var enOut = Path.Join(_workDir, "en.json");

            var ko = Run("render", recipePath, "--out", koOut, "--format", format);
            var en = Run("render", recipePath, "--out", enOut, "--format", format, "--lang", "en");
            var koStdout = Run("render", recipePath, "--out", "-", "--format", format);
            var enStdout = Run(Env("en"), "render", recipePath, "--out", "-", "--format", format);

            Assert.Equal(0, ko.Exit);
            Assert.Equal(ko.Exit, en.Exit);
            Assert.Equal(0, koStdout.Exit);
            Assert.Equal(koStdout.Exit, enStdout.Exit);
            if (format == "raw-spec")
            {
                // raw-spec은 실제 submit payload이므로 bytes 그대로 같아야 한다.
                Assert.Equal(File.ReadAllBytes(koOut), File.ReadAllBytes(enOut));
                Assert.Equal(koStdout.Stdout, enStdout.Stdout);
                Assert.Equal(File.ReadAllText(koOut) + Environment.NewLine, koStdout.Stdout);
            }
            else
            {
                // build-request 미리보기는 같은 언어에서도 호출마다 새로 만드는 값(RequestId/ToolDefinitionId/CreatedAt)이 있어
                // 그 세 값만 빼고 비교한다.
                Assert.Equal(WithoutGeneratedIds(File.ReadAllText(koOut)), WithoutGeneratedIds(File.ReadAllText(enOut)));
                Assert.Equal(WithoutGeneratedIds(koStdout.Stdout), WithoutGeneratedIds(enStdout.Stdout));
            }
        }

        private static string WithoutGeneratedIds(string json)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            foreach (var generated in new[] { "RequestId", "ToolDefinitionId", "CreatedAt" })
            {
                Assert.True(node.Remove(generated), generated);
            }

            return node.ToJsonString();
        }

        // U02: validate 성공/실패 exit code와 violation 행(rule id·field)은 언어와 무관하다.
        [Fact]
        public void Validate_ExitCodesDoNotDependOnLanguage()
        {
            var valid = Path.Join(_workDir, "valid.json");
            var invalid = Path.Join(_workDir, "invalid.json");
            File.WriteAllText(valid, ValidRecipeJson);
            File.WriteAllText(invalid, InvalidRecipeJson);

            var koValid = Run("validate", valid);
            var enValid = Run("validate", valid, "--lang", "en");
            var koInvalid = Run("validate", invalid);
            var enInvalid = Run("validate", invalid, "--lang", "en");

            Assert.Equal(0, koValid.Exit);
            Assert.Equal(koValid.Exit, enValid.Exit);
            Assert.Equal(koValid.Stdout, enValid.Stdout);
            Assert.Equal(1, koInvalid.Exit);
            Assert.Equal(koInvalid.Exit, enInvalid.Exit);
            Assert.Equal(RuleIds(koInvalid.Stderr), RuleIds(enInvalid.Stderr));
        }

        private static string[] RuleIds(string stderr) =>
            stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(':', 2)[0])
                .ToArray();

        // U01: 영어로 실행한 뒤에도 다음 기본 호출과 범위 밖 CliText는 한국어이고, 전역 culture는 바뀌지 않는다.
        [Fact]
        public void Language_DoesNotLeakAcrossSequentialRuns()
        {
            var uiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.CurrentCulture;

            Assert.StartsWith("Usage:", Run("--lang", "en", "--help").Stdout, StringComparison.Ordinal);
            Assert.StartsWith("사용법:", Run("--help").Stdout, StringComparison.Ordinal);
            Assert.Equal(CliLanguage.Ko, CliText.Current);
            Assert.Equal(uiCulture, CultureInfo.CurrentUICulture);
            Assert.Equal(culture, CultureInfo.CurrentCulture);
        }

        // U01: 병렬 호출이 서로의 언어를 바꾸지 않는다.
        [Fact]
        public async Task Language_DoesNotLeakAcrossParallelRuns()
        {
            var runs = Enumerable.Range(0, 64).Select(i => Task.Run(() =>
            {
                var english = i % 2 == 0;
                var result = english ? Run("--lang", "en", "validate", "--help") : Run("validate", "--help");
                return (english, result);
            }));

            foreach (var (english, result) in await Task.WhenAll(runs))
            {
                Assert.Equal(0, result.Exit);
                Assert.StartsWith(english ? "Usage: nodekit validate" : "사용법: nodekit validate", result.Stdout, StringComparison.Ordinal);
            }
        }

        // U01: 범위는 중첩되고 Dispose하면 바깥 언어로 돌아간다.
        [Fact]
        public void Scope_RestoresPreviousLanguage()
        {
            Assert.Equal(CliLanguage.Ko, CliText.Current);
            using (CliText.Use(CliLanguage.En))
            {
                Assert.Equal("Validation passed", CliText.Get("FunctionRecipeValidate_Passed"));
                using (CliText.Use(CliLanguage.Ko))
                {
                    Assert.Equal("검증 통과", CliText.Get("FunctionRecipeValidate_Passed"));
                }

                Assert.Equal(CliLanguage.En, CliText.Current);
            }

            Assert.Equal(CliLanguage.Ko, CliText.Current);
        }

        [Theory]
        [InlineData("ko", "Ko")]
        [InlineData("KO-kr", "Ko")]
        [InlineData("en", "En")]
        [InlineData("en_US.UTF-8", "En")]
        [InlineData("en@euro", "En")]
        public void TryParse_AcceptsPrimarySubtag(string value, string expected)
        {
            Assert.True(CliLanguageSelector.TryParse(value, out var language));
            Assert.Equal(Enum.Parse<CliLanguage>(expected), language);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("C")]
        [InlineData("POSIX")]
        [InlineData("ja-JP")]
        [InlineData("korean")]
        [InlineData("eng")]
        public void TryParse_RejectsUnsupported(string? value)
        {
            Assert.False(CliLanguageSelector.TryParse(value, out _));
        }
    }
}
