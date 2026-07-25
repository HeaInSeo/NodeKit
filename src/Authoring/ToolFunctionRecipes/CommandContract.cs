using System.Collections.Generic;

namespace NodeKit.Authoring.ToolFunctionRecipes
{
    /// <summary>Structured execution command (data-model.md CommandContract, FR-005/FR-006).</summary>
    internal sealed class CommandContract
    {
        public string Executable { get; set; } = string.Empty;

        public List<string> Arguments { get; set; } = new();

        public string WorkingDirectory { get; set; } = string.Empty;

        public List<EnvironmentEntry> Environment { get; set; } = new();

        public List<int> SuccessExitCodes { get; set; } = new();

        public TimeoutPolicy? TimeoutPolicy { get; set; }

        public void Normalize()
        {
            Executable ??= string.Empty;
            Arguments ??= new List<string>();
            WorkingDirectory ??= string.Empty;
            Environment ??= new List<EnvironmentEntry>();
            SuccessExitCodes ??= new List<int>();
            if (SuccessExitCodes.Count == 0)
            {
                SuccessExitCodes.Add(0);
            }
        }
    }
}
