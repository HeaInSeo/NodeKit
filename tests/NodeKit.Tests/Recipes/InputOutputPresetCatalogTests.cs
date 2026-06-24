using System.Linq;
using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class InputOutputPresetCatalogTests
    {
        [Fact]
        public void InputPresets_ContainsFastqPairedWithExpectedFields()
        {
            var preset = InputOutputPresetCatalog.FindInputPreset("fastq-paired");

            Assert.Equal("reads", preset.Role);
            Assert.Equal("fastq", preset.Format);
            Assert.Equal("pair", preset.Shape);
            Assert.NotEmpty(preset.Examples);
        }

        [Fact]
        public void InputPresets_ContainsAllFiveNamedPresetsPlusCustom()
        {
            var ids = InputOutputPresetCatalog.InputPresets.Select(p => p.Id).ToList();

            Assert.Equal(
                new[] { "fastq-paired", "fastq-single", "bam-alignment", "fasta-reference", "vcf-variants", "custom" },
                ids);
        }

        [Fact]
        public void InputPresets_CustomPreset_HasNoRoleFormatShape()
        {
            var preset = InputOutputPresetCatalog.FindInputPreset(InputOutputPresetCatalog.CustomPresetId);

            Assert.Equal(string.Empty, preset.Role);
            Assert.Equal(string.Empty, preset.Format);
            Assert.Equal(string.Empty, preset.Shape);
        }

        [Fact]
        public void OutputPresets_ContainsBamPrimaryWithExpectedFields()
        {
            var preset = InputOutputPresetCatalog.FindOutputPreset("bam-primary");

            Assert.Equal("alignment", preset.Role);
            Assert.Equal("bam", preset.Format);
            Assert.Equal("primary", preset.Class);
        }

        [Fact]
        public void OutputPresets_ContainsAllFiveNamedPresetsPlusCustom()
        {
            var ids = InputOutputPresetCatalog.OutputPresets.Select(p => p.Id).ToList();

            Assert.Equal(
                new[] { "bam-primary", "bai-index", "vcf-primary", "log-file", "metrics-file", "custom" },
                ids);
        }

        [Fact]
        public void OutputPresets_CustomPreset_HasNoRoleFormatClass()
        {
            var preset = InputOutputPresetCatalog.FindOutputPreset(InputOutputPresetCatalog.CustomPresetId);

            Assert.Equal(string.Empty, preset.Role);
            Assert.Equal(string.Empty, preset.Format);
            Assert.Equal(string.Empty, preset.Class);
        }
    }
}
