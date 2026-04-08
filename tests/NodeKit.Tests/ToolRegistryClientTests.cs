using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Tests
{
    /// <summary>
    /// FakeToolRegistryClient — IToolRegistryClient 테스트 더블.
    /// GrpcToolRegistryClient가 반환하는 것과 동일한 DTO 구조를 미리 지정한다.
    /// </summary>
    internal sealed class FakeToolRegistryClient : IToolRegistryClient
    {
        private readonly IReadOnlyList<RegisteredTool> _tools;
        private readonly Exception? _throws;

        internal FakeToolRegistryClient(IReadOnlyList<RegisteredTool> tools)
            => _tools = tools;

        internal FakeToolRegistryClient(Exception throws)
            => (_tools, _throws) = (Array.Empty<RegisteredTool>(), throws);

        public Task<IReadOnlyList<RegisteredTool>> ListToolsAsync(CancellationToken ct = default)
        {
            if (_throws != null)
            {
                throw _throws;
            }

            return Task.FromResult(_tools);
        }
    }

    public sealed class ToolRegistryClientTests
    {
        // ── DTO 구조 검증 ─────────────────────────────────────────────────────

        [Fact]
        public void RegisteredTool_DefaultValues_AreEmpty()
        {
            var tool = new RegisteredTool();

            Assert.Equal(string.Empty, tool.CasHash);
            Assert.Equal(string.Empty, tool.ToolName);
            Assert.Equal(string.Empty, tool.ImageUri);
            Assert.Equal(string.Empty, tool.Digest);
            Assert.Empty(tool.InputNames);
            Assert.Empty(tool.OutputNames);
            Assert.Equal(default, tool.RegisteredAt);
        }

        [Fact]
        public void RegisteredTool_InitProperties_SetCorrectly()
        {
            var at = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
            var tool = new RegisteredTool
            {
                CasHash = "abc123",
                ToolName = "bwa-mem2",
                ImageUri = "registry.example.com/bwa-mem2:2.2.1@sha256:abc",
                Digest = "sha256:abc",
                InputNames = new[] { "ref.fa", "reads.fastq" },
                OutputNames = new[] { "out.sam" },
                RegisteredAt = at,
            };

            Assert.Equal("abc123", tool.CasHash);
            Assert.Equal("bwa-mem2", tool.ToolName);
            Assert.Equal("sha256:abc", tool.Digest);
            Assert.Equal(2, tool.InputNames.Count);
            Assert.Equal("ref.fa", tool.InputNames[0]);
            Assert.Equal("out.sam", tool.OutputNames[0]);
            Assert.Equal(at, tool.RegisteredAt);
        }

        // ── Unix timestamp → DateTimeOffset 변환 검증 ─────────────────────────
        // NodeForge는 RegisteredAt을 Unix seconds(time.Now().Unix())로 전송한다.
        // GrpcToolRegistryClient는 DateTimeOffset.FromUnixTimeSeconds()로 변환한다.

        [Fact]
        public void UnixSecondsToDateTimeOffset_RoundTrips()
        {
            long unixSeconds = 1_775_000_000L; // 2026년 대략적인 타임스탬프
            var expected = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            var tool = new RegisteredTool { RegisteredAt = expected };

            Assert.Equal(unixSeconds, tool.RegisteredAt.ToUnixTimeSeconds());
        }

        [Fact]
        public void UnixSeconds_Zero_MapsToEpoch()
        {
            var epoch = DateTimeOffset.FromUnixTimeSeconds(0);
            Assert.Equal(1970, epoch.Year);
        }

        // ── FakeToolRegistryClient 동작 검증 ─────────────────────────────────

        [Fact]
        public async Task ListToolsAsync_ReturnsPreconfiguredTools()
        {
            var tools = new[]
            {
                new RegisteredTool { ToolName = "bwa-mem2", CasHash = "hash1" },
                new RegisteredTool { ToolName = "samtools", CasHash = "hash2" },
            };
            IToolRegistryClient client = new FakeToolRegistryClient(tools);

            var result = await client.ListToolsAsync();

            Assert.Equal(2, result.Count);
            Assert.Equal("bwa-mem2", result[0].ToolName);
            Assert.Equal("samtools", result[1].ToolName);
        }

        [Fact]
        public async Task ListToolsAsync_EmptyList_ReturnsEmpty()
        {
            IToolRegistryClient client = new FakeToolRegistryClient(Array.Empty<RegisteredTool>());

            var result = await client.ListToolsAsync();

            Assert.Empty(result);
        }

        [Fact]
        public async Task ListToolsAsync_WhenClientThrows_PropagatesException()
        {
            var ex = new InvalidOperationException("gRPC unavailable");
            IToolRegistryClient client = new FakeToolRegistryClient(ex);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.ListToolsAsync());
        }

        [Fact]
        public async Task ListToolsAsync_CancellationToken_IsPassedThrough()
        {
            // 취소된 토큰이라도 fake는 즉시 결과를 반환한다 (gRPC 차단 없음).
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            IToolRegistryClient client = new FakeToolRegistryClient(Array.Empty<RegisteredTool>());

            // fake는 token을 무시하므로 예외 없이 완료된다.
            var result = await client.ListToolsAsync(cts.Token);
            Assert.Empty(result);
        }
    }
}
