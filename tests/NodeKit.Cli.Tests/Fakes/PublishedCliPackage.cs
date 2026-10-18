using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace NodeKit.Cli.Tests.Fakes
{
    /// <summary>
    /// P10.executable (S4-04-A–F): scripts/package-cli.sh가 만든 실제 CLI package를 repository 밖 임시 디렉터리에
    /// 풀고, 그 안의 command alias <c>nodekit</c>를 별도 OS process로 실행한다. package 위치는
    /// <see cref="PackageDirVariable"/>(archive·.sha256·sidecar manifest가 있는 디렉터리)로 받는다. 값이 없으면
    /// 테스트는 skip되고, CI의 test report gate가 이 class의 skip을 실패로 본다. 테스트 process 안의 CliApp이나
    /// 주입 client를 쓰지 않으므로 Program/config/gRPC 배선 전체를 지난다.
    /// </summary>
    internal sealed class PublishedCliPackage
    {
        public const string PackageDirVariable = "NODEKIT_CLI_PACKAGE_DIR";

        /// <summary>값이 있으면 각 실행의 args/exit/stdout/stderr와 package sha256을 이 디렉터리의 JSONL에 덧붙인다.</summary>
        public const string TranscriptDirVariable = "NODEKIT_CLI_ACCEPTANCE_OUT";

        private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(60);
        private static readonly object _transcriptLock = new();
        private static readonly Lazy<PublishedCliPackage?> _instance = new(Load);

        private PublishedCliPackage(string archivePath, string archiveSha256, string installDir, string sourceSha)
        {
            ArchivePath = archivePath;
            ArchiveSha256 = archiveSha256;
            InstallDir = installDir;
            SourceSha = sourceSha;
        }

        public string ArchivePath { get; }

        /// <summary>archive 파일의 sha256(hex). .sha256 파일과 일치함을 확인한 값이다.</summary>
        public string ArchiveSha256 { get; }

        /// <summary>archive를 푼 최상위 디렉터리(repository 밖 임시 경로).</summary>
        public string InstallDir { get; }

        /// <summary>cli-manifest.json의 source.sha.</summary>
        public string SourceSha { get; }

        public string Command => Path.Join(InstallDir, "nodekit");

        /// <summary>package가 지정되지 않았으면 테스트를 skip한다.</summary>
        public static PublishedCliPackage Require() =>
            _instance.Value ?? throw SkipException();

        private static Exception SkipException()
        {
            Assert.Skip($"{PackageDirVariable} 미설정 — published CLI package subprocess 인수 스킵");
            return new InvalidOperationException("unreachable");
        }

        /// <summary>
        /// package의 nodekit을 <paramref name="workingDirectory"/>에서 실행한다. 부모 환경의 NODEKIT_NODEVAULT_URL은
        /// 지워서 명시한 --url/env만 쓰게 한다. timeout이면 process tree를 끝내고 실패한다.
        /// </summary>
        public PublishedCliResult Run(
            string workingDirectory,
            IEnumerable<string> args,
            string? stdin = null,
            IReadOnlyDictionary<string, string>? environment = null,
            TimeSpan? timeout = null)
        {
            var argList = args.ToList();
            var start = new ProcessStartInfo(Command)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in argList)
            {
                start.ArgumentList.Add(arg);
            }

            start.Environment.Remove("NODEKIT_NODEVAULT_URL");
            start.Environment["NO_COLOR"] = "1";
            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                {
                    start.Environment[key] = value;
                }
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException($"{Command}를 시작하지 못했습니다.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
            }

            process.StandardInput.Close();

            var limit = timeout ?? _defaultTimeout;
            if (!process.WaitForExit(limit))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                Assert.Fail($"nodekit {string.Join(' ', argList)}가 {limit.TotalSeconds}s 안에 끝나지 않았습니다.");
            }

            process.WaitForExit();
            var result = new PublishedCliResult(process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
            AppendTranscript(workingDirectory, argList, result);
            return result;
        }

        private void AppendTranscript(string workingDirectory, List<string> args, PublishedCliResult result)
        {
            var dir = Environment.GetEnvironmentVariable(TranscriptDirVariable);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var line = JsonSerializer.Serialize(new
            {
                package_sha256 = ArchiveSha256,
                source_sha = SourceSha,
                cwd = workingDirectory,
                args,
                exit = result.Exit,
                stdout = result.Stdout,
                stderr = result.Stderr,
            });
            lock (_transcriptLock)
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Join(dir, "published-cli-transcript.jsonl"), line + "\n");
            }
        }

        private static PublishedCliPackage? Load()
        {
            var packageDir = Environment.GetEnvironmentVariable(PackageDirVariable);
            if (string.IsNullOrEmpty(packageDir))
            {
                return null;
            }

            var archive = Assert.Single(Directory.GetFiles(packageDir, "nodekit-cli-*-linux-x64.tar.gz"));
            var expected = File.ReadAllText(archive + ".sha256").Split(' ', 2)[0].Trim();
            string actual;
            using (var stream = File.OpenRead(archive))
            {
                actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            }

            Assert.Equal(expected, actual);

            var root = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "nodekit-published-cli-" + Guid.NewGuid())).FullName;
            var tar = Process.Start(new ProcessStartInfo("tar") { ArgumentList = { "-xzf", archive, "-C", root }, UseShellExecute = false })
                ?? throw new InvalidOperationException("tar를 시작하지 못했습니다.");
            tar.WaitForExit();
            Assert.Equal(0, tar.ExitCode);

            var installDir = Assert.Single(Directory.GetDirectories(root));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Join(installDir, "cli-manifest.json")));
            var sourceSha = manifest.RootElement.GetProperty("source").GetProperty("sha").GetString() ?? string.Empty;
            return new PublishedCliPackage(archive, actual, installDir, sourceSha);
        }
    }

    internal sealed record PublishedCliResult(int Exit, string Stdout, string Stderr);
}
