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
    }
}
