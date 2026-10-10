using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// 외부 Recipe/ToolFunctionRecipe 파일 입력 오류 하나. Code는 submit
    /// --format jsonl의 errorCode로 그대로 쓰인다(docs/NODEKIT_CLI_USAGE.md).
    /// 이 오류는 모두 exit 2 — 파싱된 문서의 L1 위반(exit 1)과 구분된다.
    /// </summary>
    internal sealed record AuthoringLoadError(string Code, string Message);

    /// <summary>
    /// validate/render/submit이 공유하는 authoring 파일 loader
    /// (Fixtures/Contract/cli-acceptance-contract.json authoringSchema /
    /// inputErrorContract, owner P02.loader).
    ///
    /// 순서: 파일 읽기 → JSON 문서 파싱 → 최상위 객체 확인 → SchemaVersion
    /// draft-1 gate → 역직렬화. gate를 역직렬화 전에 두는 이유: RecipeDocument와
    /// ToolFunctionRecipe initializer가 SchemaVersion을 draft-1로 채우므로
    /// 역직렬화 후에는 "값이 없음"을 구별할 수 없다. 자동 보완/migration은 하지
    /// 않는다.
    /// </summary>
    internal static class AuthoringFileLoader
    {
        public const string SupportedSchemaVersion = "draft-1";

        public const string ReadFailedCode = "RECIPE_READ_FAILED";
        public const string ParseFailedCode = "RECIPE_PARSE_FAILED";
        public const string EmptyCode = "RECIPE_EMPTY";
        public const string UnsupportedSchemaVersionCode = "UNSUPPORTED_SCHEMA_VERSION";
        public const string MissingBuildKindCode = "MISSING_BUILD_KIND";
        public const string UnsupportedBuildKindCode = "UNSUPPORTED_BUILD_KIND";

        private const string SchemaVersionProperty = "SchemaVersion";

        public static bool TryLoad<T>(string path, JsonSerializerOptions options, out T? document, out AuthoringLoadError? error)
            where T : class =>
            TryLoad(path, options, out document, out error, out _);

        /// <summary>
        /// 위와 같지만 파싱한 바로 그 파일 bytes도 돌려준다 — source snapshot이 다시 읽은
        /// 다른 내용이 아니라 실제로 검증·렌더한 입력과 같은 bytes를 고정하게 한다.
        /// </summary>
        public static bool TryLoad<T>(string path, JsonSerializerOptions options, out T? document, out AuthoringLoadError? error, out byte[]? rawBytes)
            where T : class
        {
            document = null;
            error = null;
            rawBytes = null;

            string content;
            try
            {
                rawBytes = File.ReadAllBytes(path);

                // File.ReadAllText와 같은 해석(BOM 감지, 기본 UTF-8)을 같은 bytes에 적용한다.
                using var reader = new StreamReader(new MemoryStream(rawBytes), detectEncodingFromByteOrderMarks: true);
                content = reader.ReadToEnd();
            }
            catch (UnauthorizedAccessException ex)
            {
                // UnauthorizedAccessException은 IOException의 하위 타입이 아니다 —
                // 읽기 권한 없음/디렉터리 경로가 여기로 온다.
                error = new AuthoringLoadError(ReadFailedCode, $"recipe 파일을 읽을 권한이 없습니다: {path} ({ex.Message})");
                return false;
            }
            catch (IOException ex)
            {
                error = new AuthoringLoadError(ReadFailedCode, $"recipe 파일을 읽을 수 없습니다: {path} ({ex.Message})");
                return false;
            }

            try
            {
                using var parsed = JsonDocument.Parse(content);
                var root = parsed.RootElement;
                if (root.ValueKind == JsonValueKind.Null)
                {
                    error = new AuthoringLoadError(EmptyCode, $"recipe 파일이 비어있습니다: {path}");
                    return false;
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    error = new AuthoringLoadError(
                        ParseFailedCode,
                        $"recipe JSON 파싱에 실패했습니다: {path} (최상위 값은 JSON 객체여야 합니다. 현재: {root.ValueKind})");
                    return false;
                }

                if (!TryCheckSchemaVersion(root, path, out error))
                {
                    return false;
                }

                document = root.Deserialize<T>(options);
            }
            catch (JsonException ex)
            {
                error = new AuthoringLoadError(ParseFailedCode, $"recipe JSON 파싱에 실패했습니다: {path} ({ex.Message})");
                return false;
            }

            if (document is null)
            {
                error = new AuthoringLoadError(EmptyCode, $"recipe 파일이 비어있습니다: {path}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// RecipeDocument 전용: 공통 loader + Normalize + BuildKind 확인.
        /// validate/render/submit이 같은 분류를 쓰도록 여기 한 곳에 둔다.
        /// </summary>
        public static bool TryLoadRecipe(string path, JsonSerializerOptions options, out RecipeDocument? recipe, out AuthoringLoadError? error) =>
            TryLoadRecipe(path, options, out recipe, out error, out _);

        /// <summary>위와 같지만 파싱한 파일 bytes도 돌려준다(submit의 source snapshot용).</summary>
        public static bool TryLoadRecipe(string path, JsonSerializerOptions options, out RecipeDocument? recipe, out AuthoringLoadError? error, out byte[]? rawBytes)
        {
            if (!TryLoad(path, options, out recipe, out error, out rawBytes))
            {
                return false;
            }

            recipe!.Normalize();

            if (recipe.BuildKind is null)
            {
                error = new AuthoringLoadError(
                    MissingBuildKindCode,
                    $"recipe 파일에 buildKind가 없습니다: {path} ({SupportedBuildKindsHint()} 중 하나를 지정하세요.)");
                recipe = null;
                return false;
            }

            // JsonStringEnumConverter는 숫자 값(예: 999)도 받아들인다 — 정의되지
            // 않은 kind는 렌더러에서 예외로 터지기 전에 입력 오류로 돌려보낸다.
            if (!Enum.IsDefined(recipe.BuildKind.Value))
            {
                error = new AuthoringLoadError(
                    UnsupportedBuildKindCode,
                    $"지원하지 않는 buildKind입니다: {path} ({recipe.BuildKind.Value:D}). {SupportedBuildKindsHint()} 중 하나를 지정하세요.");
                recipe = null;
                return false;
            }

            return true;
        }

        private static string SupportedBuildKindsHint() =>
            string.Join(" | ", Enum.GetNames<RecipeKind>());

        // property 이름은 대소문자 무시, 값은 정확히 일치(cli-acceptance-contract.json
        // authoringSchema). 이름만 대소문자가 다른 키가 둘 이상이면 어느 값이
        // 쓰일지 모호하므로 거부한다.
        private static bool TryCheckSchemaVersion(JsonElement root, string path, out AuthoringLoadError? error)
        {
            error = null;
            var matches = root.EnumerateObject()
                .Where(p => string.Equals(p.Name, SchemaVersionProperty, StringComparison.OrdinalIgnoreCase))
                .ToList();

            string? problem = null;
            if (matches.Count == 0)
            {
                problem = "SchemaVersion이 없습니다";
            }
            else if (matches.Count > 1)
            {
                problem = "SchemaVersion이 여러 번 지정되었습니다";
            }
            else if (matches[0].Value.ValueKind != JsonValueKind.String)
            {
                problem = $"SchemaVersion 값이 문자열이 아닙니다({matches[0].Value.ValueKind})";
            }
            else if (!string.Equals(matches[0].Value.GetString(), SupportedSchemaVersion, StringComparison.Ordinal))
            {
                problem = $"지원하지 않는 SchemaVersion입니다: '{matches[0].Value.GetString()}'";
            }

            if (problem is null)
            {
                return true;
            }

            error = new AuthoringLoadError(
                UnsupportedSchemaVersionCode,
                $"{problem}: {path} (지원 값: \"{SchemaVersionProperty}\": \"{SupportedSchemaVersion}\". 자동 보완/변환은 하지 않습니다.)");
            return false;
        }
    }
}
