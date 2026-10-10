using System.Text;
using System.Text.Json;

namespace NodeKit.Cli.Operations
{
    /// <summary>저장소 오류 하나. ExitCode는 CLI 종료 코드로 그대로 쓰인다(취소 130, 그 외 2).</summary>
    internal sealed record OperationStoreError(string Code, string Message, int ExitCode = 2);

    /// <summary>어떤 파일을 쓰는 중인지 — 테스트 장애 주입 지점이 대상을 고를 때 쓴다.</summary>
    internal enum OperationWriteKind
    {
        Receipt,
        SourceSnapshot,
        ResolvedSnapshot,
    }

    /// <summary>테스트 장애 주입 문맥. Receipt 쓰기면 TargetPhase는 저장하려는 phase다.</summary>
    internal sealed record OperationWriteContext(OperationWriteKind Kind, string? TargetPhase, AtomicWriteStage Stage);

    /// <summary>현재 source가 receipt에 묶인 과거 source snapshot과 같은지(S2-02-C05).</summary>
    internal enum SourceBindingStatus
    {
        Matches,
        Diverged,
    }

    /// <summary>
    /// NodeKit 로컬 operation journal(S2-02). record root(기본: Recipe 옆 `.nodekit`) 아래에
    /// mutable receipt와 content-addressed 불변 snapshot을 둔다.
    ///
    ///   receipts/&lt;request-id&gt;.json            mutable operation receipt
    ///   snapshots/source/&lt;sha256&gt;.json        authoring/companion exact bytes
    ///   snapshots/resolved/&lt;sha256&gt;.json      실제 ResolveToolSpec 반환
    ///
    /// 모든 쓰기는 AtomicFileWriter(같은 디렉터리 임시 파일 → flush → rename)를 쓴다.
    /// operation handle은 살아 있는 동안 receipt별 writer 잠금을 쥔다 — 같은 receipt를
    /// 두 번째로 여는 writer는 기록/제출 없이 OPERATION_LOCKED로 멈춘다.
    /// 보장하지 않는 것: 정전 내구성, 모든 filesystem, 서버 쪽 중복 생성 방지.
    /// </summary>
    internal sealed class LocalOperationStore
    {
        public const string RootDirectoryName = ".nodekit";

        public const string ExistsCode = "OPERATION_RECORD_EXISTS";
        public const string LockedCode = "OPERATION_LOCKED";
        public const string WriteFailedCode = "OPERATION_WRITE_FAILED";
        public const string WriteCancelledCode = "OPERATION_WRITE_CANCELLED";
        public const string InvalidCode = "OPERATION_RECORD_INVALID";
        public const string MismatchCode = "OPERATION_RECORD_MISMATCH";

        public const string OperationLockSuffix = ".nodekit-oplock";

        private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

        private static readonly JsonSerializerOptions _readOptions = new()
        {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        };

        private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public LocalOperationStore(string rootDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
            RootDirectory = Path.GetFullPath(rootDirectory);
        }

        public string RootDirectory { get; }

        /// <summary>테스트 전용: 각 원자 쓰기 단계 직전에 호출된다. 던진 예외는 실제 IO 실패와 같은 경로다.</summary>
        internal Action<OperationWriteContext>? BeforeWriteStage { get; set; }

        /// <summary>Recipe 옆 `.nodekit`을 record root로 쓴다. 설치 디렉터리나 CWD를 요구하지 않는다.</summary>
        public static LocalOperationStore ForRecipe(string recipePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(recipePath);
            var directory = Path.GetDirectoryName(Path.GetFullPath(recipePath))!;
            return new LocalOperationStore(Path.Join(directory, RootDirectoryName));
        }

        public static string EnvelopeSha256(OperationEnvelope envelope) =>
            OperationHashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(envelope, _writeOptions));

        /// <summary>
        /// receipt 경로만으로 그 snapshot이 있는 record root를 정한다 — `receipt watch|cancel|replay
        /// &lt;receipt.json&gt;`처럼 receipt 경로만 받은 프로세스가 같은 store를 다시 연다.
        /// `receipts/` 디렉터리 안의 receipt는 그 부모가 root(기본 배치)이고, 그 외 위치의
        /// receipt는 같은 디렉터리의 `.nodekit`이 root다.
        /// </summary>
        public static string RootForReceipt(string receiptPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
            var directory = Path.GetDirectoryName(Path.GetFullPath(receiptPath))!;
            var parent = Path.GetDirectoryName(directory);
            return Path.GetFileName(directory) == "receipts" && parent is not null
                ? parent
                : Path.Join(directory, RootDirectoryName);
        }

