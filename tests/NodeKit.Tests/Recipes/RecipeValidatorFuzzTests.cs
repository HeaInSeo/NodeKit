using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    /// <summary>
    /// Fuzz tests for the recipe-level L1 rules that stand between free-text
    /// authoring input and an unquoted (Packages/Channels) or quoted
    /// (SourceUri) shell string in the rendered Dockerfile. CLAUDE.md §11
    /// calls out "L1 rules that can be bypassed by unusual input (empty
    /// string, whitespace, unicode variants)" as a required check before any
    /// change is complete — these tests make that check a standing
    /// regression instead of a one-off manual review.
    /// </summary>
    public class RecipeValidatorFuzzTests
    {
        // Characters that must never be accepted inside a SourceUri because
        // RecipeRenderer embeds it inside a double-quoted shell string
        // ("curl -fsSL -o source.tar.gz \"" + SourceUri + "\""); any of these,
        // anywhere in the value, can break out of that quoting.
        private static readonly char[] _sourceUriDangerousChars =
        {
            ' ', '\t', '\n', '\r', '"', '\'', '`', '$', '\\',
        };

        // Characters that must never be accepted inside a Packages/Channels
        // entry because RecipeRenderer appends both completely unquoted onto
        // a "RUN conda install ..." / "RUN conda config --add channels ..."
        // line — there is no quoting at all here, so the allowlist regex is
        // the only thing standing between a package name and shell injection.
        private static readonly char[] _shellMetaChars =
        {
            ' ', ';', '|', '&', '`', '$', '(', ')', '<', '>', '"', '\'', '\\', '#', '\n', '\r', '*', '~', '%', '^',
        };

        private static readonly Gen<char> _alnum = Gen.Char.AlphaNumeric;

        private static Gen<string> CarrierWithDangerousCharInserted(char[] dangerousSet) =>
            from carrier in Gen.String[_alnum, 0, 16]
            from dangerous in Gen.OneOf(dangerousSet.Select(c => Gen.Const(c)).ToArray())
            from position in Gen.Int[0, carrier.Length]
            select carrier.Insert(position, dangerous.ToString());

        [Fact]
        public void SourceUri_AnyDangerousCharacterAnywhere_IsAlwaysRejected()
        {
            CarrierWithDangerousCharInserted(_sourceUriDangerousChars).Sample(
                fuzzedSuffix =>
                {
                    var recipe = MinimalSourceBuildStructured();
                    recipe.SourceUri = "https://example.test/" + fuzzedSuffix;

                    var result = RecipeValidator.Validate(recipe);
                    return result.Violations.Any(v => v.RuleId == "L1-RCP-014");
                },
                iter: 300);
        }

        [Fact]
        public void Packages_AnyShellMetacharacterAnywhere_IsAlwaysRejected()
        {
            CarrierWithDangerousCharInserted(_shellMetaChars).Sample(
                fuzzedVersion =>
                {
                    var recipe = MinimalConda();
                    recipe.Packages.Clear();
                    recipe.Packages.Add("bwa=" + fuzzedVersion);

                    var result = RecipeValidator.Validate(recipe);
                    return result.Violations.Any(v => v.RuleId == "L1-RCP-011");
                },
                iter: 300);
        }

        [Fact]
        public void Channels_AnyShellMetacharacterAnywhere_IsAlwaysRejected()
        {
            CarrierWithDangerousCharInserted(_shellMetaChars).Sample(
                fuzzedChannel =>
                {
                    var recipe = MinimalConda();
                    recipe.Channels.Clear();
                    recipe.Channels.Add("bioconda" + fuzzedChannel);

                    var result = RecipeValidator.Validate(recipe);
                    return result.Violations.Any(v => v.RuleId == "L1-RCP-012");
                },
                iter: 300);
        }

        // Curated "naughty string" corpus: boundary values (empty, huge),
        // malformed/control-character input, and Unicode edge cases (BOM,
        // zero-width space, RTL override, full-width lookalike punctuation,
        // a non-BMP surrogate-pair character). Every value is written as a
        // \u escape rather than a literal character — an inline BOM/RTL-
        // override character in source is itself the "Trojan Source" trick
        // (CVE-2021-42574), which is exactly the kind of hidden-character
        // risk this test exists to catch downstream, so it must not appear
        // literally here either. None of these are randomly generated —
        // they're specific values known to break naive string/regex
        // handling elsewhere, run here against every field an author
        // directly controls to confirm RecipeValidationPipeline never throws.
        public static IEnumerable<object[]> NaughtyStrings()
        {
            var values = new[]
            {
                string.Empty,
                " ",
                "\t",
                "\r\n",
                "\0",
                "\u00A0", // no-break space
                "\u200B", // zero-width space
                "\uFEFF", // byte-order mark
                "\u202E", // right-to-left override
                "\uFF02", // fullwidth quotation mark
                "\uFF04", // fullwidth dollar sign
                "\U0001F600", // emoji — a real non-BMP surrogate pair
                new string('a', 10_000),
                new string('"', 500),
                "../../etc/passwd",
                "$(rm -rf /)",
                "`rm -rf /`",
                "'; DROP TABLE tools; --",
                "<script>alert(1)</script>",
            };

            foreach (var value in values)
            {
                yield return new object[] { value };
            }
        }

        [Theory]
        [MemberData(nameof(NaughtyStrings))]
        public void ValidationPipeline_NaughtyStringInAnyAuthorField_NeverThrows(string naughty)
        {
            var failures = new List<string>();

            foreach (var (label, recipe) in RecipesWithNaughtyFieldInjected(naughty))
            {
                var exception = Record.Exception(() => RecipeValidationPipeline.ValidateRecipe(recipe));
                if (exception is not null)
                {
                    failures.Add($"{label}: {exception.GetType().Name}: {exception.Message}");
                }
            }

            Assert.Empty(failures);
        }

        private static IEnumerable<(string Label, RecipeDocument Recipe)> RecipesWithNaughtyFieldInjected(string naughty)
        {
            foreach (var (field, mutate) in _sourceStructuredFields)
            {
                var recipe = MinimalSourceBuildStructured();
                mutate(recipe, naughty);
                yield return ($"SourceBuildStructured.{field}", recipe);
            }

            foreach (var (field, mutate) in _condaFields)
            {
                var recipe = MinimalConda();
                mutate(recipe, naughty);
                yield return ($"Conda.{field}", recipe);
            }
        }

        private static readonly (string Field, Action<RecipeDocument, string> Mutate)[] _sourceStructuredFields =
        {
            ("SourceUri", (r, v) => r.SourceUri = v),
            ("SourceBuildCommand", (r, v) =>
            {
                r.SourceBuildCommands.Clear();
                r.SourceBuildCommands.Add(v);
            }),
            ("ToolName", (r, v) => r.ToolName = v),
            ("Version", (r, v) => r.Version = v),
        };

        private static readonly (string Field, Action<RecipeDocument, string> Mutate)[] _condaFields =
        {
            ("Package", (r, v) =>
            {
                r.Packages.Clear();
                r.Packages.Add(v);
            }),
            ("Channel", (r, v) =>
            {
                r.Channels.Clear();
                r.Channels.Add(v);
            }),
            ("ToolName", (r, v) => r.ToolName = v),
            ("Version", (r, v) => r.Version = v),
        };

        private static RecipeDocument MinimalSourceBuildStructured() => new()
        {
            BuildKind = RecipeBuildKind.SourceBuildStructured,
            ToolName = "bwa-mem",
            Version = "0.7.17",
            Script = "bwa mem -t 4 ref.fa reads_1.fq reads_2.fq",
            BuildProfile = "generic",
            RuntimeProfile = "minimal",
            SourceUri = "https://example.test/source.tar.gz",
            SourceChecksum = "sha256:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd",
            SourceBuildCommands = { "make install DESTDIR=/nodekit/output" },
            Inputs = { new ToolInput { Name = "reads", Role = "sample-fastq", Format = "fastq", Shape = "pair" } },
            Outputs = { new ToolOutput { Name = "aligned", Role = "aligned-bam", Format = "bam", Shape = "single", Class = "primary" } },
        };

        private static RecipeDocument MinimalConda() => new()
        {
            BuildKind = RecipeBuildKind.Conda,
            ToolName = "bwa-mem",
            Version = "0.7.17",
            Script = "bwa mem -t 4 ref.fa reads_1.fq reads_2.fq",
            BaseImage = "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            Channels = { "bioconda" },
            Packages = { "bwa=0.7.17=h5bf99c6_8" },
            Inputs = { new ToolInput { Name = "reads", Role = "sample-fastq", Format = "fastq", Shape = "pair" } },
            Outputs = { new ToolOutput { Name = "aligned", Role = "aligned-bam", Format = "bam", Shape = "single", Class = "primary" } },
        };
    }
}
