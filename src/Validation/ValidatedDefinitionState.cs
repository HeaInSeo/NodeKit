using System;
using NodeKit.Authoring;

namespace NodeKit.Validation
{
    /// <summary>
    /// 마지막으로 검증에 통과한 ToolDefinition의 상태를 추적한다.
    /// </summary>
    internal sealed class ValidatedDefinitionState
    {
        private string? _validatedFingerprint;

        internal bool HasValidatedDefinition => _validatedFingerprint is not null;

        internal void MarkValidated(ToolDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            _validatedFingerprint = ToolDefinitionFingerprint.Create(definition);
        }

        internal void Invalidate()
        {
            _validatedFingerprint = null;
        }

        internal bool Matches(ToolDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return _validatedFingerprint is not null &&
                string.Equals(_validatedFingerprint, ToolDefinitionFingerprint.Create(definition), StringComparison.Ordinal);
        }
    }
}
