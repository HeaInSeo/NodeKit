using System.Text;

namespace NodeKit.Cli
{
    /// <summary>원자 저장 결과 분류. ExitCode는 CLI 종료 코드로 그대로 쓰인다.</summary>
    internal enum AtomicWriteOutcome
    {
        Committed,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// 원자 저장 결과 하나. Committed가 아니면 최종 경로는 호출 전 상태
    /// 그대로다(기존 파일은 byte 단위로 보존, 새 대상이면 파일 없음).
    /// </summary>
    internal sealed record AtomicWriteResult(AtomicWriteOutcome Outcome, string? Code, string? Message)
    {
        public static readonly AtomicWriteResult Committed = new(AtomicWriteOutcome.Committed, null, null);

        public int ExitCode => Outcome switch
        {
            AtomicWriteOutcome.Committed => 0,
            AtomicWriteOutcome.Cancelled => 130,
            _ => 2,
        };
    }

    /// <summary>원자 저장 단계. 테스트가 단계 직전에 장애를 주입할 때 쓴다.</summary>
    internal enum AtomicWriteStage
    {
        TempCreate,
        Write,
        Flush,
        Replace,
    }

    /// <summary>
    /// create/validate/render 저장 경로가 공유할 원자 파일 저장 foundation
    /// (Fixtures/Contract/cli-acceptance-contract.json supportProfile, owner
    /// P02.writer_foundation). caller 전환은 P02.writer_caller에서 한다.
    ///
    /// 순서: 대상 확인 → writer 잠금 → 같은 디렉터리 임시 파일에 전체 쓰기 →
    /// flush(디스크까지) → rename으로 원자 교체. rename 전 실패/취소는 기존
    /// 파일을 건드리지 않는다. rename 이후에는 새 완전한 파일이 최종본이다.
    /// copy/delete 방식 덮어쓰기 fallback은 없다.
    ///
    /// 보장하지 않는 것: 정전 내구성(디렉터리 fsync 없음), 모든 filesystem/OS.
    /// </summary>
    internal static class AtomicFileWriter
    {
        public const string TargetUnsupportedCode = "WRITE_TARGET_UNSUPPORTED";
        public const string ParentMissingCode = "WRITE_PARENT_MISSING";
        public const string LockedCode = "WRITE_LOCKED";
        public const string FailedCode = "WRITE_FAILED";
        public const string CancelledCode = "WRITE_CANCELLED";

        public const string TempSuffix = ".nodekit-tmp";
        public const string LockSuffix = ".nodekit-lock";

        private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>File.WriteAllText와 같은 UTF-8(BOM 없음)으로 저장한다.</summary>
        public static AtomicWriteResult Write(string path, string content, CancellationToken cancellationToken = default) =>
            Write(path, _utf8NoBom.GetBytes(content), cancellationToken, beforeStage: null);

        public static AtomicWriteResult Write(string path, byte[] content, CancellationToken cancellationToken = default) =>
            Write(path, content, cancellationToken, beforeStage: null);

        /// <summary>
        /// beforeStage: 테스트 전용 장애 주입 지점. 각 단계 직전에 호출되며
        /// 여기서 던진 예외는 실제 IO 실패와 같은 경로로 처리된다.
        /// </summary>
        internal static AtomicWriteResult Write(
            string path,
            byte[] content,
            CancellationToken cancellationToken,
            Action<AtomicWriteStage>? beforeStage)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(content);

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                return Fail(TargetUnsupportedCode, $"저장 대상이 파일 경로가 아닙니다: {path}");
            }

            if (!Directory.Exists(directory))
            {
                return Fail(ParentMissingCode, $"저장할 디렉터리가 없습니다: {directory}");
            }

            if (TryDescribeUnsupportedTarget(fullPath) is { } unsupported)
            {
                return Fail(TargetUnsupportedCode, $"{unsupported}: {path} (일반 파일만 원자적으로 교체합니다. 덮어쓰지 않았습니다.)");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(path);
            }

            FileStream lockStream;
            try
            {
                lockStream = AcquireWriterLock(directory, fileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Fail(FailedCode, $"저장할 디렉터리에 쓸 권한이 없습니다: {directory} ({ex.Message})");
            }
            catch (IOException ex)
            {
                return Fail(LockedCode, $"다른 NodeKit 프로세스가 같은 파일을 저장하는 중입니다: {path} ({ex.Message})");
            }

            using (lockStream)
            {
                RemoveStaleTempFiles(directory, fileName);
                return WriteLocked(path, fullPath, directory, fileName, content, cancellationToken, beforeStage);
            }
        }

        /// <summary>임시 파일 이름. 같은 디렉터리에 두어야 rename이 원자적이다.</summary>
        internal static string TempPrefix(string fileName) => $".{fileName}.";

