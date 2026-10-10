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
    }
}
