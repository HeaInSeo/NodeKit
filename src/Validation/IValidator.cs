using NodeKit.Authoring;

namespace NodeKit.Validation
{
    /// <summary>L1 정적 검증기 인터페이스.</summary>
    public interface IValidator
    {
        ValidationResult Validate(ToolDefinition definition);
    }
}
