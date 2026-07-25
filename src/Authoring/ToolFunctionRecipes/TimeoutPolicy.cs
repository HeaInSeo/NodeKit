namespace NodeKit.Authoring.ToolFunctionRecipes
{
    /// <summary>Soft/hard timeout, in seconds.</summary>
    internal sealed class TimeoutPolicy
    {
        public int? SoftSeconds { get; set; }

        public int? HardSeconds { get; set; }
    }
}
