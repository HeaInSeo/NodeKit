using System.Collections.Generic;

namespace NodeKit.Authoring.ToolFunctionRecipes
{
    internal enum PortDirection
    {
        Input,
        Output,
    }

    internal enum PortCardinality
    {
        Single,
        Multiple,
    }

    /// <summary>Input/Output shared port shape (data-model.md PortContract, research.md §9).</summary>
    internal sealed class PortContract
    {
        public string Name { get; set; } = string.Empty;

        public PortDirection Direction { get; set; }

        public string DataFormat { get; set; } = string.Empty;

        public PortCardinality Cardinality { get; set; } = PortCardinality.Single;

        /// <summary>Input 전용 — Output에서는 의미 없음.</summary>
        public bool Required { get; set; } = true;

        /// <summary>Input 전용.</summary>
        public string PathPlacementRule { get; set; } = string.Empty;

        /// <summary>Input 전용 (예: BAM+BAI).</summary>
        public List<string> CompanionFiles { get; set; } = new();

        /// <summary>Output 전용.</summary>
        public string PathOrGlob { get; set; } = string.Empty;

        /// <summary>Output 전용.</summary>
        public string CompletionCheck { get; set; } = string.Empty;

        /// <summary>Output 전용, 선택.</summary>
        public string DownstreamCompatibilityNote { get; set; } = string.Empty;

        public void Normalize()
        {
            Name ??= string.Empty;
            DataFormat ??= string.Empty;
            PathPlacementRule ??= string.Empty;
            CompanionFiles ??= new List<string>();
            PathOrGlob ??= string.Empty;
            CompletionCheck ??= string.Empty;
            DownstreamCompatibilityNote ??= string.Empty;
        }
    }
}
