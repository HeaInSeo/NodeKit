using System;
using NodeKit.Authoring;

namespace NodeKit.Validation
{
    /// <summary>
    /// L1 이미지 URI 검증기.
    /// - latest 태그 차단
    /// - digest(@sha256:...) 미포함 차단
    /// </summary>
    public class ImageUriValidator : IValidator
    {
        public ValidationResult Validate(ToolDefinition definition)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var uri = definition.ImageUri;

            if (string.IsNullOrWhiteSpace(uri))
            {
                return ValidationResult.Fail("L1-IMG-001", "이미지 URI가 비어있습니다.", nameof(definition.ImageUri));
            }

            // latest 태그 차단
            if (uri.Contains(":latest", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Fail(
                    "L1-IMG-002",
                    $"'latest' 태그는 허용되지 않습니다. 정확한 버전 태그 + digest(@sha256:...)를 사용하세요. ({uri})",
                    nameof(definition.ImageUri));
            }

            // 태그 없이 이미지 이름만 있는 경우 (latest 암묵적 사용)
            var imagePart = uri.Contains('@', StringComparison.Ordinal)
                ? uri[..uri.IndexOf('@', StringComparison.Ordinal)]
                : uri;

            if (!imagePart.Contains(':', StringComparison.Ordinal))
            {
                return ValidationResult.Fail(
                    "L1-IMG-003",
                    $"이미지 태그가 지정되지 않았습니다. 버전 태그와 digest(@sha256:...)를 모두 포함해야 합니다. ({uri})",
                    nameof(definition.ImageUri));
            }

            // SHA256 digest 필수
            if (!uri.Contains("@sha256:", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Fail(
                    "L1-IMG-004",
                    $"이미지 digest(@sha256:...)가 없습니다. 재현성 보장을 위해 digest 고정이 필수입니다. ({uri})",
                    nameof(definition.ImageUri));
            }

            return ValidationResult.Pass;
        }
    }
}
