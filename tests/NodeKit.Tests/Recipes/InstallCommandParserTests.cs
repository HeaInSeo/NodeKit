using System.Collections.Generic;
using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class InstallCommandParserTests
    {
        public static IEnumerable<object[]> ParsedCases() =>
        [
            // 완전 고정 (engine=version=build) — 경고 없음
            ["conda install -c bioconda -c conda-forge bwa=0.7.17=h84994c4_5 -y",
                "conda", new[] { "bioconda", "conda-forge" }, new[] { "bwa=0.7.17=h84994c4_5" }, 0],
            // 채널 복수, 패키지 복수, 완전 고정
            ["micromamba install -c bioconda -c conda-forge samtools=1.20=h50ea8bc_0 htslib=1.20=h5efdd21_1",
                "micromamba", new[] { "bioconda", "conda-forge" }, new[] { "samtools=1.20=h50ea8bc_0", "htslib=1.20=h5efdd21_1" }, 0],
            // --channel= 등호 형식
            ["conda install --channel=bioconda bwa=0.7.17=h84994c4_5",
                "conda", new[] { "bioconda" }, new[] { "bwa=0.7.17=h84994c4_5" }, 0],
            // -y / --yes 플래그 무시
            ["conda install -y -c bioconda bwa=0.7.17=h84994c4_5 --quiet",
                "conda", new[] { "bioconda" }, new[] { "bwa=0.7.17=h84994c4_5" }, 0],
            // -n envname 스킵
            ["conda install -n myenv -c bioconda bwa=0.7.17=h84994c4_5",
                "conda", new[] { "bioconda" }, new[] { "bwa=0.7.17=h84994c4_5" }, 0],
            // --name=envname 스킵
            ["conda install --name=myenv -c bioconda bwa=0.7.17=h84994c4_5",
                "conda", new[] { "bioconda" }, new[] { "bwa=0.7.17=h84994c4_5" }, 0],
            // --prefix 스킵
            ["micromamba install -p /opt/env -c conda-forge samtools=1.20=h50ea8bc_0",
                "micromamba", new[] { "conda-forge" }, new[] { "samtools=1.20=h50ea8bc_0" }, 0],
            // micromamba 단일 채널
            ["micromamba install -c bioconda bwa=0.7.17=h84994c4_5",
                "micromamba", new[] { "bioconda" }, new[] { "bwa=0.7.17=h84994c4_5" }, 0],
        ];

        [Theory, MemberData(nameof(ParsedCases))]
        public void Parsed_ReturnsExpectedEngineChannelsPackages(
            string command, string engine, string[] channels, string[] packages, int warnCount)
        {
            var result = InstallCommandParser.Parse(command);

            Assert.Equal(InstallCommandParseStatus.Parsed, result.Status);
            Assert.Equal(engine, result.PackageEngine);
            Assert.Equal(channels, result.Channels);
            Assert.Equal(packages, result.Packages);
            Assert.Empty(result.Missing);
            Assert.Equal(warnCount, result.Warnings.Count);
        }

        public static IEnumerable<object[]> ParsedWithWarningCases() =>
        [
            // 버전만 고정 (name=version, build string 없음) → build string 경고
            ["conda install -c bioconda bwa=0.7.17",
                "conda", "build string이 고정되어 있지 않습니다"],
            // 이름만 (버전 없음) → 버전 경고
            ["conda install -c bioconda bwa",
                "conda", "버전이 고정되어 있지 않습니다"],
            // 복수 패키지 중 일부만 버전 고정
            ["micromamba install -c bioconda bwa=0.7.17=h84994c4_5 samtools",
                "micromamba", "버전이 고정되어 있지 않습니다"],
        ];

        [Theory, MemberData(nameof(ParsedWithWarningCases))]
        public void Parsed_WithVersionWarning_StatusIsParsed(string command, string engine, string warningSubstring)
        {
            var result = InstallCommandParser.Parse(command);

            Assert.Equal(InstallCommandParseStatus.Parsed, result.Status);
            Assert.Equal(engine, result.PackageEngine);
            Assert.Empty(result.Missing);
            Assert.Contains(result.Warnings, w => w.Contains(warningSubstring));
        }

        public static IEnumerable<object[]> PartiallyParsedCases() =>
        [
            // 채널 없음 → Missing=[Channels]
            ["conda install bwa=0.7.17=h84994c4_5",
                "conda", new[] { "Channels" }],
            // 패키지 없음 → Missing=[Packages]
            ["conda install -c bioconda",
                "conda", new[] { "Packages" }],
            // 채널도 없고 패키지도 없음 → Missing=[Packages, Channels]
            ["conda install -y",
                "conda", new[] { "Packages", "Channels" }],
        ];

        [Theory, MemberData(nameof(PartiallyParsedCases))]
        public void PartiallyParsed_MissingFieldsReturned(string command, string engine, string[] missing)
        {
            var result = InstallCommandParser.Parse(command);

            Assert.Equal(InstallCommandParseStatus.PartiallyParsed, result.Status);
            Assert.Equal(engine, result.PackageEngine);
            Assert.Equal(missing, result.Missing);
        }

        [Fact]
        public void CondaCreate_IsPartiallyParsed_WithSemanticWarning()
        {
            var result = InstallCommandParser.Parse("conda create -c bioconda -n myenv bwa=0.7.17=h84994c4_5");

            Assert.Equal(InstallCommandParseStatus.PartiallyParsed, result.Status);
            Assert.Equal("conda", result.PackageEngine);
            Assert.Contains(result.Warnings, w => w.Contains("conda create"));
        }

        [Fact]
        public void MicromambaCreate_IsPartiallyParsed_WithSemanticWarning()
        {
            var result = InstallCommandParser.Parse("micromamba create -c conda-forge python=3.11=h1234567_0");

            Assert.Equal(InstallCommandParseStatus.PartiallyParsed, result.Status);
            Assert.Contains(result.Warnings, w => w.Contains("micromamba create"));
        }

        public static IEnumerable<object[]> FailedCases() =>
        [
            // 지원하지 않는 패키지 관리자
            ["pip install bwa==0.7.17"],
            ["apt-get install -y bwa"],
            ["brew install bwa"],
            ["mamba install -c bioconda bwa"],
            // 래핑된 명령
            ["/bin/bash -c 'conda install bwa'"],
            ["bash -c \"conda install bwa\""],
            // 지원하지 않는 subcommand
            ["conda env create -f environment.yml"],
            ["conda update bwa"],
            ["conda remove bwa"],
            // subcommand 없음
            ["conda"],
            ["micromamba"],
            // 빈 문자열 / 공백
            [""],
            ["   "],
            // git
            ["git clone https://github.com/lh3/bwa.git && make"],
        ];

        [Theory, MemberData(nameof(FailedCases))]
        public void Failed_UnsupportedCommandOrEmpty(string command)
        {
            var result = InstallCommandParser.Parse(command);

            Assert.Equal(InstallCommandParseStatus.Failed, result.Status);
            Assert.Null(result.PackageEngine);
            Assert.Empty(result.Channels);
            Assert.Empty(result.Packages);
            Assert.Empty(result.Missing);
            Assert.NotEmpty(result.Warnings);
        }

        [Fact]
        public void Failed_EmptyString_OriginalCommandIsNull()
        {
            var result = InstallCommandParser.Parse("");

            Assert.Null(result.OriginalCommand);
        }

        [Fact]
        public void Failed_NonEmpty_OriginalCommandPreserved()
        {
            const string cmd = "pip install bwa==0.7.17";
            var result = InstallCommandParser.Parse(cmd);

            Assert.Equal(cmd, result.OriginalCommand);
        }

        [Fact]
        public void Parsed_OriginalCommandPreserved()
        {
            const string cmd = "conda install -c bioconda bwa=0.7.17=h84994c4_5";
            var result = InstallCommandParser.Parse(cmd);

            Assert.Equal(cmd, result.OriginalCommand);
        }

        [Fact]
        public void UnknownDashFlag_IsSkipped_DoesNotBecomePackage()
        {
            var result = InstallCommandParser.Parse(
                "conda install -c bioconda --experimental bwa=0.7.17=h84994c4_5");

            Assert.Equal(InstallCommandParseStatus.Parsed, result.Status);
            Assert.Equal(new[] { "bwa=0.7.17=h84994c4_5" }, result.Packages);
        }

        [Fact]
        public void ChannelLongFormEqualsSign_ParsedCorrectly()
        {
            var result = InstallCommandParser.Parse(
                "conda install --channel=bioconda --channel=conda-forge bwa=0.7.17=h84994c4_5");

            Assert.Equal(new[] { "bioconda", "conda-forge" }, result.Channels);
            Assert.Equal(InstallCommandParseStatus.Parsed, result.Status);
        }
    }
}
