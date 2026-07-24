using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Cli
{
    /// <summary>
    /// Resolves image digests from public container registries.
    /// Supports docker.io (Docker Hub) with anonymous token auth, and quay.io
    /// with unauthenticated access for public repositories.
    /// </summary>
    internal sealed class PublicRegistryImageDigestResolver : IImageDigestResolver, IDisposable
    {
        private readonly HttpClient _httpClient;

        public PublicRegistryImageDigestResolver()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        internal PublicRegistryImageDigestResolver(HttpMessageHandler handler)
        {
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<ImageDigestResolutionResult> ResolveAsync(
            string imageUri,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(imageUri))
            {
                return ImageDigestResolutionResult.InvalidReference("이미지 주소가 비어있습니다.");
            }

            if (imageUri.Contains('@'))
            {
                return ImageDigestResolutionResult.InvalidReference(
                    "이미지 참조에 이미 digest가 포함되어 있습니다.");
            }

            if (!TryParseImageRef(imageUri, out var registry, out var repository, out var tag))
            {
                return ImageDigestResolutionResult.InvalidReference(
                    $"이미지 참조를 파싱할 수 없습니다: {imageUri}");
            }

            try
            {
                if (string.Equals(registry, "quay.io", StringComparison.OrdinalIgnoreCase))
                {
                    return await ResolveQuayIoAsync(repository, tag, cancellationToken);
                }

                // docker.io or no registry prefix → Docker Hub
                return await ResolveDockerHubAsync(repository, tag, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return ImageDigestResolutionResult.NetworkUnavailable(
                    $"네트워크 오류: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return ImageDigestResolutionResult.NetworkUnavailable("요청 시간이 초과되었습니다.");
            }
            // 리뷰 지적: ResolveDockerHubAsync가 토큰 응답 본문을
            // JsonDocument.Parse로 파싱하는데, 200 응답이라도 실제 JSON이 아닌
            // 경우(캡티브 포털, 사내 프록시 인증 안내 HTML 등)가 있다 — 이 경우
            // JsonException이 여기 잡히는 두 예외 종류 밖으로 그대로 빠져나가서
            // wizard 전체가 크래시했다. resolver 실패는 항상 수동 입력으로
            // 대체(degrade)돼야 한다는 계약(ImageDigestAutoResolveHelper 참조)을
            // 어기고 있었다.
            catch (JsonException ex)
            {
                return ImageDigestResolutionResult.NetworkUnavailable(
                    $"레지스트리 응답을 파싱할 수 없습니다: {ex.Message}");
            }
        }

        private async Task<ImageDigestResolutionResult> ResolveDockerHubAsync(
            string repository, string tag, CancellationToken ct)
        {
            var tokenUrl =
                $"https://auth.docker.io/token?service=registry.docker.io&scope=repository:{repository}:pull";

            using var tokenResponse = await _httpClient.GetAsync(tokenUrl, ct);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return ImageDigestResolutionResult.NetworkUnavailable(
                    $"Docker Hub 인증 실패: HTTP {(int)tokenResponse.StatusCode}");
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(ct);
            using var tokenDoc = JsonDocument.Parse(tokenJson);
            if (!tokenDoc.RootElement.TryGetProperty("token", out var tokenElement))
            {
                return ImageDigestResolutionResult.NetworkUnavailable(
                    "Docker Hub 토큰 응답을 파싱할 수 없습니다.");
            }

            var token = tokenElement.GetString();
            if (string.IsNullOrEmpty(token))
            {
                return ImageDigestResolutionResult.NetworkUnavailable(
                    "Docker Hub 토큰이 비어 있습니다.");
            }

            using var manifestRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://registry-1.docker.io/v2/{repository}/manifests/{tag}");
            manifestRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            manifestRequest.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/vnd.docker.distribution.manifest.v2+json"));
            manifestRequest.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/vnd.oci.image.manifest.v1+json"));
            manifestRequest.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/vnd.docker.distribution.manifest.list.v2+json"));

            using var manifestResponse = await _httpClient.SendAsync(manifestRequest, ct);
            if (manifestResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return ImageDigestResolutionResult.NotFound(
                    $"이미지를 찾을 수 없습니다: {repository}:{tag}");
            }

            if (!manifestResponse.IsSuccessStatusCode)
            {
                return ImageDigestResolutionResult.NetworkUnavailable(
                    $"매니페스트 조회 실패: HTTP {(int)manifestResponse.StatusCode}");
            }

            return ExtractDigest(manifestResponse, $"{repository}:{tag}");
        }

        private async Task<ImageDigestResolutionResult> ResolveQuayIoAsync(
            string repository, string tag, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://quay.io/v2/{repository}/manifests/{tag}");
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/vnd.docker.distribution.manifest.v2+json"));
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/vnd.oci.image.manifest.v1+json"));

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return ImageDigestResolutionResult.NotFound(
                    $"이미지를 찾을 수 없습니다: quay.io/{repository}:{tag}");
            }

            if (!response.IsSuccessStatusCode)
            {
                return ImageDigestResolutionResult.NetworkUnavailable(
                    $"quay.io 응답 오류: HTTP {(int)response.StatusCode}");
            }

            return ExtractDigest(response, $"quay.io/{repository}:{tag}");
        }

        private static ImageDigestResolutionResult ExtractDigest(
            HttpResponseMessage response, string label)
        {
            if (response.Headers.TryGetValues("Docker-Content-Digest", out var values))
            {
                var digest = values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (digest != null)
                {
                    return ImageDigestResolutionResult.Resolved(digest);
                }
            }

            return ImageDigestResolutionResult.NotFound(
                $"응답에 Docker-Content-Digest 헤더가 없습니다: {label}");
        }

        // Parses "registry/repository:tag", "repository:tag", or "repository".
        // Returns true if parsing succeeds. Images with '@' are rejected upstream.
        private static bool TryParseImageRef(
            string imageUri,
            out string registry,
            out string repository,
            out string tag)
        {
            registry = string.Empty;
            tag = "latest";

            var slashIdx = imageUri.IndexOf('/');
            var rest = imageUri;

            if (slashIdx > 0)
            {
                var firstPart = imageUri[..slashIdx];
                if (firstPart.Contains('.') || firstPart.Contains(':'))
                {
                    registry = firstPart;
                    rest = imageUri[(slashIdx + 1)..];
                }
            }

            var colonIdx = rest.LastIndexOf(':');
            if (colonIdx > 0 && colonIdx < rest.Length - 1)
            {
                repository = rest[..colonIdx];
                tag = rest[(colonIdx + 1)..];
            }
            else
            {
                repository = rest;
            }

            // Docker Hub 공식 이미지(예: alpine, ubuntu)는 실제로는 library/ 네임스페이스
            // 아래에 있다 (docker pull alpine == docker pull docker.io/library/alpine).
            // quay.io는 이 규칙이 없으므로 제외한다.
            if (!string.Equals(registry, "quay.io", StringComparison.OrdinalIgnoreCase)
                && !repository.Contains('/', StringComparison.Ordinal))
            {
                repository = $"library/{repository}";
            }

            return !string.IsNullOrWhiteSpace(repository);
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
