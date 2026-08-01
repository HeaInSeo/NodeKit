using System.Collections.Generic;

namespace NodeKit.Authoring.ToolFunctionRecipes
{
    /// <summary>실행 환경/호환성 선언 (FR-015).</summary>
    internal sealed class ExecutionEnvironmentContract
    {
        public List<string> SupportedPlatforms { get; set; } = new();

        public List<string> WritablePaths { get; set; } = new();

        public string NetworkPolicy { get; set; } = string.Empty;

        public bool RequiresRoot { get; set; }

        public List<string> RequiredCapabilities { get; set; } = new();

        public void Normalize()
        {
            SupportedPlatforms ??= new List<string>();
            WritablePaths ??= new List<string>();
            NetworkPolicy ??= string.Empty;
            RequiredCapabilities ??= new List<string>();
        }
    }
}
