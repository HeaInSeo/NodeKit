using NodeKit.Policy;
using Xunit;

namespace NodeKit.Tests
{
    public class OpaWasmHelpersTests
    {
        [Fact]
        public void ParseResult_WhenResultArrayEmpty_ReturnsNoViolations()
        {
            var result = OpaWasmHelpers.ParseResult("""[{"result": []}]""");

            Assert.True(result.IsAllowed);
            Assert.Empty(result.Violations);
        }

        [Fact]
        public void ParseResult_WhenResultHasMessages_ReturnsViolations()
        {
            var result = OpaWasmHelpers.ParseResult("""[{"result": ["DFM001: FROM은 하나여야 합니다"]}]""");

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "DFM001");
        }

        [Fact]
        public void ParseResult_WhenOuterArrayEmpty_ReturnsEmptyResultViolation()
        {
            // entrypoint 불일치 등으로 OPA가 어떤 result 항목도 만들지 못한 경우 —
            // 이를 "위반 없음"으로 오인하면 정책이 조용히 fail-open된다.
            var result = OpaWasmHelpers.ParseResult("[]");

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "WASM-EMPTY-RESULT");
        }

        [Fact]
        public void ParseResult_WhenEntryHasNoResultProperty_ReturnsEmptyResultViolation()
        {
            var result = OpaWasmHelpers.ParseResult("[{}]");

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "WASM-EMPTY-RESULT");
        }

        [Fact]
        public void ParseResult_WhenJsonMalformed_ReturnsParseErrorViolation()
        {
            var result = OpaWasmHelpers.ParseResult("not json");

            Assert.False(result.IsAllowed);
            Assert.Contains(result.Violations, v => v.RuleId == "WASM-PARSE-ERR");
        }
    }
}
