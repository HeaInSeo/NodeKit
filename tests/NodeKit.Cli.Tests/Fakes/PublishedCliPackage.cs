using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
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

        /// <summary>값이 있으면 각 실행의 <see cref="PublishedCliResult.Transcript"/>를 이 디렉터리의 JSONL에 덧붙인다.</summary>
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

        /// <summary>
        /// P10.portability (S4-04-G): <see cref="Require"/>와 같은 archive를 <paramref name="parentDirectory"/>에 새로 풀어
        /// 독립 설치 하나를 만든다. archive hash를 다시 확인하며, 다른 설치와 파일을 공유하지 않으므로 호출자가 그
        /// 설치를 지워도 다른 설치는 그대로 동작한다.
        /// </summary>
        public static PublishedCliPackage InstallInto(string parentDirectory)
        {
            var shared = Require();
            Assert.Equal(shared.ArchiveSha256, Sha256File(shared.ArchivePath));
            Directory.CreateDirectory(parentDirectory);
            Assert.Empty(Directory.GetFileSystemEntries(parentDirectory));
            return Extract(shared.ArchivePath, shared.ArchiveSha256, parentDirectory);
        }

        private static Exception SkipException()
        {
            Assert.Skip($"{PackageDirVariable} 미설정 — published CLI package subprocess 인수 스킵");
            return new InvalidOperationException("unreachable");
        }

        /// <summary>
        /// package의 nodekit을 <paramref name="workingDirectory"/>에서 실행한다. 부모 환경의 NODEKIT_NODEVAULT_URL과
        /// NO_COLOR는 지워서 명시한 --url/env만 쓰게 한다. timeout이면 process tree를 끝내고 실패한다. <paramref name="host"/>가
        /// 있으면 이 실행 동안 그 loopback fake가 받은 RPC를 transcript에 같이 남긴다. <paramref name="recordDirectory"/>는
        /// transcript에 hash를 남길 <c>.nodekit</c>의 부모 디렉터리다(기본 CWD). Recipe를 CWD 밖에서 실행하면 기록은
        /// Recipe 옆에 생기므로 그 디렉터리를 준다.
        /// </summary>
        public PublishedCliResult Run(
            string workingDirectory,
            IEnumerable<string> args,
            string? stdin = null,
            IReadOnlyDictionary<string, string>? environment = null,
            TimeSpan? timeout = null,
            LoopbackGrpcFixtureHost? host = null,
            string? recordDirectory = null)
        {
            var argList = args.ToList();
            var buildMark = host?.Fake.CallCount ?? 0;
            var registerMark = host?.Registry.RegisterRequestBytes.Count ?? 0;
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
            // NO_COLOR를 강제하지 않는다. 강제하면 redirected stdout/stderr에서 ANSI를 끄는 CLI 자체의 판단을
            // 가린다. 부모 환경의 값도 지워 각 테스트가 명시한 environment만 남게 한다.
            start.Environment.Remove("NO_COLOR");
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
            var exit = process.ExitCode;
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            // S4-04-F: 같은 package hash에 이 실행의 입력·출력·fake RPC·로컬 파일 hash를 한 record로 묶는다.
            var rpc = new List<TranscriptRpc>();
            if (host is not null)
            {
                rpc.AddRange(host.Fake.CallsSince(buildMark).Select(c => TranscriptRpc.Of(c.Method, c.Request)));
                for (var i = registerMark; i < host.Registry.RegisterRequestBytes.Count; i++)
                {
                    rpc.Add(TranscriptRpc.Of("Register", host.Registry.RegisterRequests[i], host.Registry.RegisterRequestBytes[i]));
                }
            }

            var transcript = JsonSerializer.Serialize(new
            {
                package_sha256 = ArchiveSha256,
                source_sha = SourceSha,
                install_dir = InstallDir,
                cwd = workingDirectory,
                args = argList,
                stdin,
                env = environment ?? new Dictionary<string, string>(),
                exit,
                stdout,
                stderr,
                rpc = rpc.Select(r => new { method = r.Method, request_type = r.RequestType, request_sha256 = r.RequestSha256, request = r.RequestJson }),
                record_dir = recordDirectory ?? workingDirectory,
                files = StateFileHashes(recordDirectory ?? workingDirectory),
            });
            AppendTranscript(transcript);
            return new PublishedCliResult(exit, stdout, stderr, transcript);
        }

        /// <summary><c>&lt;dir&gt;/.nodekit</c> 아래 모든 파일(receipt·lock 포함)의 상대 경로와 sha256. 정렬해 결정적으로 남긴다.</summary>
        private static SortedDictionary<string, string> StateFileHashes(string directory)
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var root = Path.Join(directory, ".nodekit");
            if (!Directory.Exists(root))
            {
                return hashes;
            }

            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/');
                hashes[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            }

            return hashes;
        }

        private static void AppendTranscript(string line)
        {
            var dir = Environment.GetEnvironmentVariable(TranscriptDirVariable);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

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
            var actual = Sha256File(archive);
            Assert.Equal(expected, actual);

            var root = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "nodekit-published-cli-" + Guid.NewGuid())).FullName;
            return Extract(archive, actual, root);
        }

        private static PublishedCliPackage Extract(string archive, string archiveSha256, string root)
        {
            var tar = Process.Start(new ProcessStartInfo("tar") { ArgumentList = { "-xzf", archive, "-C", root }, UseShellExecute = false })
                ?? throw new InvalidOperationException("tar를 시작하지 못했습니다.");
            tar.WaitForExit();
            Assert.Equal(0, tar.ExitCode);

            var installDir = Assert.Single(Directory.GetDirectories(root));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Join(installDir, "cli-manifest.json")));
            var sourceSha = manifest.RootElement.GetProperty("source").GetProperty("sha").GetString() ?? string.Empty;
            return new PublishedCliPackage(archive, archiveSha256, installDir, sourceSha);
        }

        private static string Sha256File(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
    }

    /// <summary>
    /// published process 한 번의 결과. <see cref="Transcript"/>는 package sha256·source sha·cwd·args·stdin·env
    /// override·exit·stdout·stderr·fake RPC(순서, 요청 type, 요청 bytes sha256, 요청 JSON)·<c>.nodekit</c> 파일 hash를
    /// 담은 JSON 한 줄이며 <see cref="PublishedCliPackage.TranscriptDirVariable"/>의 JSONL에 그대로 덧붙는다.
    /// </summary>
    internal sealed record PublishedCliResult(int Exit, string Stdout, string Stderr, string Transcript);

    /// <summary>
    /// fake가 받은 RPC 한 건. Resolve/Submit/Watch의 bytes는 fake가 역직렬화한 message를 다시 encode한 값이고,
    /// Register는 FakeToolRegistryService가 받은 직후 남긴 bytes다.
    /// </summary>
    internal sealed record TranscriptRpc(string Method, string RequestType, string RequestSha256, string RequestJson)
    {
        public static TranscriptRpc Of(string method, IMessage request, byte[]? bytes = null) => new(
            method,
            request.Descriptor.FullName,
            Convert.ToHexStringLower(SHA256.HashData(bytes ?? request.ToByteArray())),
            JsonFormatter.Default.Format(request));
    }
}
