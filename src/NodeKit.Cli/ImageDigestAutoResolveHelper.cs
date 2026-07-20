using System;
using System.Threading;

namespace NodeKit.Cli
{
    /// <summary>
    /// Shared "try to auto-resolve an image digest, describe the failure if it
    /// didn't work" helper used by both <see cref="BeginnerGuideFlow"/>'s
    /// free-text container-ref entry and <see cref="RecipeCreateFlow"/>'s
    /// BaseImage field entry, so the two wizard modes give the same behavior and
    /// wording for the same underlying resolver outcome.
    /// </summary>
    internal static class ImageDigestAutoResolveHelper
    {
        // Returns the resolved digest and the reference it was resolved against
        // (which can differ from imageUri when a resolver rewrites the
        // reference, e.g. MappedHarborImageDigestResolver mapping a host-less
        // public reference onto a concrete Harbor pull path). Both are null on
        // failure — the failure description is written to console before
        // returning.
        internal static (string? Digest, string? ResolvedReference) TryResolveImageDigest(
            string imageUri,
            IImageDigestResolver digestResolver,
            IRecipeConsole console,
            IRecipeCreateCancellationSource cancellation)
        {
            if (cancellation.IsCancellationRequested)
            {
                throw new RecipeCreateCancelledException();
            }

            // 마법사는 동기/블로킹 콘솔 루프라 이 호출 도중에는 사용자가 /cancel을
            // 입력할 방법이 없다 — 유일한 탈출구는 타임아웃뿐이다. 구현체들
            // (HarborImageDigestResolver/PublicRegistryImageDigestResolver)이
            // TaskCanceledException을 이미 NetworkUnavailable 결과로 바꿔주므로
            // 여기서 별도 try/catch가 필요 없다.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = digestResolver.ResolveAsync(imageUri, timeoutCts.Token).GetAwaiter().GetResult();
            if (result.Status == ImageDigestResolutionStatus.Resolved && !string.IsNullOrWhiteSpace(result.Digest))
            {
                return (result.Digest, result.ResolvedReference ?? imageUri);
            }

            console.WriteLine();
            console.WriteLine(DescribeDigestResolutionFailure(result));
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                console.WriteLine(result.Message);
            }

            console.WriteLine("이미지 registry에서 digest를 복사해 입력하세요.");
            return (null, null);
        }

        internal static string DescribeDigestResolutionFailure(ImageDigestResolutionResult result) => result.Status switch
        {
            ImageDigestResolutionStatus.NotFound => "이미지를 찾을 수 없습니다. 이미지 이름과 tag를 확인하세요.",
            ImageDigestResolutionStatus.AuthenticationRequired => "registry 인증이 필요합니다. 현재 CLI는 인증 조회를 지원하지 않습니다.",
            ImageDigestResolutionStatus.NetworkUnavailable => "네트워크 연결을 확인할 수 없습니다. 수동으로 digest를 입력하세요.",
            ImageDigestResolutionStatus.InvalidReference => "이미지 주소 형식이 올바르지 않습니다.",
            ImageDigestResolutionStatus.Unsupported => "현재 환경에서는 자동 조회를 사용할 수 없습니다.",
            ImageDigestResolutionStatus.Resolved => "이미지 digest를 확인했습니다.",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unsupported digest resolution status."),
        };
    }
}