        internal static string LockPath(string directory, string fileName) =>
            Path.Join(directory, $".{fileName}{LockSuffix}");

        private static AtomicWriteResult WriteLocked(
            string path,
            string fullPath,
            string directory,
            string fileName,
            byte[] content,
            CancellationToken cancellationToken,
            Action<AtomicWriteStage>? beforeStage)
        {
            var tempPath = Path.Join(directory, $"{TempPrefix(fileName)}{Guid.NewGuid():N}{TempSuffix}");
            var committed = false;
            try
            {
                beforeStage?.Invoke(AtomicWriteStage.TempCreate);
                using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    CopyUnixModeFromExisting(fullPath, temp);

                    beforeStage?.Invoke(AtomicWriteStage.Write);
                    temp.Write(content, 0, content.Length);

                    beforeStage?.Invoke(AtomicWriteStage.Flush);
                    temp.Flush(flushToDisk: true);
                }

                // commit 지점: 이 검사 뒤로는 취소를 받지 않는다.
                if (cancellationToken.IsCancellationRequested)
                {
                    return Cancelled(path);
                }

                beforeStage?.Invoke(AtomicWriteStage.Replace);
                File.Move(tempPath, fullPath, overwrite: true);
                committed = true;
                return AtomicWriteResult.Committed;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Fail(FailedCode, $"파일을 저장할 권한이 없습니다: {path} ({ex.Message}). 기존 파일은 바뀌지 않았습니다.");
            }
            catch (IOException ex)
            {
                return Fail(FailedCode, $"파일을 저장하지 못했습니다: {path} ({ex.Message}). 기존 파일은 바뀌지 않았습니다.");
            }
            finally
            {
                if (!committed)
                {
                    TryDelete(tempPath);
                }
            }
        }

        // 이미 있는 대상이 일반 파일이 아니면(디렉터리, symlink, 장치/FIFO 등)
        // rename 교체가 사용자가 기대한 대상을 바꾸지 않거나 의미가 달라진다.
        private static string? TryDescribeUnsupportedTarget(string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                return "저장 대상이 디렉터리입니다";
            }

            FileSystemInfo info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                return info.LinkTarget is null ? null : "저장 대상이 깨진 심볼릭 링크입니다";
            }

            return info.LinkTarget is null ? null : "저장 대상이 심볼릭 링크입니다";
        }

        // FileShare.None은 Unix에서 flock(LOCK_EX|LOCK_NB)로 구현된다. 잠금 파일은
        // 지우지 않는다 — 지우면 이미 열어 둔 다른 writer가 unlink된 inode를
        // 잠그는 동안 새 writer가 새 파일을 잠가 배제가 깨진다.
        private static FileStream AcquireWriterLock(string directory, string fileName) =>
            new(LockPath(directory, fileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        // 잠금을 잡은 상태이므로 같은 대상의 임시 파일은 강제 종료된 이전 writer의
        // 잔여물이다. 최종 경로는 rename 전이므로 영향이 없다.
        private static void RemoveStaleTempFiles(string directory, string fileName)
        {
            List<string> stale;
            try
            {
                stale = Directory.EnumerateFiles(directory, $"*{TempSuffix}")
                    .Where(f => IsTempFileFor(Path.GetFileName(f), fileName))
                    .ToList();
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            foreach (var file in stale)
            {
                TryDelete(file);
            }
        }

        // 정확히 ".{fileName}.{32 hex}.nodekit-tmp"만 이 대상의 임시 파일이다.
        // 접두어만 비교하면 "a.json.bak"의 임시 파일을 "a.json" writer가 지운다.
        // 파일 이름의 '*'/'?'가 검색 패턴으로 해석되지 않도록 직접 비교한다.
        internal static bool IsTempFileFor(string candidate, string fileName)
        {
            var prefix = TempPrefix(fileName);
            const int idLength = 32;
            if (candidate.Length != prefix.Length + idLength + TempSuffix.Length
                || !candidate.StartsWith(prefix, StringComparison.Ordinal)
                || !candidate.EndsWith(TempSuffix, StringComparison.Ordinal))
            {
                return false;
            }

            return candidate.Substring(prefix.Length, idLength).All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
        }

        private static void CopyUnixModeFromExisting(string fullPath, FileStream temp)
        {
            if (OperatingSystem.IsWindows() || !File.Exists(fullPath))
            {
                return;
            }

            File.SetUnixFileMode(temp.SafeFileHandle, File.GetUnixFileMode(fullPath));
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static AtomicWriteResult Fail(string code, string message) =>
            new(AtomicWriteOutcome.Failed, code, message);

        private static AtomicWriteResult Cancelled(string path) =>
            new(AtomicWriteOutcome.Cancelled, CancelledCode, $"저장이 취소되었습니다: {path}. 기존 파일은 바뀌지 않았습니다.");
    }
}
