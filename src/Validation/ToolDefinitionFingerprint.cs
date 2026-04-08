using System;
using System.Linq;
using System.Text;
using NodeKit.Authoring;

namespace NodeKit.Validation
{
    /// <summary>
    /// 검증 성공 시점의 ToolDefinition과 현재 폼 입력이 같은지 비교하기 위한 fingerprint 생성기.
    /// </summary>
    internal static class ToolDefinitionFingerprint
    {
        private const char FieldSeparator = '\u001f';
        private const char ItemSeparator = '\u001e';

        internal static string Create(ToolDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            var builder = new StringBuilder();
            Append(builder, definition.Name);
            Append(builder, definition.ImageUri);
            Append(builder, definition.DockerfileContent);
            Append(builder, definition.Script);
            Append(builder, definition.EnvironmentSpec);
            AppendList(builder, definition.Inputs.Select(input => input.Name));
            AppendList(builder, definition.Outputs.Select(output => output.Name));
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string? value)
        {
            builder.Append(value ?? string.Empty);
            builder.Append(FieldSeparator);
        }

        private static void AppendList(StringBuilder builder, System.Collections.Generic.IEnumerable<string?> values)
        {
            builder.Append(string.Join(ItemSeparator, values.Select(value => value ?? string.Empty)));
            builder.Append(FieldSeparator);
        }
    }
}
