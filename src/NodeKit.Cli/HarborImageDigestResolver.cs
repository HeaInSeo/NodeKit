using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Cli
{
    /// <summary>
    /// Resolves image digests by querying Harbor's OCI Distribution API.
    /// Configure via environment variables:
    ///   NODEKIT_HARBOR_URL      — required, e.g. https://harbor.lab.local
    ///   NODEKIT_HARBOR_CA_CERT  — optional path to a PEM CA cert file
    ///   NODEKIT_HARBOR_USER     — optional, for authenticated projects
    ///   NODEKIT_HARBOR_PASSWORD — optional, paired with NODEKIT_HARBOR_USER
    /// </summary>
    internal sealed class HarborImageDigestResolver : IImageDigestResolver, IDisposable
    {
        private readonly string _harborHost;
        private readonly string _baseUrl;
        private readonly HttpClient _httpClient;

        private HarborImageDigestResolver(string harborHost, string baseUrl, HttpClient httpClient)
        {
            _harborHost = harborHost;
            _baseUrl = baseUrl;
            _httpClient = httpClient;
        }

        internal static HarborImageDigestResolver CreateForTest(string harborUrl, HttpMessageHandler handler)
        {
            var baseUrl = harborUrl.TrimEnd('/');
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.v2+json"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.manifest.v1+json"));
            var host = ExtractHost(baseUrl);
            return new HarborImageDigestResolver(host, baseUrl, client);
        }

        public static HarborImageDigestResolver? TryCreate()
        {
            var rawUrl = Environment.GetEnvironmentVariable("NODEKIT_HARBOR_URL");
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return null;
            }

            var baseUrl = rawUrl.TrimEnd('/');

            X509Certificate2? customCa = null;
            var caCertPath = Environment.GetEnvironmentVariable("NODEKIT_HARBOR_CA_CERT");
            if (!string.IsNullOrWhiteSpace(caCertPath) && File.Exists(caCertPath))
            {
                // CreateFromPemFile()은 개인키를 요구한다 — CA 신뢰 전용 인증서는
                // 개인키가 없는 게 정상이므로 인증서 전용 로더를 사용해야 한다.
                customCa = X509CertificateLoader.LoadCertificateFromFile(caCertPath);
            }

            var handler = new HttpClientHandler();
            if (customCa != null)
            {
                var pinnedCa = customCa;
                handler.ServerCertificateCustomValidationCallback = (_, cert, chain, errors) =>
                {
                    if (errors == SslPolicyErrors.None)
                    {
                        return true;
                    }

                    if (cert is null || chain is null)
                    {
                        return false;
                    }

                    chain.ChainPolicy.ExtraStore.Add(pinnedCa);
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return chain.Build(cert);
                };
            }

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.v2+json"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.manifest.v1+json"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.list.v2+json"));

            var user = Environment.GetEnvironmentVariable("NODEKIT_HARBOR_USER");
            var pass = Environment.GetEnvironmentVariable("NODEKIT_HARBOR_PASSWORD");
            if (!string.IsNullOrWhiteSpace(user) && pass is not null)
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            var host = ExtractHost(baseUrl);
            return new HarborImageDigestResolver(host, baseUrl, client);
        }

        public async Task<ImageDigestResolutionResult> ResolveAsync(
            string imageUri,
            CancellationToken cancellationToken)
        {
            if (!TryParseImageUri(imageUri, out var host, out var repository, out var reference))
            {
                return ImageDigestResolutionResult.InvalidReference("이미지 주소 형식을 인식할 수 없습니다.");
            }

            if (!string.Equals(host, _harborHost, StringComparison.OrdinalIgnoreCase))
            {
                return ImageDigestResolutionResult.Unsupported(
                    $"구성된 Harbor({_harborHost})의 이미지가 아닙니다. 수동으로 digest를 입력하세요.");
            }

            var manifestUrl = $"{_baseUrl}/v2/{repository}/manifests/{reference}";

            try
            {
                using var response = await _httpClient.GetAsync(manifestUrl, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return ImageDigestResolutionResult.AuthenticationRequired(
                        "NODEKIT_HARBOR_USER / NODEKIT_HARBOR_PASSWORD 환경변수를 설정하세요.");
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return ImageDigestResolutionResult.NotFound(
                        $"이미지를 찾을 수 없습니다: {repository}:{reference}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return ImageDigestResolutionResult.NetworkUnavailable(
                        $"Harbor가 HTTP {(int)response.StatusCode}를 반환했습니다.");
                }

                if (response.Headers.TryGetValues("Docker-Content-Digest", out var values))
                {
                    var digest = values.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(digest))
                    {
                        return ImageDigestResolutionResult.Resolved(digest);
                    }
                }

                return ImageDigestResolutionResult.NotFound("응답에 digest 헤더가 없습니다.");
            }
            catch (HttpRequestException ex)
            {
                return ImageDigestResolutionResult.NetworkUnavailable(
                    $"Harbor에 연결할 수 없습니다: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return ImageDigestResolutionResult.NetworkUnavailable("요청 시간이 초과되었습니다.");
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private static string ExtractHost(string url)
        {
            var uri = new Uri(url);
            return uri.Host + (uri.IsDefaultPort ? string.Empty : $":{uri.Port}");
        }

        // Parses "host/path:tag" or "host:port/path:tag".
        // Returns false for refs that already contain '@' (already have digest)
        // or refs without a registry host (no '.'/':' in first component).
        private static bool TryParseImageUri(string imageUri, out string host, out string repository, out string reference)
        {
            host = string.Empty;
            repository = string.Empty;
            reference = string.Empty;

            if (string.IsNullOrWhiteSpace(imageUri) || imageUri.Contains('@'))
            {
                return false;
            }

            var slashIdx = imageUri.IndexOf('/');
            if (slashIdx < 0)
            {
                return false;
            }

            var firstComponent = imageUri[..slashIdx];
            if (!firstComponent.Contains('.') && !firstComponent.Contains(':'))
            {
                return false;
            }

            host = firstComponent;
            var rest = imageUri[(slashIdx + 1)..];

            var colonIdx = rest.LastIndexOf(':');
            if (colonIdx < 0)
            {
                repository = rest;
                reference = "latest";
            }
            else
            {
                repository = rest[..colonIdx];
                reference = rest[(colonIdx + 1)..];
            }

            return !string.IsNullOrWhiteSpace(repository) && !string.IsNullOrWhiteSpace(reference);
        }
    }
}
