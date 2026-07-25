using System.Collections.Generic;

namespace NodeKit.Authoring.ToolFunctionRecipes
{
    internal enum ObservationLevel
    {
        Basic,
        Enhanced,
        Full,
    }

    /// <summary>승인에 필요한 관찰 수준·커버리지 선언 (FR-016).</summary>
    internal sealed class ValidationRequirements
    {
        public ObservationLevel? MinimumObservationLevel { get; set; }

        public Dictionary<string, bool> RequiredCoverage { get; set; } = new();

        public void Normalize() => RequiredCoverage ??= new Dictionary<string, bool>();
    }
}
