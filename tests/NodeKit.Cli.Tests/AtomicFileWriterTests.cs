using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P02.writer_foundation — AtomicFileWriter 단독 검수 (S4-03-G의 공통 저장
    /// 경계, cli-acceptance-contract.json supportProfile). caller별 exit 전달은
    /// P02.writer_caller에서 검수한다.
    /// </summary>
    public class AtomicFileWriterTests : IDisposable
    {
        private static readonly byte[] _oldBytes = Encoding.UTF8.GetBytes("{\"marker\":\"old-complete\"}\n");
        private static readonly byte[] _newBytes = Encoding.UTF8.GetBytes("{\"marker\":\"new-complete\",\"pad\":\"" + new string('x', 64 * 1024) + "\"}\n");

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-atomic-tests-" + Guid.NewGuid());

        public AtomicFileWriterTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_workDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(_workDir, recursive: true);
        }

        private string Target(string name = "recipe.json") => Path.Join(_workDir, name);

        private string[] TempFiles() =>
            Directory.GetFiles(_workDir, "*" + AtomicFileWriter.TempSuffix);

        // ── 정상 commit ────────────────────────────────────────────────────────────

        [Fact]
        public void Write_NewTarget_CommitsExactBytes_AndLeavesNoTemp()
        {
            var target = Target();

            var result = AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);

            Assert.Equal(AtomicWriteOutcome.Committed, result.Outcome);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(_newBytes, File.ReadAllBytes(target));
            Assert.Empty(TempFiles());
        }

        [Fact]
        public void Write_ExistingTarget_ReplacedByCompleteNewFile()
        {
            var target = Target();
            File.WriteAllBytes(target, _oldBytes);

            var result = AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);

            Assert.Equal(AtomicWriteOutcome.Committed, result.Outcome);
            Assert.Equal(_newBytes, File.ReadAllBytes(target));
            Assert.Empty(TempFiles());
        }

        [Fact]
        public void Write_String_UsesUtf8WithoutBom_LikeWriteAllText()
        {
            var target = Target();
            const string content = "{\"ToolName\":\"한글-도구\"}";

            AtomicFileWriter.Write(target, content, TestContext.Current.CancellationToken);

            Assert.Equal(new UTF8Encoding(false).GetBytes(content), File.ReadAllBytes(target));
        }

        [Fact]
        public void Write_ExistingTarget_KeepsUnixFileMode()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Skip("Unix file mode 전용");
                return;
            }

            var target = Target();
            File.WriteAllBytes(target, _oldBytes);
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(target, mode);

            AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);

            Assert.Equal(mode, File.GetUnixFileMode(target));
        }

        // ── commit 전 장애: 기존 bytes 보존, exit 2 ──────────────────────────────────

        [Theory]
        [InlineData("TempCreate")]
        [InlineData("Write")]
        [InlineData("Flush")]
        [InlineData("Replace")]
        public void Write_IoFaultBeforeCommit_PreservesOldBytes_Exit2(string faultStageName)
        {
            var target = Target();
            File.WriteAllBytes(target, _oldBytes);

            var result = AtomicFileWriter.Write(target, _newBytes, CancellationToken.None, stage =>
            {
                if (stage == Enum.Parse<AtomicWriteStage>(faultStageName))
                {
                    throw new IOException($"injected fault at {stage}");
                }
            });

            Assert.Equal(AtomicWriteOutcome.Failed, result.Outcome);
            Assert.Equal(2, result.ExitCode);
            Assert.Equal(AtomicFileWriter.FailedCode, result.Code);
            Assert.Contains("기존 파일은 바뀌지 않았습니다", result.Message);
            Assert.Equal(_oldBytes, File.ReadAllBytes(target));
            Assert.Empty(TempFiles());
        }

        [Theory]
        [InlineData("TempCreate")]
        [InlineData("Write")]
        [InlineData("Flush")]
        [InlineData("Replace")]
        public void Write_IoFaultBeforeCommit_NewTarget_LeavesNoFinalFile(string faultStageName)
        {
            var target = Target();

            var result = AtomicFileWriter.Write(target, _newBytes, CancellationToken.None, stage =>
            {
                if (stage == Enum.Parse<AtomicWriteStage>(faultStageName))
                {
                    throw new IOException($"injected fault at {stage}");
                }
            });

            Assert.Equal(2, result.ExitCode);
            Assert.False(File.Exists(target));
            Assert.Empty(TempFiles());
        }

        [Fact]
        public void Write_PermissionFaultBeforeCommit_PreservesOldBytes_Exit2()
        {
            var target = Target();
            File.WriteAllBytes(target, _oldBytes);

            var result = AtomicFileWriter.Write(target, _newBytes, CancellationToken.None, stage =>
            {
                if (stage == AtomicWriteStage.Write)
                {
                    throw new UnauthorizedAccessException("injected permission fault");
                }
            });

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(AtomicFileWriter.FailedCode, result.Code);
            Assert.Contains("권한", result.Message);
            Assert.Equal(_oldBytes, File.ReadAllBytes(target));
            Assert.Empty(TempFiles());
        }

        // 강제 종료처럼 IO가 아닌 예외로 중단돼도 rename 전이면 최종 경로는 이전
        // 완전한 파일이다. (예외는 호출자에게 그대로 전달된다.)
        [Theory]
        [InlineData("Write")]
        [InlineData("Flush")]
        [InlineData("Replace")]
        public void Write_AbortBeforeReplace_FinalPathIsOldCompleteFile(string abortStageName)
        {
            var target = Target();
            File.WriteAllBytes(target, _oldBytes);

            Assert.Throws<InvalidOperationException>(() =>
                AtomicFileWriter.Write(target, _newBytes, CancellationToken.None, stage =>
                {
                    if (stage == Enum.Parse<AtomicWriteStage>(abortStageName))
                    {
                        throw new InvalidOperationException("simulated abort");
                    }
                }));

            Assert.Equal(_oldBytes, File.ReadAllBytes(target));
        }

        // ── 취소: commit 전이면 130, 기존 bytes 보존 ────────────────────────────────

        [Fact]
        public void Write_CancelledBeforeStart_Exit130_PreservesOldBytes()
        {
            var target = Target();
            File.WriteAllBytes(target, _oldBytes);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = AtomicFileWriter.Write(target, _newBytes, cts.Token);

            Assert.Equal(AtomicWriteOutcome.Cancelled, result.Outcome);
            Assert.Equal(130, result.ExitCode);
            Assert.Equal(AtomicFileWriter.CancelledCode, result.Code);
            Assert.Equal(_oldBytes, File.ReadAllBytes(target));
            Assert.Empty(TempFiles());
        }

        [Fact]
        public void Write_CancelledAfterFlushBeforeReplace_Exit130_PreservesOldBytes()
        {
            var target = Target();
            File.WriteAllBytes(target, _oldBytes);
            using var cts = new CancellationTokenSource();

            var result = AtomicFileWriter.Write(target, _newBytes, cts.Token, stage =>
            {
                if (stage == AtomicWriteStage.Flush)
                {
                    cts.Cancel();
                }
            });

            Assert.Equal(130, result.ExitCode);
            Assert.Equal(_oldBytes, File.ReadAllBytes(target));
            Assert.Empty(TempFiles());
        }

        // ── 지원하지 않는 대상: 덮어쓰기 전 exit 2 ──────────────────────────────────

        [Fact]
        public void Write_TargetIsDirectory_Exit2_Unsupported_DirectoryUntouched()
        {
            var target = Target("as-dir");
            Directory.CreateDirectory(target);
            File.WriteAllBytes(Path.Join(target, "inner.txt"), _oldBytes);

            var result = AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(AtomicFileWriter.TargetUnsupportedCode, result.Code);
            Assert.True(Directory.Exists(target));
            Assert.Equal(_oldBytes, File.ReadAllBytes(Path.Join(target, "inner.txt")));
            Assert.Empty(TempFiles());
        }

        [Fact]
        public void Write_ParentMissing_Exit2_DoesNotCreateDirectory()
        {
            var parent = Path.Join(_workDir, "missing");
            var target = Path.Join(parent, "recipe.json");

            var result = AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(AtomicFileWriter.ParentMissingCode, result.Code);
            Assert.False(Directory.Exists(parent));
        }

        [Fact]
        public void Write_TargetIsSymlink_Exit2_LinkAndPointeeUntouched()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Skip("symlink 생성 권한이 Windows 환경마다 다름");
            }

            var pointee = Target("real.json");
            File.WriteAllBytes(pointee, _oldBytes);
            var link = Target("link.json");
            File.CreateSymbolicLink(link, pointee);

            var result = AtomicFileWriter.Write(link, _newBytes, TestContext.Current.CancellationToken);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(AtomicFileWriter.TargetUnsupportedCode, result.Code);
            Assert.NotNull(new FileInfo(link).LinkTarget);
            Assert.Equal(_oldBytes, File.ReadAllBytes(pointee));
        }

        [Fact]
        public void Write_DirectoryNotWritable_Exit2_PreservesOldBytes()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Skip("Unix 권한 검사 전용");
                return;
            }

            if (Environment.UserName == "root")
            {
                Assert.Skip("root는 디렉터리 쓰기 권한 제한을 무시함");
            }

            var target = Target();
            File.WriteAllBytes(target, _oldBytes);
            File.SetUnixFileMode(_workDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var result = AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(AtomicFileWriter.FailedCode, result.Code);
            Assert.Equal(_oldBytes, File.ReadAllBytes(target));
        }

        // ── writer 배제 ────────────────────────────────────────────────────────────

        [Fact]
        public void Write_WhileAnotherWriterHoldsLock_Exit2_Locked_PreservesOldBytes()
        {
            var target = Target();
            File.WriteAllBytes(target, _oldBytes);
            var lockPath = AtomicFileWriter.LockPath(_workDir, "recipe.json");

            using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var blocked = AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);

                Assert.Equal(2, blocked.ExitCode);
                Assert.Equal(AtomicFileWriter.LockedCode, blocked.Code);
                Assert.Equal(_oldBytes, File.ReadAllBytes(target));
            }

            var after = AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);
            Assert.Equal(0, after.ExitCode);
            Assert.Equal(_newBytes, File.ReadAllBytes(target));
        }

        // 다른 OS 프로세스(flock(1))가 잡은 잠금도 배제되는지 실제 프로세스로 확인.
        [Fact]
        public void Write_WhileOtherProcessHoldsLock_Exit2_Locked()
        {
            if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/flock"))
            {
                Assert.Skip("Linux flock(1) 전용");
            }

            var target = Target();
            File.WriteAllBytes(target, _oldBytes);
            var lockPath = AtomicFileWriter.LockPath(_workDir, "recipe.json");

            var psi = new ProcessStartInfo("/usr/bin/flock")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            // -o: 잠금 fd를 자식 명령에 넘기지 않는다. 그래야 flock 프로세스가
            // 끝나는 즉시(WaitForExit) 잠금이 풀린다.
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(lockPath);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("echo locked; sleep 30");

            using var holder = Process.Start(psi)!;
            try
            {
                Assert.Equal("locked", holder.StandardOutput.ReadLine());

                var blocked = AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);

                Assert.Equal(AtomicFileWriter.LockedCode, blocked.Code);
                Assert.Equal(_oldBytes, File.ReadAllBytes(target));
            }
            finally
            {
                holder.Kill(entireProcessTree: true);
                holder.WaitForExit();
            }

            Assert.Equal(0, AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken).ExitCode);
            Assert.Equal(_newBytes, File.ReadAllBytes(target));
        }

        // ── 강제 종료 잔여물 복구 ──────────────────────────────────────────────────

        // rename 전에 강제 종료된 writer는 부분 임시 파일만 남긴다. 최종 경로는
        // 이전 완전한 파일이고, 다음 writer가 잠금을 잡은 뒤 그 잔여물을 지운다.
        [Fact]
        public void Write_AfterKilledWriter_RemovesStaleTemp_OnlyForSameTarget()
        {
            var target = Target("a.json");
            File.WriteAllBytes(target, _oldBytes);
            var staleSame = Path.Join(_workDir, ".a.json." + Guid.NewGuid().ToString("N") + AtomicFileWriter.TempSuffix);
            var otherTarget = Path.Join(_workDir, ".a.json.bak." + Guid.NewGuid().ToString("N") + AtomicFileWriter.TempSuffix);
            File.WriteAllBytes(staleSame, _newBytes.Take(10).ToArray());
            File.WriteAllBytes(otherTarget, _newBytes.Take(10).ToArray());

            Assert.Equal(_oldBytes, File.ReadAllBytes(target));

            var result = AtomicFileWriter.Write(target, _newBytes, TestContext.Current.CancellationToken);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(_newBytes, File.ReadAllBytes(target));
            Assert.False(File.Exists(staleSame));
            Assert.True(File.Exists(otherTarget));
        }

        [Theory]
        [InlineData(".a.json.0123456789abcdef0123456789abcdef.nodekit-tmp", "a.json", true)]
        [InlineData(".a.json.bak.0123456789abcdef0123456789abcdef.nodekit-tmp", "a.json", false)]
        [InlineData(".a.json.0123456789ABCDEF0123456789abcdef.nodekit-tmp", "a.json", false)]
        [InlineData(".a.json.short.nodekit-tmp", "a.json", false)]
        [InlineData(".a.json.nodekit-lock", "a.json", false)]
        [InlineData(".a*.json.0123456789abcdef0123456789abcdef.nodekit-tmp", "a*.json", true)]
        public void IsTempFileFor_MatchesOnlyExactTempShape(string candidate, string fileName, bool expected)
        {
            Assert.Equal(expected, AtomicFileWriter.IsTempFileFor(candidate, fileName));
        }
    }
}
