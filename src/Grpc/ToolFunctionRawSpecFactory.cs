using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NodeKit.Grpc
{
    /// <summary>
    /// P06.raw_typed: 두 번째(function) 이미지 빌드의 raw_spec을 NodeVault
    /// 소유 스키마 nodevault.build.raw_spec.v1 그대로 만든다 (v0.9.1 계약 S3-04).
    ///
    /// key는 정확히 {schema_version, kind, base_image_digest, script} 넷이다.
    /// schema_version은 'nodevault.build.raw_spec.v1', kind는 숫자 2
    /// (BUILD_KIND_TOOLFUNCTIONSPEC), base_image_digest는 역할 A(Recipe의
    /// BaseToolImageDigest), script는 ScriptPath 경로 문자열이 아니라 materialize한
    /// source 내용이다. Port/Resource/Display/FunctionId/Revision/ToolSpecDigest 같은
    /// typed 등록 metadata는 넣지 않는다 — 그것은 ToolFunctionRegistrationRequestFactory
    /// 경로다. ToolFunctionBuildRequestPreviewFactory의 stage1/stage2 wrapper는 화면
    /// 미리보기 전용이며 이 wire payload로 재사용하지 않는다.
    ///
    /// base image digest의 대소문자는 바꾸지 않는다 — v1 identity 정규화는
    /// NodeVault가 한다. 이 클래스는 identity/hash를 계산하지 않는다.
    /// </summary>
    internal static class ToolFunctionRawSpecFactory
    {
        public const string SchemaVersion = "nodevault.build.raw_spec.v1";
        public const string InvalidRawSpecCode = "INVALID_RAW_SPEC";

        // BUILD_KIND_TOOLFUNCTIONSPEC — NodeVault는 encoding/json으로 읽으므로
        // 열거형 이름이 아니라 정수값을 보낸다(ToolSpecRawSpecFactory와 같은 이유).
        public const int BuildKindToolFunctionSpec = 2;

        private static readonly string[] _exactKeys = { "schema_version", "kind", "base_image_digest", "script" };

        // nodevault.build.raw_spec.v1.schema.json의 base_image_digest pattern.
        private static readonly Regex _baseImageDigestPattern =
            new(@"\Asha256:[0-9a-fA-F]{64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

        /// <summary>
        /// A와 materialize된 script로 raw_spec JSON을 만든다. 스키마에 맞지 않는
        /// 값이면 payload를 만들지 않고 실패를 돌려준다.
        /// </summary>
        public static bool TryBuild(
            string? baseImageDigest,
            string? script,
            bool indented,
            out string? rawSpecJson,
            out ToolFunctionWireError? error)
        {
            rawSpecJson = null;
            if (!TryCheckFields(baseImageDigest, script, out error))
            {
                return false;
            }

            var payload = new RawSpecV1(SchemaVersion, BuildKindToolFunctionSpec, baseImageDigest!, script!);
            rawSpecJson = indented
                ? JsonSerializer.Serialize(payload, _indented)
                : JsonSerializer.Serialize(payload);
            return true;
        }

        /// <summary>
        /// 이미 만들어진 raw_spec JSON이 v1 스키마와 정확히 맞는지 확인한다.
        /// fixture/serializer 경계용이다: kind 문자열, schema_version 오타, 추가 key,
        /// 빈 script, 뒤에 붙은 내용은 모두 거부한다.
        /// </summary>
        public static bool TryValidate(string? rawSpecJson, out ToolFunctionWireError? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(rawSpecJson))
            {
                error = Invalid("raw_spec이 비어 있습니다.");
                return false;
            }

            JsonDocument document;
            try
            {
                // 한 JSON 값 뒤에 다른 내용이 있으면 Parse가 JsonException을 던진다.
                document = JsonDocument.Parse(rawSpecJson);
            }
            catch (JsonException ex)
            {
                error = Invalid($"raw_spec이 올바른 JSON 하나가 아닙니다 ({ex.Message}).");
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    error = Invalid("raw_spec은 JSON object여야 합니다.");
                    return false;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in root.EnumerateObject())
                {
                    if (Array.IndexOf(_exactKeys, property.Name) < 0)
                    {
                        error = Invalid($"raw_spec v1에 없는 key입니다: '{property.Name}'.");
                        return false;
                    }

                    if (!seen.Add(property.Name))
                    {
                        error = Invalid($"raw_spec key가 중복되었습니다: '{property.Name}'.");
                        return false;
                    }
                }

                foreach (var key in _exactKeys)
                {
                    if (!seen.Contains(key))
                    {
                        error = Invalid($"raw_spec v1 필수 key가 없습니다: '{key}'.");
                        return false;
                    }
                }

                var schemaVersion = root.GetProperty("schema_version");
                if (schemaVersion.ValueKind != JsonValueKind.String || schemaVersion.GetString() != SchemaVersion)
                {
                    error = Invalid($"schema_version은 정확히 '{SchemaVersion}'여야 합니다.");
                    return false;
                }

                // TryGetInt32는 2.0/2e0 같은 정수 아닌 표기를 거부한다(NodeVault encoding/json도 거부).
                var kind = root.GetProperty("kind");
                if (kind.ValueKind != JsonValueKind.Number
                    || !kind.TryGetInt32(out var kindValue)
                    || kindValue != BuildKindToolFunctionSpec)
                {
                    error = Invalid("kind는 숫자 2(BUILD_KIND_TOOLFUNCTIONSPEC)여야 합니다.");
                    return false;
                }

                var digest = root.GetProperty("base_image_digest");
                var script = root.GetProperty("script");
                if (digest.ValueKind != JsonValueKind.String || script.ValueKind != JsonValueKind.String)
                {
                    error = Invalid("base_image_digest와 script는 문자열이어야 합니다.");
                    return false;
                }

                return TryCheckFields(digest.GetString(), script.GetString(), out error);
            }
        }

        private static bool TryCheckFields(string? baseImageDigest, string? script, out ToolFunctionWireError? error)
        {
            error = null;
            if (baseImageDigest is null || !_baseImageDigestPattern.IsMatch(baseImageDigest))
            {
                error = Invalid($"base_image_digest는 'sha256:' 뒤에 16진수 64자리여야 합니다: '{baseImageDigest}'.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(script))
            {
                error = Invalid("script 내용이 비어 있거나 공백/개행뿐입니다.");
                return false;
            }

            return true;
        }

        private static ToolFunctionWireError Invalid(string message) => new(InvalidRawSpecCode, message, 1);

        // 필드 이름·순서는 nodevault.build.raw_spec.v1 스키마 그대로다. 이름을 바꾸거나
        // 필드를 더하면 스키마의 additionalProperties:false에 걸린다.
        internal sealed record RawSpecV1(
            [property: JsonPropertyName("schema_version")] string SchemaVersion,
            [property: JsonPropertyName("kind")] int Kind,
            [property: JsonPropertyName("base_image_digest")] string BaseImageDigest,
            [property: JsonPropertyName("script")] string Script);
    }
}