        public static LocalOperationStore ForReceipt(string receiptPath) => new(RootForReceipt(receiptPath));

        public string DefaultReceiptPath(string requestId) =>
            Path.Join(RootDirectory, "receipts", $"{requestId}.json");

        /// <summary>
        /// fresh ToolSpec 요청을 prepared로 기록한다 — 부작용 RPC 전의 intent 저장이다.
        /// 이미 있는 receipt는 조용히 덮지 않는다(2). 성공하면 잠금을 쥔 handle을 돌려준다.
        /// </summary>
        public OperationStoreError? TryCreateToolSpecOperation(
            string requestId,
            string endpoint,
            OperationEnvelope envelope,
            SourceSnapshot source,
            string? receiptPath,
            out OperationHandle? handle)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(envelope);
            ArgumentNullException.ThrowIfNull(source);
            handle = null;

            var path = Path.GetFullPath(receiptPath ?? DefaultReceiptPath(requestId));
            if (RootMismatch(path) is { } rootError)
            {
                return rootError;
            }

            // 손상된 source를 durable basis로 남기지 않는다 — receipt/snapshot을 쓰기 전에 멈춘다.
            if (source.SchemaVersion != SourceSnapshot.CurrentSchemaVersion)
            {
                return Invalid(path, $"지원하지 않는 source snapshot schema '{source.SchemaVersion}'");
            }

            if (source.FindInvalidEntry() is { } sourceEntryError)
            {
                return Invalid(path, $"source snapshot: {sourceEntryError}");
            }

            if (receiptPath is null && TryEnsureDirectory(Path.GetDirectoryName(path)!) is { } dirError)
            {
                return dirError;
            }

            if (TryAcquireLock(path, out var lockStream) is { } lockError)
            {
                return lockError;
            }

            if (File.Exists(path) || Directory.Exists(path))
            {
                lockStream!.Dispose();
                return new OperationStoreError(ExistsCode, $"operation 기록이 이미 있습니다: {path}. 덮어쓰지 않았습니다.");
            }

            var sourceBytes = JsonSerializer.SerializeToUtf8Bytes(source, _writeOptions);
            var sourceSha = OperationHashing.Sha256Hex(sourceBytes);
            if (WriteImmutable(OperationWriteKind.SourceSnapshot, SourceSnapshotPath(sourceSha), sourceBytes) is { } sourceError)
            {
                lockStream!.Dispose();
                return sourceError;
            }

            var receipt = new OperationReceipt
            {
                SchemaVersion = OperationReceipt.CurrentSchemaVersion,
                OperationKind = OperationReceipt.ToolSpecBuildKind,
                RequestId = requestId,
                Endpoint = endpoint,
                Phase = OperationPhase.Prepared,
                Envelope = envelope,
                EnvelopeSha256 = EnvelopeSha256(envelope),
                SourceSnapshotSha256 = sourceSha,
            };

            if (WriteReceipt(path, receipt) is { } receiptError)
            {
                lockStream!.Dispose();
                return receiptError;
            }

