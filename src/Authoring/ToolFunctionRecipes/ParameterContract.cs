namespace NodeKit.Authoring.ToolFunctionRecipes
{
    internal enum ParameterType
    {
        String,
        Integer,
        Number,
        Boolean,
        Enum,
    }

    /// <summary>파일이 아닌 값 계약 (FR-012).</summary>
    internal sealed class ParameterContract
    {
        public string Name { get; set; } = string.Empty;

        public ParameterType Type { get; set; }

        public string DefaultValue { get; set; } = string.Empty;

        public string AllowedRange { get; set; } = string.Empty;

        public bool Required { get; set; }

        public string CliArgumentMapping { get; set; } = string.Empty;

        public string MutuallyExclusiveGroup { get; set; } = string.Empty;

        public void Normalize()
        {
            Name ??= string.Empty;
            DefaultValue ??= string.Empty;
            AllowedRange ??= string.Empty;
            CliArgumentMapping ??= string.Empty;
            MutuallyExclusiveGroup ??= string.Empty;
        }
    }
}
