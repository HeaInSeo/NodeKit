using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace NodeKit.Cli.Operations
{
    /// <summary>
    /// fresh 요청을 준비한 순간의 authoring/companion 파일 exact bytes(S2-02-C01).
    /// local record root 아래 content-addressed 불변 artifact로 저장된다.
    /// 이 SHA256 binding은 local 규칙이며 전역 ToolSpec/Manifest/Lock identity가 아니다.
    /// </summary>
    internal sealed record SourceSnapshot
    {
        public const string CurrentSchemaVersion = "nodekit.source-snapshot.v1";

        [JsonPropertyName("schema_version")]
        public required string SchemaVersion { get; init; }

        [JsonPropertyName("files")]
        public required IReadOnlyList<SourceSnapshotFile> Files { get; init; }

        /// <summary>logical path 순서를 고정해 같은 입력이 항상 같은 bytes가 되게 한다.</summary>
        public static SourceSnapshot FromFiles(IEnumerable<(string LogicalPath, byte[] Content)> files)
        {
            ArgumentNullException.ThrowIfNull(files);
            var entries = files
                .OrderBy(f => f.LogicalPath, StringComparer.Ordinal)
                .Select(f => new SourceSnapshotFile
                {
                    LogicalPath = f.LogicalPath,
                    Sha256 = OperationHashing.Sha256Hex(f.Content),
                    ContentBase64 = Convert.ToBase64String(f.Content),
                })
                .ToList();

            return new SourceSnapshot { SchemaVersion = CurrentSchemaVersion, Files = entries };
        }

        /// <summary>
        /// 모든 entry가 디코드 가능한 base64이고 그 bytes가 선언된 소문자 SHA-256과 같은지 확인한다.
        /// 바깥 JSON hash만으로는 entry별 digest를 믿을 수 없다. 문제가 없으면 null, 있으면 이유.
        /// </summary>
        public string? FindInvalidEntry()
        {
            if (Files is null)
            {
                return "files 누락";
            }

            for (var i = 0; i < Files.Count; i++)
            {
                var file = Files[i];
                if (file is null || string.IsNullOrEmpty(file.LogicalPath))
                {
                    return $"files[{i}]의 logical_path 누락";
                }

                if (!OperationHashing.IsSha256Hex(file.Sha256))
                {
                    return $"{file.LogicalPath}의 sha256이 소문자 SHA-256 hex가 아님";
                }

                if (file.ContentBase64 is null)
                {
                    return $"{file.LogicalPath}의 content_base64 누락";
                }

                byte[] content;
                try
                {
                    content = Convert.FromBase64String(file.ContentBase64);
                }
                catch (FormatException)
                {
                    return $"{file.LogicalPath}의 content_base64를 디코드할 수 없음";
                }

                if (OperationHashing.Sha256Hex(content) != file.Sha256)
                {
                    return $"{file.LogicalPath}의 content bytes가 선언된 sha256과 다름";
                }
            }

            return null;
        }
    }

    internal sealed record SourceSnapshotFile
    {
        /// <summary>Recipe 기준 논리 상대 path. 설치/CWD/user 절대 path는 identity 재료가 아니다.</summary>
        [JsonPropertyName("logical_path")]
        public required string LogicalPath { get; init; }

        [JsonPropertyName("sha256")]
        public required string Sha256 { get; init; }

        [JsonPropertyName("content_base64")]
        public required string ContentBase64 { get; init; }
    }

    internal static class OperationHashing
    {
        public static string Sha256Hex(byte[] content) =>
            Convert.ToHexStringLower(SHA256.HashData(content));

        /// <summary>정확히 소문자 hex 64자인가 — snapshot 파일 이름으로 쓰기 전에 확인한다.</summary>
        public static bool IsSha256Hex(string? value) =>
            value is { Length: 64 } && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}
