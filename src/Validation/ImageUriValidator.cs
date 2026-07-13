using System;
using System.Text.RegularExpressions;
using NodeKit.Authoring;
using NodeKit.Policy;

namespace NodeKit.Validation
{
    /// <summary>
    /// L1 이미지 URI 검증기.
    /// - latest 태그 차단
    /// - digest(@sha256:...) 미포함 차단
    /// - digest 형식(64자리 hex) 불일치 차단
    /// </summary>
    internal class ImageUriValidator : IValidator
    {
        // \z, not $ — .NET's $ (without RegexOptions.Multiline) tolerates one
        // trailing '\n', which would let a digest value with an embedded
        // newline pass this check (see RecipeValidator's matching fix note).
        private static readonly Regex _sha256DigestPattern = new(@"\A[0-9a-fA-F]{64}\z", RegexOptions.Compiled);

        public ValidationResult Validate(ToolDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

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

            var lastSlashIndex = imagePart.LastIndexOf('/');
            var tagSeparatorIndex = imagePart.LastIndexOf(':');

            if (tagSeparatorIndex <= lastSlashIndex)
            {
                return ValidationResult.Fail(
                    "L1-IMG-003",
                    $"이미지 태그가 지정되지 않았습니다. 버전 태그와 digest(@sha256:...)를 모두 포함해야 합니다. ({uri})",
                    nameof(definition.ImageUri));
            }

            // SHA256 digest 필수
            var digestIndex = uri.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
            if (digestIndex < 0)
            {
                return ValidationResult.Fail(
                    "L1-IMG-004",
                    $"이미지 digest(@sha256:...)가 없습니다. 재현성 보장을 위해 digest 고정이 필수입니다. ({uri})",
                    nameof(definition.ImageUri));
            }

            // digest 형식 검증 (64자리 hex)
            var digest = uri[(digestIndex + "@sha256:".Length)..];
            if (!_sha256DigestPattern.IsMatch(digest))
            {
                return ValidationResult.Fail(
                    "L1-IMG-005",
                    $"이미지 digest 형식이 올바르지 않습니다. sha256 digest는 64자리 16진수여야 합니다. ({uri})",
                    nameof(definition.ImageUri));
            }

            // ImageUri는 Dockerfile의 첫 번째 FROM base image와 같아야 한다 —
            // 둘 다 빌드 전부터 이미 존재하는 동일한 pinned input 이미지를 가리킨다.
            if (!string.IsNullOrWhiteSpace(definition.DockerfileContent))
            {
                var instructions = DockerfileParser.Parse(definition.DockerfileContent);
                if (instructions.Count > 0
                    && string.Equals(instructions[0].Cmd, "FROM", StringComparison.Ordinal)
                    && instructions[0].Value.Count > 0
                    && !string.Equals(instructions[0].Value[0], uri, StringComparison.Ordinal))
                {
                    return ValidationResult.Fail(
                        "L1-IMG-006",
                        $"ImageUri가 Dockerfile의 첫 번째 FROM base image와 일치하지 않습니다. ImageUri='{uri}', FROM='{instructions[0].Value[0]}'",
                        nameof(definition.ImageUri));
                }
            }

            return ValidationResult.Pass;
        }
    }
}
