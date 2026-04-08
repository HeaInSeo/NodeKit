using System.IO;
using System.Linq;
using NodeKit.Policy;
using Xunit;

namespace NodeKit.Tests
{
    /// <summary>
    /// WasmPolicyChecker + dockguard.wasm 통합 테스트.
    /// 실제 .wasm 바이트를 로드하여 DFM001~DFM004 규칙 pass/block을 검증한다.
    /// </summary>
    public sealed class WasmPolicyCheckerTests
    {
        // 빌드 출력 디렉토리에 복사된 .wasm 경로
        private static readonly string WasmPath = Path.Combine(
            Path.GetDirectoryName(typeof(WasmPolicyCheckerTests).Assembly.Location)!,
            "assets",
            "policy",
            "dockguard.wasm");

        private static WasmPolicyChecker CreateChecker()
        {
            Assert.True(File.Exists(WasmPath), $"dockguard.wasm not found at: {WasmPath}");
            var bytes = File.ReadAllBytes(WasmPath);
            var bundle = new PolicyBundle(bytes, "test");
            return new WasmPolicyChecker(bundle);
        }

        // ─── 통과 케이스 ─────────────────────────────────────────────────────

        [Fact]
        public void Pass_ValidSingleStage()
        {
            // FROM 하나, AS builder, AS final 없음, COPY --from 없음 → 전체 통과
            const string dockerfile = """
                FROM ubuntu:22.04 AS builder
                RUN apt-get update && apt-get install -y python3
                COPY ./requirements.txt /app/requirements.txt
                """;

            using var checker = CreateChecker();
            var result = checker.Check(dockerfile);

            Assert.True(result.IsAllowed, "위반 없어야 함: " + string.Join("; ", result.Violations.Select(v => v.Message)));
        }

        // ─── DFM001: FROM은 반드시 하나 ──────────────────────────────────────

        [Fact]
        public void Dfm001_NoFrom_Blocked()
        {
            const string dockerfile = """
                RUN apt-get update
                COPY ./src /app
                """;

            using var checker = CreateChecker();
            var result = checker.Check(dockerfile);

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "DFM001");
        }

        [Fact]
        public void Dfm001_TwoFrom_Blocked()
        {
            const string dockerfile = """
                FROM ubuntu:22.04 AS builder
                RUN apt-get update
                FROM python:3.11 AS runner
                COPY --from=builder /app /app
                """;

            using var checker = CreateChecker();
            var result = checker.Check(dockerfile);

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "DFM001");
        }

        // ─── DFM002: FROM에 AS builder 필수 ──────────────────────────────────

        [Fact]
        public void Dfm002_FromWithoutAlias_Blocked()
        {
            // FROM이 하나지만 AS builder 없음
            const string dockerfile = """
                FROM ubuntu:22.04
                RUN apt-get update
                """;

            using var checker = CreateChecker();
            var result = checker.Check(dockerfile);

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "DFM002");
        }

        [Fact]
        public void Dfm002_FromWithWrongAlias_Blocked()
        {
            // AS builder가 아닌 다른 별칭
            const string dockerfile = """
                FROM ubuntu:22.04 AS base
                RUN apt-get update
                """;

            using var checker = CreateChecker();
            var result = checker.Check(dockerfile);

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "DFM002");
        }

        // ─── DFM003: FROM ... AS final 금지 ──────────────────────────────────

        [Fact]
        public void Dfm003_FromAsFinal_Blocked()
        {
            // AS final 사용 → DFM003
            const string dockerfile = """
                FROM ubuntu:22.04 AS final
                RUN apt-get update
                """;

            using var checker = CreateChecker();
            var result = checker.Check(dockerfile);

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "DFM003");
        }

        // ─── DFM004: COPY --from=builder 금지 ────────────────────────────────

        [Fact]
        public void Dfm004_CopyFromBuilder_Blocked()
        {
            // COPY --from=builder 사용 → DFM004
            const string dockerfile = """
                FROM ubuntu:22.04 AS builder
                RUN apt-get update
                COPY --from=builder /app/dist /final/app
                """;

            using var checker = CreateChecker();
            var result = checker.Check(dockerfile);

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "DFM004");
        }

        [Fact]
        public void Dfm004_CopyWithoutFrom_Passes()
        {
            // 일반 COPY는 DFM004에 걸리지 않음
            const string dockerfile = """
                FROM ubuntu:22.04 AS builder
                RUN apt-get update
                COPY ./src /app/src
                """;

            using var checker = CreateChecker();
            var result = checker.Check(dockerfile);

            Assert.True(result.IsAllowed, "위반 없어야 함: " + string.Join("; ", result.Violations.Select(v => v.Message)));
        }
    }
}
