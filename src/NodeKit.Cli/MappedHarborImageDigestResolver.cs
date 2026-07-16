using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Cli
{
    /// <summary>
    /// Wraps <see cref="HarborImageDigestResolver"/> with the
    /// <see cref="HarborImageReferenceMapper"/> mapping layer, so
    /// <see cref="BaseImageCatalog"/>'s host-less public candidates can be
    /// resolved against Harbor without <see cref="HarborImageDigestResolver"/>
    /// itself guessing a project path — that resolver only ever parses and looks
    /// up an already-complete Harbor reference; this wrapper is where the
    /// "public reference → concrete Harbor reference" decision happens.
    /// </summary>
    internal sealed class MappedHarborImageDigestResolver : IImageDigestResolver
    {
        private const string NoMappingMessage =
            "이 이미지는 Harbor 매핑이 설정되지 않아 자동 조회할 수 없습니다. " +
            "NODEKIT_HARBOR_IMAGE_MAP 환경변수로 origin=harbor전체경로 형식의 매핑을 설정하거나, " +
            "[0] 직접 입력으로 Harbor 이미지 주소(예: harbor.lab.local/<project>/<repo>:<tag>)를 입력하세요.";

        private readonly HarborImageDigestResolver _inner;

        internal MappedHarborImageDigestResolver(HarborImageDigestResolver inner)
        {
            _inner = inner;
        }

        // True when NODEKIT_HARBOR_IMAGE_MAP has at least one entry — used to
        // decide whether the base-image selection screen may claim auto-resolve
        // is available at all, before the user picks a candidate.
        internal bool HasAnyMapping => HarborImageReferenceMapper.HasAnyMapping();

        public async Task<ImageDigestResolutionResult> ResolveAsync(string imageUri, CancellationToken cancellationToken)
        {
            // Try the reference exactly as given first. A reference that already
            // carries an explicit host (e.g. the user typed a full
            // "harbor.lab.local/project/repo:tag" via [0] 직접 입력, or picked a
            // candidate hosted somewhere else entirely) is handled correctly by
            // HarborImageDigestResolver's existing host-matching — resolved,
            // not-found, wrong-host Unsupported, etc. Only a genuinely host-less
            // reference (BaseImageCatalog's public candidates) comes back
            // InvalidReference, which is the one case the mapping layer exists for.
            var direct = await _inner.ResolveAsync(imageUri, cancellationToken).ConfigureAwait(false);
            if (direct.Status != ImageDigestResolutionStatus.InvalidReference)
            {
                return direct.Status == ImageDigestResolutionStatus.Resolved
                    ? direct with { ResolvedReference = imageUri }
                    : direct;
            }

            var mapped = HarborImageReferenceMapper.TryMapToHarbor(imageUri);
            if (mapped is null)
            {
                return ImageDigestResolutionResult.Unsupported(NoMappingMessage);
            }

            var result = await _inner.ResolveAsync(mapped, cancellationToken).ConfigureAwait(false);
            return result.Status == ImageDigestResolutionStatus.Resolved
                ? result with { ResolvedReference = mapped }
                : result;
        }
    }
}
