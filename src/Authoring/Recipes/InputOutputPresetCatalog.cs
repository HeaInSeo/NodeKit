using System.Collections.Generic;
using System.Linq;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Fixed FASTQ/BAM/VCF presets for ToolInput/ToolOutput authoring — see
    /// design doc Section 13. The "custom" entry has no preset values; it
    /// signals that Role/Format/Shape (or Class) must be asked directly.
    /// </summary>
    internal static class InputOutputPresetCatalog
    {
        public const string CustomPresetId = "custom";

        public static IReadOnlyList<ToolInputPreset> InputPresets { get; } = new List<ToolInputPreset>
        {
            new(
                "fastq-paired",
                Text("FASTQ paired-end reads", "FASTQ paired-end reads"),
                Text("쌍을 이루는 FASTQ 시퀀싱 리드입니다.", "Paired-end FASTQ sequencing reads."),
                Role: "reads",
                Format: "fastq",
                Shape: "pair",
                Examples: new[] { "sample_R1.fastq.gz", "sample_R2.fastq.gz" }),
            new(
                "fastq-single",
                Text("FASTQ single-end reads", "FASTQ single-end reads"),
                Text("단일 FASTQ 시퀀싱 리드입니다.", "Single-end FASTQ sequencing reads."),
                Role: "reads",
                Format: "fastq",
                Shape: "single",
                Examples: new[] { "sample.fastq.gz" }),
            new(
                "bam-alignment",
                Text("BAM alignment", "BAM alignment"),
                Text("정렬된 BAM 파일입니다.", "An aligned BAM file."),
                Role: "alignment",
                Format: "bam",
                Shape: "single",
                Examples: new[] { "sample.bam" }),
            new(
                "fasta-reference",
                Text("FASTA reference", "FASTA reference"),
                Text("참조 서열 FASTA 파일입니다.", "A reference sequence FASTA file."),
                Role: "reference",
                Format: "fasta",
                Shape: "single",
                Examples: new[] { "reference.fasta" }),
            new(
                "vcf-variants",
                Text("VCF variants", "VCF variants"),
                Text("variant 호출 결과 VCF 파일입니다.", "A variant-call VCF file."),
                Role: "variants",
                Format: "vcf",
                Shape: "single",
                Examples: new[] { "sample.vcf.gz" }),
            new(
                CustomPresetId,
                Text("직접 입력", "Custom"),
                Text("Role/Format/Shape를 직접 입력합니다.", "Enter Role/Format/Shape directly."),
                Role: string.Empty,
                Format: string.Empty,
                Shape: string.Empty,
                Examples: System.Array.Empty<string>()),
        };

        public static IReadOnlyList<ToolOutputPreset> OutputPresets { get; } = new List<ToolOutputPreset>
        {
            new(
                "bam-primary",
                Text("BAM alignment output", "BAM alignment output"),
                Text("주 산출물인 BAM 정렬 파일입니다.", "The primary BAM alignment output."),
                Role: "alignment",
                Format: "bam",
                Class: "primary",
                Examples: new[] { "output.bam" }),
            new(
                "bai-index",
                Text("BAM index output", "BAM index output"),
                Text("BAM 인덱스 파일입니다.", "The BAM index file."),
                Role: "index",
                Format: "bai",
                Class: "index",
                Examples: new[] { "output.bam.bai" }),
            new(
                "vcf-primary",
                Text("VCF variant output", "VCF variant output"),
                Text("주 산출물인 VCF variant 파일입니다.", "The primary VCF variant output."),
                Role: "variants",
                Format: "vcf",
                Class: "primary",
                Examples: new[] { "output.vcf.gz" }),
            new(
                "log-file",
                Text("Log file", "Log file"),
                Text("실행 로그 파일입니다.", "An execution log file."),
                Role: "log",
                Format: "txt",
                Class: "log",
                Examples: new[] { "run.log" }),
            new(
                "metrics-file",
                Text("Metrics file", "Metrics file"),
                Text("실행 지표 파일입니다.", "An execution metrics file."),
                Role: "metrics",
                Format: "txt",
                Class: "metrics",
                Examples: new[] { "metrics.txt" }),
            new(
                CustomPresetId,
                Text("직접 입력", "Custom"),
                Text("Role/Format/Class를 직접 입력합니다.", "Enter Role/Format/Class directly."),
                Role: string.Empty,
                Format: string.Empty,
                Class: string.Empty,
                Examples: System.Array.Empty<string>()),
        };

        public static ToolInputPreset FindInputPreset(string id) =>
            InputPresets.Single(p => p.Id == id);

        public static ToolOutputPreset FindOutputPreset(string id) =>
            OutputPresets.Single(p => p.Id == id);

        private static LocalizedText Text(string ko, string en) =>
            new(new Dictionary<string, string> { ["ko"] = ko, ["en"] = en });
    }
}