            handle = new OperationHandle(this, path, receipt, lockStream!);
            return null;
        }

        /// <summary>
        /// 기존 receipt를 잠그고 읽는다. 잘린 JSON·미지원 schema·hash 불일치·참조 snapshot
        /// 누락/손상은 모두 OPERATION_RECORD_INVALID(2)이며 문자열 일부에서 값을 추측하지 않는다.
        /// </summary>
        public OperationStoreError? TryOpen(string receiptPath, out OperationHandle? handle)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
            handle = null;
            var path = Path.GetFullPath(receiptPath);
            if (RootMismatch(path) is { } rootError)
            {
                return rootError;
            }

            if (TryAcquireLock(path, out var lockStream) is { } lockError)
            {
                return lockError;
            }

            if (TryReadReceipt(path, out var receipt) is { } readError)
            {
                lockStream!.Dispose();
                return readError;
            }

            handle = new OperationHandle(this, path, receipt!, lockStream!);
            return null;
        }

        /// <summary>
        /// 현재 source(예: 편집된 Recipe)를 receipt에 묶인 과거 snapshot과 비교한다.
        /// 과거 snapshot은 지우거나 현재 내용에 다시 묶지 않는다.
        /// </summary>
        public static SourceBindingStatus CompareSource(OperationReceipt receipt, SourceSnapshot current)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            ArgumentNullException.ThrowIfNull(current);
            var currentSha = OperationHashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(current, _writeOptions));
            return currentSha == receipt.SourceSnapshotSha256 ? SourceBindingStatus.Matches : SourceBindingStatus.Diverged;
        }

        public OperationStoreError? TryReadSourceSnapshot(string sha256, out SourceSnapshot? snapshot)
        {
            snapshot = null;
            if (!OperationHashing.IsSha256Hex(sha256))
            {
                return Invalid(RootDirectory, "source snapshot ID가 소문자 SHA-256 hex가 아님");
            }

            var path = SourceSnapshotPath(sha256);
            if (TryReadImmutable(path, sha256, SourceSnapshot.CurrentSchemaVersion, s => s.SchemaVersion, out snapshot) is { } readError)
            {
                return readError;
            }

            // 바깥 hash가 맞아도 entry별 digest/content는 따로 확인해야 신뢰할 수 있다.
            if (snapshot!.FindInvalidEntry() is { } entryError)
            {
                snapshot = null;
                return Invalid(path, entryError);
            }

            return null;
        }

        public OperationStoreError? TryReadResolvedSnapshot(string sha256, out ResolvedSnapshot? snapshot)
        {
            snapshot = null;
            return OperationHashing.IsSha256Hex(sha256)
                ? TryReadImmutable(ResolvedSnapshotPath(sha256), sha256, ResolvedSnapshot.CurrentSchemaVersion, s => s.SchemaVersion, out snapshot)
                : Invalid(RootDirectory, "resolved snapshot ID가 소문자 SHA-256 hex가 아님");
        }

        internal string SourceSnapshotPath(string sha256) =>
            Path.Join(RootDirectory, "snapshots", "source", $"{sha256}.json");

        internal string ResolvedSnapshotPath(string sha256) =>
            Path.Join(RootDirectory, "snapshots", "resolved", $"{sha256}.json");

        internal OperationStoreError? WriteResolvedSnapshot(ResolvedSnapshot snapshot, out string sha256)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, _writeOptions);
            sha256 = OperationHashing.Sha256Hex(bytes);
            return WriteImmutable(OperationWriteKind.ResolvedSnapshot, ResolvedSnapshotPath(sha256), bytes);
        }

        internal OperationStoreError? WriteReceipt(string path, OperationReceipt receipt)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, _writeOptions);
            return AtomicWrite(path, bytes, new OperationWriteContext(OperationWriteKind.Receipt, receipt.Phase, AtomicWriteStage.TempCreate));
        }

        private static OperationStoreError Invalid(string path, string reason) =>
            new(InvalidCode, $"operation 기록을 신뢰할 수 없습니다: {path} ({reason}). 아무 요청도 보내지 않았습니다.");

        // receipt 경로에서 다시 찾을 수 없는 root에 snapshot을 두지 않는다 — 나중에 receipt
        // 경로만 받은 프로세스가 엉뚱한 곳에서 snapshot을 찾게 된다.
        private OperationStoreError? RootMismatch(string receiptPath)
        {
            var expected = RootForReceipt(receiptPath);
            return string.Equals(
                    Path.TrimEndingDirectorySeparator(expected),
                    Path.TrimEndingDirectorySeparator(RootDirectory),
                    StringComparison.Ordinal)
                ? null
                : new OperationStoreError(
                    MismatchCode,
                    $"receipt {receiptPath}의 record root는 {expected}인데 이 store는 {RootDirectory}입니다. receipt 경로로 snapshot을 다시 찾을 수 없어 기록하지 않았습니다.");
        }

        private static OperationStoreError? TryEnsureDirectory(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                return null;
            }
            catch (IOException ex)
            {
                return new OperationStoreError(WriteFailedCode, $"기록 디렉터리를 만들 수 없습니다: {directory} ({ex.Message})");
            }
            catch (UnauthorizedAccessException ex)
            {
                return new OperationStoreError(WriteFailedCode, $"기록 디렉터리를 만들 권한이 없습니다: {directory} ({ex.Message})");
            }
        }

        // receipt별 operation 잠금. AtomicFileWriter의 쓰기 잠금과 다른 파일이어야 한다 —
        // 같은 파일이면 operation 동안 쥔 잠금이 자기 자신의 receipt 쓰기를 막는다.
        // 잠금 파일은 지우지 않는다(AtomicFileWriter.AcquireWriterLock과 같은 이유).
        private static OperationStoreError? TryAcquireLock(string receiptPath, out FileStream? lockStream)
        {
            lockStream = null;
            var directory = Path.GetDirectoryName(receiptPath);
            var fileName = Path.GetFileName(receiptPath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                return new OperationStoreError(WriteFailedCode, $"receipt 경로가 파일 경로가 아닙니다: {receiptPath}");
            }

            if (!Directory.Exists(directory))
            {
                return new OperationStoreError(WriteFailedCode, $"receipt 디렉터리가 없습니다: {directory}");
            }

            var lockPath = Path.Join(directory, $".{fileName}{OperationLockSuffix}");
            try
            {
                lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return null;
            }
            catch (IOException ex) when (AtomicFileWriter.IsLockContention(ex))
            {
                return new OperationStoreError(LockedCode, $"다른 NodeKit 프로세스가 같은 operation 기록을 쓰는 중입니다: {receiptPath}. 새 요청 ID로 우회 제출하지 않았습니다.");
            }
            catch (IOException ex)
            {
                return new OperationStoreError(WriteFailedCode, $"operation 잠금 파일을 열 수 없습니다: {lockPath} ({ex.Message})");
            }
            catch (UnauthorizedAccessException ex)
            {
                return new OperationStoreError(WriteFailedCode, $"operation 잠금 파일을 만들 권한이 없습니다: {lockPath} ({ex.Message})");
            }
        }

        internal static OperationStoreError? ValidateReceipt(string path, OperationReceipt receipt)
        {
            if (receipt.SchemaVersion != OperationReceipt.CurrentSchemaVersion)
            {
                return Invalid(path, $"지원하지 않는 schema_version '{receipt.SchemaVersion}'");
            }

            if (receipt.OperationKind != OperationReceipt.ToolSpecBuildKind)
            {
                return Invalid(path, $"지원하지 않는 operation_kind '{receipt.OperationKind}'");
            }

            if (string.IsNullOrWhiteSpace(receipt.RequestId) || receipt.Envelope is null)
            {
                return Invalid(path, "request_id 또는 envelope 누락");
            }

            if (EnvelopeSha256(receipt.Envelope) != receipt.EnvelopeSha256)
            {
                return Invalid(path, "envelope_sha256 불일치");
            }

            if (!OperationHashing.IsSha256Hex(receipt.SourceSnapshotSha256)
                || (receipt.ResolvedSnapshotSha256 is { } resolvedSha && !OperationHashing.IsSha256Hex(resolvedSha)))
            {
                return Invalid(path, "snapshot ID가 소문자 SHA-256 hex가 아님");
            }

            var rank = OperationPhase.Rank(receipt.Phase);
            if (rank < 0)
            {
                return Invalid(path, $"알 수 없는 phase '{receipt.Phase}'");
            }

            // 각 phase의 필드 집합은 정확히 정해져 있다: 요구 필드가 없거나 뒤 phase에서만 쓰는 필드가
            // 있으면 손상이다. 예를 들어 build_id나 관측이 남은 prepared를 받아들이면 이미 제출된
            // 요청을 새 prepared로 보고 Resolve/Submit을 다시 보내게 된다.
            if (CheckPhaseField(path, receipt.Phase, "resolved snapshot 참조", receipt.ResolvedSnapshotSha256 is not null, !string.IsNullOrEmpty(receipt.ResolvedSnapshotSha256), rank >= OperationPhase.Rank(OperationPhase.SubmitInFlight)) is { } resolvedError)
            {
                return resolvedError;
            }

            if (CheckPhaseField(path, receipt.Phase, "build_id", receipt.BuildId is not null, !string.IsNullOrEmpty(receipt.BuildId), rank >= OperationPhase.Rank(OperationPhase.Acknowledged)) is { } buildIdError)
            {
                return buildIdError;
            }

            // terminal은 terminal watch 관측을 저장했다는 뜻이다 — 결과 없는 terminal은 이미 끝난
            // 빌드처럼 취급되지만 보고할 durable 상태/결과가 없다.
            var hasObservation = receipt.LastObservation is not null;
            if (CheckPhaseField(path, receipt.Phase, "last_observation", hasObservation, hasObservation, rank >= OperationPhase.Rank(OperationPhase.Terminal)) is { } observationError)
            {
                return observationError;
            }

            return null;
        }

        private static OperationStoreError? CheckPhaseField(string path, string phase, string field, bool present, bool complete, bool required)
        {
            if (required && !complete)
            {
                return Invalid(path, $"{phase}인데 {field}가 없음");
            }

            if (!required && present)
            {
                return Invalid(path, $"{phase}에는 {field}가 있을 수 없음");
            }

            return null;
        }

        private OperationStoreError? TryReadReceipt(string path, out OperationReceipt? receipt)
        {
            receipt = null;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (FileNotFoundException)
            {
                return Invalid(path, "파일 없음");
            }
            catch (DirectoryNotFoundException)
            {
                return Invalid(path, "파일 없음");
            }
            catch (IOException ex)
            {
                return Invalid(path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Invalid(path, ex.Message);
            }

            try
            {
                receipt = JsonSerializer.Deserialize<OperationReceipt>(bytes, _readOptions);
            }
            catch (JsonException ex)
            {
                return Invalid(path, $"JSON 손상: {ex.Message}");
            }

            if (receipt is null)
            {
                return Invalid(path, "빈 기록");
            }

            if (ValidateReceipt(path, receipt) is { } invalid)
            {
                receipt = null;
                return invalid;
            }

            if (TryReadSourceSnapshot(receipt.SourceSnapshotSha256, out _) is { } sourceError)
            {
                receipt = null;
                return sourceError;
            }

            if (receipt.ResolvedSnapshotSha256 is not { } resolvedSha)
            {
                return null;
            }

            if (TryReadResolvedSnapshot(resolvedSha, out var resolved) is { } resolvedError)
            {
                receipt = null;
                return resolvedError;
            }

            // 다른 operation의 유효한 snapshot을 가리키면 그 resolved digest를 이 빌드에 잘못 연결한다.
            // 같은 Recipe의 재시도는 source/envelope가 같으므로 attempt(request_id·endpoint)까지 대조한다.
            if (resolved!.RequestId != receipt.RequestId
                || resolved.Endpoint != receipt.Endpoint
                || resolved.SourceSnapshotSha256 != receipt.SourceSnapshotSha256
                || resolved.EnvelopeSha256 != receipt.EnvelopeSha256
                || resolved.RequestedToolName != receipt.Envelope.ToolName
                || resolved.RequestedVersion != receipt.Envelope.Version)
            {
                receipt = null;
                return Invalid(path, "resolved snapshot이 이 receipt의 attempt/source/envelope에 묶여 있지 않음");
            }

            return null;
        }

        private OperationStoreError? TryReadImmutable<T>(
            string path, string expectedSha, string expectedSchema, Func<T, string> schemaOf, out T? value)
            where T : class
        {
            value = null;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                return Invalid(path, $"snapshot을 읽을 수 없음: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Invalid(path, $"snapshot을 읽을 수 없음: {ex.Message}");
            }

            if (OperationHashing.Sha256Hex(bytes) != expectedSha)
            {
                return Invalid(path, "snapshot hash 불일치");
            }

            try
            {
                value = JsonSerializer.Deserialize<T>(bytes, _readOptions);
            }
            catch (JsonException ex)
            {
                return Invalid(path, $"snapshot JSON 손상: {ex.Message}");
            }

            if (value is null || schemaOf(value) != expectedSchema)
            {
                value = null;
                return Invalid(path, "지원하지 않는 snapshot schema");
            }

            return null;
        }

        // content-addressed 불변 파일: 이미 있으면 bytes가 같을 때만 그대로 둔다.
        // 다른 bytes가 있으면 덮지 않고 무결성 오류로 멈춘다.
        private OperationStoreError? WriteImmutable(OperationWriteKind kind, string path, byte[] bytes)
        {
            if (TryEnsureDirectory(Path.GetDirectoryName(path)!) is { } dirError)
            {
                return dirError;
            }

            if (File.Exists(path))
            {
                try
                {
                    return File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)
                        ? null
                        : Invalid(path, "같은 hash 이름의 불변 snapshot 내용이 다름");
                }
                catch (IOException ex)
                {
                    return Invalid(path, $"기존 snapshot을 읽을 수 없음: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Invalid(path, $"기존 snapshot을 읽을 수 없음: {ex.Message}");
                }
            }

            return AtomicWrite(path, bytes, new OperationWriteContext(kind, null, AtomicWriteStage.TempCreate));
        }

        private OperationStoreError? AtomicWrite(string path, byte[] bytes, OperationWriteContext context)
        {
            var hook = BeforeWriteStage;
            var result = AtomicFileWriter.Write(
                path,
                bytes,
                CancellationToken.None,
                hook is null ? null : stage => hook(context with { Stage = stage }));

            return result.Outcome switch
            {
                AtomicWriteOutcome.Committed => null,
                AtomicWriteOutcome.Cancelled => new OperationStoreError(WriteCancelledCode, result.Message ?? path, 130),
                _ => new OperationStoreError(WriteFailedCode, $"operation 기록을 저장하지 못했습니다 [{result.Code}]: {result.Message}"),
            };
        }
    }

    /// <summary>
    /// 잠금을 쥔 열린 operation 하나. Advance는 불변 필드를 바꾸지 않고 phase를
    /// 앞으로만 옮긴다. 저장에 실패하면 메모리의 Receipt도 이전 값 그대로다.
    /// </summary>
    internal sealed class OperationHandle : IDisposable
    {
        private readonly LocalOperationStore _store;
        private readonly FileStream _lock;
        private bool _disposed;

        internal OperationHandle(LocalOperationStore store, string receiptPath, OperationReceipt receipt, FileStream operationLock)
        {
            _store = store;
            ReceiptPath = receiptPath;
            Receipt = receipt;
            _lock = operationLock;
        }

        public string ReceiptPath { get; }

        public OperationReceipt Receipt { get; private set; }

        public LocalOperationStore Store => _store;

        public OperationStoreError? Advance(OperationReceipt next)
        {
            ArgumentNullException.ThrowIfNull(next);
            ObjectDisposedException.ThrowIf(_disposed, this);

            var current = Receipt;
            if (next.RequestId != current.RequestId
                || next.Endpoint != current.Endpoint
                || next.OperationKind != current.OperationKind
                || next.SchemaVersion != current.SchemaVersion
                || next.EnvelopeSha256 != current.EnvelopeSha256
                || !Equals(next.Envelope, current.Envelope)
                || next.SourceSnapshotSha256 != current.SourceSnapshotSha256)
            {
                return new OperationStoreError(
                    LocalOperationStore.MismatchCode,
                    $"저장된 요청(request ID: {current.RequestId})과 다른 payload/endpoint/operation_kind/version으로 기록을 바꿀 수 없습니다. 새 요청은 새 attempt로 시작하세요.");
            }

            if (OperationPhase.Rank(next.Phase) < OperationPhase.Rank(current.Phase))
            {
                return new OperationStoreError(
                    LocalOperationStore.MismatchCode,
                    $"operation phase를 {current.Phase}에서 {next.Phase}로 되돌릴 수 없습니다 (request ID: {current.RequestId}).");
            }

            if (current.ResolvedSnapshotSha256 is not null && next.ResolvedSnapshotSha256 != current.ResolvedSnapshotSha256)
            {
                return new OperationStoreError(
                    LocalOperationStore.MismatchCode,
                    $"이미 기록된 resolved snapshot을 바꿀 수 없습니다 (request ID: {current.RequestId}).");
            }

            if (current.BuildId is not null && next.BuildId != current.BuildId)
            {
                return new OperationStoreError(
                    LocalOperationStore.MismatchCode,
                    $"이미 기록된 build_id({current.BuildId})를 바꿀 수 없습니다 (request ID: {current.RequestId}).");
            }

            // 다시 열 때 INVALID가 될 기록은 처음부터 쓰지 않는다.
            if (LocalOperationStore.ValidateReceipt(ReceiptPath, next) is { } invalid)
            {
                return invalid;
            }

            if (_store.WriteReceipt(ReceiptPath, next) is { } error)
            {
                return error;
            }

            Receipt = next;
            return null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _lock.Dispose();
            _disposed = true;
        }
    }
}
