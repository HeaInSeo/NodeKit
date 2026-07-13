using System;
using System.Linq;
using CsCheck;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    /// <summary>
    /// Property-based test for the R22-C security invariant (design doc
    /// §2.6 Q5, §5): for any valid SourceUri / SourceBuildCommands content,
    /// RenderSourceBuildStructured must keep exactly the fixed builder/
    /// runtime instruction skeleton — no recipe field value can smuggle in
    /// an extra Dockerfile directive. The hand-written tests in
    /// RecipeRendererTests only check one or two hand-picked inputs; this
    /// checks the property against many generated ones so a future change
    /// that only breaks on unusual content doesn't slip through.
    /// </summary>
    public class RecipeRendererPropertyTests
    {
        // Printable ASCII (space..~). Deliberately includes shell
        // metacharacters ($, `, ", ', &, |, ;) because SourceBuildCommands
        // intentionally allows them (see RecipeValidator.ValidateSourceFetchFields
        // comment) — the property under test is that they still can't produce
        // a second Dockerfile directive, not that they're rejected.
        private static readonly Gen<char> _printableAscii =
            Gen.Int[0x20, 0x7E].Select(i => (char)i);

        // SourceUri additionally forbids whitespace/quotes/backtick/$/backslash
        // (RecipeValidator's _sourceUriPattern) — the generator is constructed
        // to always be valid so every sample exercises the renderer, not the
        // validator's rejection path.
        private static readonly Gen<char> _sourceUriSafeChar =
            _printableAscii.Where(c => c is not (' ' or '"' or '\'' or '`' or '$' or '\\'));

        private static readonly Gen<string> _sourceUriSuffixGen =
            Gen.String[_sourceUriSafeChar, 1, 24];

        private static readonly Gen<string> _buildCommandGen =
            Gen.String[_printableAscii, 1, 40];

        private static readonly string[] _instructionKeywords =
        {
            "FROM", "RUN", "COPY", "ADD", "USER", "ENTRYPOINT", "CMD", "ARG",
            "ENV", "LABEL", "WORKDIR", "EXPOSE", "VOLUME", "HEALTHCHECK",
            "SHELL", "ONBUILD", "STOPSIGNAL",
        };

        private static readonly string[] _expectedInstructionSkeleton =
        {
            "FROM", "RUN", "FROM", "COPY", "USER",
        };

        [Fact]
        public void Render_SourceBuildStructured_AnyValidSourceUriAndCommands_KeepsFixedInstructionSkeleton()
        {
            (from suffix in _sourceUriSuffixGen
             from commands in _buildCommandGen.Array[1, 4]
             select (suffix, commands))
            .Sample(
                t =>
                {
                    var recipe = MinimalRecipe();
                    recipe.SourceUri = "https://example.test/" + t.suffix;
                    recipe.SourceBuildCommands.AddRange(t.commands);

                    // The generator is constructed to only ever produce values
                    // RecipeValidator itself accepts; if this ever trips, the
                    // generator's assumptions (not the renderer) are the bug.
                    var recipeResult = RecipeValidator.Validate(recipe);
                    if (!recipeResult.IsValid)
                    {
                        return false;
                    }

                    var dockerfile = RecipeRenderer.Render(recipe).DockerfileContent;

                    var actualInstructions = dockerfile
                        .Split('\n')
                        .Select(line => line.Split(' ', 2)[0])
                        .Where(token => _instructionKeywords.Contains(token, StringComparer.Ordinal))
                        .ToArray();

                    return actualInstructions.SequenceEqual(_expectedInstructionSkeleton)
                        && dockerfile.Contains(recipe.SourceUri, StringComparison.Ordinal);
                },
                iter: 300);
        }

        private static RecipeDocument MinimalRecipe()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.SourceBuildStructured,
                ToolName = "bwa-mem",
                Version = "0.7.17",
                Script = "bwa mem -t 4 ref.fa reads_1.fq reads_2.fq",
                BuildProfile = "generic",
                RuntimeProfile = "minimal",
                SourceChecksum = "sha256:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd",
                Inputs = { new ToolInput { Name = "reads", Role = "sample-fastq", Format = "fastq", Shape = "pair" } },
                Outputs = { new ToolOutput { Name = "aligned", Role = "aligned-bam", Format = "bam", Shape = "single", Class = "primary" } },
            };
            return recipe;
        }
    }
}
