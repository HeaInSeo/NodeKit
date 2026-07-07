using NodeKit.Policy;
using Xunit;

namespace NodeKit.Tests
{
    public class DockerfileParserTests
    {
        [Fact]
        public void Parse_WhenRunUsesHeredocSyntax_SkipsBodyLines()
        {
            var dockerfile = "FROM ubuntu:22.04\n" +
                "RUN <<EOF\n" +
                "apt-get update\n" +
                "apt-get install -y curl\n" +
                "EOF\n" +
                "COPY app/ /app/\n";

            var instructions = DockerfileParser.Parse(dockerfile);

            Assert.Equal(3, instructions.Count);
            Assert.Equal("FROM", instructions[0].Cmd);
            Assert.Equal("RUN", instructions[1].Cmd);
            Assert.Equal("COPY", instructions[2].Cmd);
        }

        [Fact]
        public void Parse_WhenHeredocUsesQuotedDelimiter_SkipsBodyLines()
        {
            var dockerfile = "FROM ubuntu:22.04\n" +
                "RUN <<'EOF'\n" +
                "echo hello\n" +
                "EOF\n";

            var instructions = DockerfileParser.Parse(dockerfile);

            Assert.Equal(2, instructions.Count);
            Assert.Equal("RUN", instructions[1].Cmd);
        }

        [Fact]
        public void Parse_WhenHeredocBodyContainsCopyKeyword_DoesNotEmitCopyInstruction()
        {
            var dockerfile = "FROM ubuntu:22.04\n" +
                "RUN <<EOF\n" +
                "COPY ../secret /app/secret\n" +
                "EOF\n";

            var instructions = DockerfileParser.Parse(dockerfile);

            Assert.DoesNotContain(instructions, i => i.Cmd == "COPY");
        }

        [Fact]
        public void Parse_WhenNoHeredocPresent_ParsesEveryLineAsAnInstruction()
        {
            var dockerfile = "FROM ubuntu:22.04\nRUN echo a\nRUN echo b\n";

            var instructions = DockerfileParser.Parse(dockerfile);

            Assert.Equal(3, instructions.Count);
        }

        // Review finding: whitespace-only splitting on ADD's JSON-array form
        // (e.g. ADD ["url", "dest"]) left the leading token as the literal
        // string "[\"url\"," — DockerfileStructureValidator's remote-source
        // check (source.StartsWith("https://")) never matched that, so a
        // remote ADD source silently bypassed L1-DOCKER-007.

        [Fact]
        public void Parse_WhenAddUsesJsonArraySyntax_ParsesEachElementSeparately()
        {
            var dockerfile = "FROM ubuntu:22.04\nADD [\"https://example.com/tool.tar.gz\", \"/tmp/tool.tar.gz\"]\n";

            var instructions = DockerfileParser.Parse(dockerfile);

            var add = instructions.Find(i => i.Cmd == "ADD");
            Assert.NotNull(add);
            Assert.Equal(new[] { "https://example.com/tool.tar.gz", "/tmp/tool.tar.gz" }, add!.Value);
        }

        [Fact]
        public void Parse_WhenCopyUsesJsonArraySyntaxWithFromFlag_KeepsFlagAndParsesArray()
        {
            var dockerfile = "FROM ubuntu:22.04\nCOPY --from=builder [\"/src/app\", \"/usr/local/bin/app\"]\n";

            var instructions = DockerfileParser.Parse(dockerfile);

            var copy = instructions.Find(i => i.Cmd == "COPY");
            Assert.NotNull(copy);
            Assert.Equal(new[] { "--from=builder", "/src/app", "/usr/local/bin/app" }, copy!.Value);
        }

        [Fact]
        public void Parse_WhenAddJsonArrayIsMalformed_FallsBackToWhitespaceSplit()
        {
            var dockerfile = "FROM ubuntu:22.04\nADD [\"not, valid, json app/ /app/\n";

            var instructions = DockerfileParser.Parse(dockerfile);

            var add = instructions.Find(i => i.Cmd == "ADD");
            Assert.NotNull(add);
            Assert.NotEmpty(add!.Value);
        }
    }
}
