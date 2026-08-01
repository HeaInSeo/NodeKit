using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    /// <summary>
    /// Regression coverage for the RecipeBuildKind -> RecipeKind internal rename.
    /// RecipeDocument.BuildKind's *type* changed (RecipeBuildKind? -> RecipeKind?),
    /// but the property name and enum member names did not, so JSON produced before
    /// the rename must still deserialize/reserialize under the same "BuildKind" key
    /// with the same string values (nodekit recipe create persists recipe.json with
    /// RecipeCreateCommand.JsonOptions: PropertyNameCaseInsensitive + JsonStringEnumConverter).
    /// </summary>
    public class RecipeDocumentSerializationTests
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        // Shape captured from a recipe.json written before the RecipeBuildKind -> RecipeKind
        // rename. The enum member name ("Conda") is unaffected by the rename, only the
        // .NET type backing it changed, so this literal is exactly what pre-rename
        // NodeKit CLI versions produced.
        private const string PreRenameJson = """
            {
              "Id": "11111111-1111-1111-1111-111111111111",
              "SchemaVersion": "draft-1",
              "BuildKind": "Conda",
              "ToolName": "bwa",
              "Version": "0.7.17",
              "BaseImage": "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
              "Channels": ["bioconda", "conda-forge"],
              "Packages": ["bwa=0.7.17=h5bf99c6_8"]
            }
            """;

        [Fact]
        public void Deserialize_PreRenameJson_ReadsBuildKindUnderSameKey()
        {
            var document = JsonSerializer.Deserialize<RecipeDocument>(PreRenameJson, _jsonOptions);

            Assert.NotNull(document);
            Assert.Equal(RecipeKind.Conda, document!.BuildKind);
            Assert.Equal("bwa", document.ToolName);
        }

        [Fact]
        public void RoundTrip_PreservesBuildKindKeyAndValue()
        {
            var document = JsonSerializer.Deserialize<RecipeDocument>(PreRenameJson, _jsonOptions)!;

            var reserialized = JsonSerializer.Serialize(document, _jsonOptions);
            using var reparsed = JsonDocument.Parse(reserialized);

            Assert.True(reparsed.RootElement.TryGetProperty("BuildKind", out var buildKindElement));
            Assert.Equal("Conda", buildKindElement.GetString());
        }

        [Fact]
        public void RoundTrip_AllRecipeKindMembers_SurviveUnderBuildKindKey()
        {
            // RecipeKind is internal, so it can't be an [InlineData] parameter on a
            // public [Theory] method (CS0051) — loop over the members instead.
            foreach (RecipeKind kind in Enum.GetValues(typeof(RecipeKind)))
            {
                var document = new RecipeDocument { BuildKind = kind };

                var json = JsonSerializer.Serialize(document, _jsonOptions);
                var roundTripped = JsonSerializer.Deserialize<RecipeDocument>(json, _jsonOptions);

                Assert.NotNull(roundTripped);
                Assert.Equal(kind, roundTripped!.BuildKind);
            }
        }
    }
}
