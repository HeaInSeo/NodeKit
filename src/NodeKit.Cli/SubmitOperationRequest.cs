using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NodeKit.Cli.Operations;

namespace NodeKit.Cli
{
    /// <summary>
    /// 로컬 검증을 마친 fresh 빌드 제출 하나의 고정 입력. `nodekit submit`(ToolSpec 첫 이미지)과
    /// `nodekit function-recipe submit`(ToolFunction 두 번째 이미지)이 같은 receipt 기록과
    /// ResolveToolSpec → SubmitToolBuild → WatchToolBuild 경로를 쓴다.
    /// </summary>
    internal sealed record SubmitOperationRequest
    {
        /// <summary>기본 receipt 위치(Recipe 옆 .nodekit/)를 정하는 Recipe 파일 경로.</summary>
        public required string RecipePath { get; init; }

        /// <summary>--url 또는 NODEKIT_NODEVAULT_URL. 주입된 test client면 null일 수 있다.</summary>
        public required string? Url { get; init; }

        public required string? ReceiptPath { get; init; }

        public TimeSpan? ConnectTimeout { get; init; }

        public TimeSpan? WatchTimeout { get; init; }

        public bool Jsonl { get; init; }

        /// <summary>false면 receipt 없이 legacy 경로로 보낸다(주입 client 기존 테스트 전용).</summary>
        public required bool Journal { get; init; }

        public required string OperationKind { get; init; }

        public required OperationEnvelope Envelope { get; init; }

        /// <summary>source snapshot에 고정할 Recipe/동반 파일의 논리 상대 경로와 exact bytes.</summary>
        public required IReadOnlyList<(string LogicalPath, byte[] Content)> SourceFiles { get; init; }

        /// <summary>ToolFunction 두 번째 이미지 빌드일 때만 있다.</summary>
        public SecondImageBuildContext? SecondImage { get; init; }
    }

    /// <summary>
    /// 두 번째 이미지 빌드의 기반 식별자. A=raw_spec.base_image_digest로 보낸 기반 이미지,
    /// C1=Recipe.ToolSpecDigest(기반 ResolvedToolSpec). 출력에서 이름을 붙여 보여줄 뿐
    /// 이번 요청의 C2/B 자리에 쓰지 않는다.
    /// </summary>
    internal sealed record SecondImageBuildContext(string BaseImageDigest, string BaseToolSpecDigest)
    {
        private static readonly Regex _fullDigestPattern =
            new(@"\Asha256:[0-9a-fA-F]{64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// 등록에 쓸 수 있는 두 번째 이미지 B인가 — WatchToolBuild record의 image_digest가
        /// 'sha256:' + 16진수 64자리일 때만 참이다(S3-05-E). 빈 값·잘린 값·태그는 B가 아니다.
        /// </summary>
        public static bool IsFullImageDigest(string? digest) =>
            digest is not null && _fullDigestPattern.IsMatch(digest);
    }
}
