using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Nodevault.V1;

namespace NodeKit.Cli.Operations
{
    /// <summary>
    /// typed ToolFunction 등록 receipt의 상태(P08.registry, S3-07). prepared는 요청 basis가 저장됐고 결과를 아직
    /// 관측하지 못했다는 뜻이다 — 이미 보냈을 수 있다(attempts). unknown은 보냈지만 결과가 불명이다(응답 유실·
    /// Unavailable·버전/identity 불충분). registered와 rejected만 terminal이다.
    /// </summary>
    internal static class RegistrationState
    {
        public const string Prepared = "prepared";
        public const string Unknown = "unknown";
        public const string Registered = "registered";
        public const string Rejected = "rejected";

        public static bool IsKnown(string? state) => state is Prepared or Unknown or Registered or Rejected;

        public static bool IsTerminal(string? state) => state is Registered or Rejected;
    }

    /// <summary>등록 요청과 함께 고정하는 역할별 provenance. 요청 wire에 없는 A/C2와 빌드 연결도 여기에 둔다.</summary>
    internal sealed record RegistrationProvenance
    {
        /// <summary>A: raw_spec.base_image_digest로 보낸 기반 이미지.</summary>
        [JsonPropertyName("base_image_digest")]
        public required string BaseImageDigest { get; init; }

        /// <summary>C1: 기반 ToolSpec digest — 요청의 base_tool_spec_digest.</summary>
        [JsonPropertyName("base_tool_spec_digest")]
        public required string BaseToolSpecDigest { get; init; }

        /// <summary>B: 두 번째 이미지 digest — 요청의 image_digest.</summary>
        [JsonPropertyName("function_image_digest")]
        public required string FunctionImageDigest { get; init; }

        /// <summary>C2: 두 번째 빌드의 resolved ToolSpec digest. wire에 보내지 않는다.</summary>
        [JsonPropertyName("second_request_basis")]
        public string? SecondRequestBasis { get; init; }

        [JsonPropertyName("build_request_id")]
        public string? BuildRequestId { get; init; }

        [JsonPropertyName("build_id")]
        public string? BuildId { get; init; }
    }

    /// <summary>서버가 돌려준 versioned 결과 그대로. client는 어느 값도 계산하거나 대체하지 않는다.</summary>
    internal sealed record RegistrationResult
    {
        [JsonPropertyName("tool_function_digest")]
        public required string ToolFunctionDigest { get; init; }

        [JsonPropertyName("cas_hash")]
        public required string CasHash { get; init; }

        [JsonPropertyName("presentation_revision_id")]
        public required string PresentationRevisionId { get; init; }

        [JsonPropertyName("canonicalization_version")]
        public required string CanonicalizationVersion { get; init; }

        [JsonPropertyName("observed_at")]
        public required string ObservedAt { get; init; }
    }

    /// <summary>마지막 실패/불명 관측. Observed는 응답이 불충분할 때 실제로 받은 값의 진단용 요약이다.</summary>
    internal sealed record RegistrationError
    {
        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("observed")]
        public string? Observed { get; init; }

        [JsonPropertyName("observed_at")]
        public required string ObservedAt { get; init; }
    }

    /// <summary>
    /// mutable 등록 receipt. request_id·endpoint·canonicalization_version·request_sha256·provenance는 생성 후 바뀌지
    /// 않는다. 정확한 요청 bytes는 content-addressed snapshot에 있고 replay는 그 bytes만 다시 보낸다.
    /// </summary>
    internal sealed record RegistrationReceipt
    {
        public const string CurrentSchemaVersion = "nodekit.registration.v1";

        public const string Kind = "toolfunction_registration";

        [JsonPropertyName("schema_version")]
        public required string SchemaVersion { get; init; }

        [JsonPropertyName("operation_kind")]
        public required string OperationKind { get; init; }

        [JsonPropertyName("request_id")]
        public required string RequestId { get; init; }

        [JsonPropertyName("endpoint")]
        public required string Endpoint { get; init; }

        [JsonPropertyName("canonicalization_version")]
        public required string CanonicalizationVersion { get; init; }

        [JsonPropertyName("request_sha256")]
        public required string RequestSha256 { get; init; }

        [JsonPropertyName("provenance")]
        public required RegistrationProvenance Provenance { get; init; }

        [JsonPropertyName("state")]
        public required string State { get; init; }

        /// <summary>RegisterToolFunction을 보내려고 기록한 횟수. 보내기 직전에 늘린다.</summary>
        [JsonPropertyName("attempts")]
        public int Attempts { get; init; }

        [JsonPropertyName("result")]
        public RegistrationResult? Result { get; init; }

        [JsonPropertyName("last_error")]
        public RegistrationError? LastError { get; init; }
    }

    /// <summary>
    /// 등록 operation journal(S2 공통 record root 안). build receipt와 같은 `.nodekit` root에 둔다.
    ///
    ///   registrations/&lt;request-id&gt;.json              mutable 등록 receipt
    ///   snapshots/registration/&lt;sha256&gt;.bin          RegisterToolFunctionRequest exact protobuf bytes
    ///
    /// 쓰기는 모두 AtomicFileWriter다. 열린 handle은 receipt별 잠금을 쥔다.
    /// </summary>
    internal sealed class RegistrationStore
    {
        public const string ExistsCode = LocalOperationStore.ExistsCode;
        public const string LockedCode = LocalOperationStore.LockedCode;
        public const string WriteFailedCode = LocalOperationStore.WriteFailedCode;
        public const string InvalidCode = LocalOperationStore.InvalidCode;
        public const string MismatchCode = LocalOperationStore.MismatchCode;

        private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

        private static readonly JsonSerializerOptions _readOptions = new()
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        public RegistrationStore(string rootDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
            RootDirectory = Path.GetFullPath(rootDirectory);
        }

        public string RootDirectory { get; }

        /// <summary>테스트 전용: 각 원자 쓰기 단계 직전에 호출된다. 던진 예외는 실제 IO 실패와 같은 경로다.</summary>
        internal Action<RegistrationWriteContext>? BeforeWriteStage { get; set; }

        /// <summary>등록 receipt 경로로 record root를 정한다(`registrations/` 부모, 아니면 같은 디렉터리의 `.nodekit`).</summary>
        public static string RootForReceipt(string receiptPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
            var directory = Path.GetDirectoryName(Path.GetFullPath(receiptPath))!;
            var parent = Path.GetDirectoryName(directory);
            return Path.GetFileName(directory) == "registrations" && parent is not null
                ? parent
                : Path.Join(directory, LocalOperationStore.RootDirectoryName);
        }

        public static RegistrationStore ForReceipt(string receiptPath) => new(RootForReceipt(receiptPath));

        public string DefaultReceiptPath(string requestId) =>
            Path.Join(RootDirectory, "registrations", $"{requestId}.json");

        internal string RequestSnapshotPath(string sha256) =>
            Path.Join(RootDirectory, "snapshots", "registration", $"{sha256}.bin");

        /// <summary>
        /// 등록 요청 bytes와 prepared receipt를 RPC 전에 저장한다. 같은 receipt가 있으면 덮지 않는다.
        /// 성공하면 잠금을 쥔 handle을 돌려준다.
        /// </summary>
        public OperationStoreError? TryCreate(
            RegisterToolFunctionRequest request,
            string endpoint,
            RegistrationProvenance provenance,
            out RegistrationHandle? handle)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(provenance);
            handle = null;
            if (!LocalOperationStore.IsCanonicalRequestId(request.RequestId))
            {
                return new OperationStoreError(
                    InvalidCode,
                    $"등록 request ID '{request.RequestId}'가 소문자 GUID 형식이 아닙니다. 아무것도 기록하거나 보내지 않았습니다.");
            }

            var path = Path.GetFullPath(DefaultReceiptPath(request.RequestId));
            if (TryEnsureDirectory(Path.GetDirectoryName(path)!) is { } dirError)
            {
                return dirError;
            }

            if (LocalOperationStore.TryAcquireLock(path, out var lockStream) is { } lockError)
            {
                return lockError;
            }

            if (File.Exists(path) || Directory.Exists(path))
            {
                lockStream!.Dispose();
                return new OperationStoreError(ExistsCode, $"등록 기록이 이미 있습니다: {path}. 덮어쓰지 않았습니다.");
            }

            var bytes = request.ToByteArray();
            var sha = OperationHashing.Sha256Hex(bytes);
            if (WriteImmutable(RequestSnapshotPath(sha), bytes) is { } snapshotError)
            {
                lockStream!.Dispose();
                return snapshotError;
            }

            var receipt = new RegistrationReceipt
            {
                SchemaVersion = RegistrationReceipt.CurrentSchemaVersion,
                OperationKind = RegistrationReceipt.Kind,
                RequestId = request.RequestId,
                Endpoint = endpoint,
                CanonicalizationVersion = request.CanonicalizationVersion,
                RequestSha256 = sha,
                Provenance = provenance,
                State = RegistrationState.Prepared,
            };
            if (Validate(path, receipt, request) is { } invalid)
            {
                lockStream!.Dispose();
                return invalid;
            }

            if (WriteReceipt(path, receipt) is { } receiptError)
            {
                lockStream!.Dispose();
                return receiptError;
            }

            handle = new RegistrationHandle(this, path, receipt, request, lockStream!);
            return null;
        }

        /// <summary>기존 등록 receipt를 잠그고 읽는다. 손상·hash 불일치·snapshot 누락은 OPERATION_RECORD_INVALID(2)다.</summary>
        public OperationStoreError? TryOpen(string receiptPath, out RegistrationHandle? handle)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
            handle = null;
            var path = Path.GetFullPath(receiptPath);
            if (LocalOperationStore.TryAcquireLock(path, out var lockStream) is { } lockError)
            {
                return lockError;
            }

            if (TryRead(path, out var receipt, out var request) is { } readError)
            {
                lockStream!.Dispose();
                return readError;
            }

            handle = new RegistrationHandle(this, path, receipt!, request!, lockStream!);
            return null;
        }

        private OperationStoreError? TryRead(string path, out RegistrationReceipt? receipt, out RegisterToolFunctionRequest? request)
        {
            receipt = null;
            request = null;
            if (TryReadBytes(path, "파일", out var bytes) is { } readError)
            {
                return readError;
            }

            try
            {
                receipt = JsonSerializer.Deserialize<RegistrationReceipt>(bytes, _readOptions);
            }
            catch (JsonException ex)
            {
                return Invalid(path, $"JSON 손상: {ex.Message}");
            }

            if (receipt is null)
            {
                return Invalid(path, "빈 기록");
            }

            if (!OperationHashing.IsSha256Hex(receipt.RequestSha256))
            {
                receipt = null;
                return Invalid(path, "request_sha256이 소문자 SHA-256 hex가 아님");
            }

            var snapshotPath = RequestSnapshotPath(receipt.RequestSha256);
            if (TryReadBytes(snapshotPath, "요청 snapshot", out var requestBytes) is { } snapshotError)
            {
                receipt = null;
                return snapshotError;
            }

            if (OperationHashing.Sha256Hex(requestBytes) != receipt.RequestSha256)
            {
                receipt = null;
                return Invalid(snapshotPath, "요청 snapshot hash 불일치");
            }

            try
            {
                request = RegisterToolFunctionRequest.Parser.ParseFrom(requestBytes);
            }
            catch (InvalidProtocolBufferException ex)
            {
                receipt = null;
                return Invalid(snapshotPath, $"요청 snapshot protobuf 손상: {ex.Message}");
            }

            if (Validate(path, receipt, request) is { } invalid)
            {
                receipt = null;
                request = null;
                return invalid;
            }

            return null;
        }

        /// <summary>
        /// 다시 열 때 신뢰할 수 없는 기록은 쓰지도 읽지도 않는다. 요청 bytes와 receipt의 request_id·version,
        /// provenance의 C1/B가 같은 요청을 가리켜야 하고, state별 필드 집합이 맞아야 한다.
        /// </summary>
        internal static OperationStoreError? Validate(string path, RegistrationReceipt receipt, RegisterToolFunctionRequest request)
        {
            if (receipt.SchemaVersion != RegistrationReceipt.CurrentSchemaVersion || receipt.OperationKind != RegistrationReceipt.Kind)
            {
                return Invalid(path, $"지원하지 않는 schema_version/operation_kind '{receipt.SchemaVersion}'/'{receipt.OperationKind}'");
            }

            if (string.IsNullOrWhiteSpace(receipt.Endpoint) || receipt.Provenance is null || !RegistrationState.IsKnown(receipt.State) || receipt.Attempts < 0)
            {
                return Invalid(path, "endpoint/provenance/state/attempts가 올바르지 않음");
            }

            if (request.RequestId != receipt.RequestId
                || request.CanonicalizationVersion != receipt.CanonicalizationVersion
                || request.BaseToolSpecDigest != receipt.Provenance.BaseToolSpecDigest
                || request.ImageDigest != receipt.Provenance.FunctionImageDigest)
            {
                return Invalid(path, "요청 snapshot이 receipt의 request_id/version/C1/B와 다름");
            }

            var registered = receipt.State == RegistrationState.Registered;
            if (registered != (receipt.Result is not null))
            {
                return Invalid(path, $"{receipt.State}와 result가 맞지 않음");
            }

            if (receipt.State is RegistrationState.Unknown or RegistrationState.Rejected && receipt.LastError is null)
            {
                return Invalid(path, $"{receipt.State}인데 last_error가 없음");
            }

            if (receipt.State != RegistrationState.Prepared && receipt.Attempts == 0)
            {
                return Invalid(path, $"{receipt.State}인데 attempts가 0");
            }

            return null;
        }

        internal OperationStoreError? WriteReceipt(string path, RegistrationReceipt receipt)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, _writeOptions);
            return AtomicWrite(path, bytes, new RegistrationWriteContext(RegistrationWriteKind.Receipt, receipt.State, receipt.Attempts, AtomicWriteStage.TempCreate));
        }

        private static OperationStoreError Invalid(string path, string reason) =>
            new(InvalidCode, $"등록 기록을 신뢰할 수 없습니다: {path} ({reason}). 아무 요청도 보내지 않았습니다.");

        private static OperationStoreError? TryReadBytes(string path, string what, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            try
            {
                bytes = File.ReadAllBytes(path);
                return null;
            }
            catch (FileNotFoundException)
            {
                return Invalid(path, $"{what} 없음");
            }
            catch (DirectoryNotFoundException)
            {
                return Invalid(path, $"{what} 없음");
            }
            catch (IOException ex)
            {
                return Invalid(path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Invalid(path, ex.Message);
            }
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

        // content-addressed 불변 파일: 이미 있으면 bytes가 같을 때만 그대로 둔다.
        private OperationStoreError? WriteImmutable(string path, byte[] bytes)
        {
            if (TryEnsureDirectory(Path.GetDirectoryName(path)!) is { } dirError)
            {
                return dirError;
            }

            if (File.Exists(path))
            {
                return TryReadBytes(path, "기존 요청 snapshot", out var existing) is { } readError
                    ? readError
                    : existing.AsSpan().SequenceEqual(bytes) ? null : Invalid(path, "같은 hash 이름의 요청 snapshot 내용이 다름");
            }

            return AtomicWrite(path, bytes, new RegistrationWriteContext(RegistrationWriteKind.RequestSnapshot, null, 0, AtomicWriteStage.TempCreate));
        }

        private OperationStoreError? AtomicWrite(string path, byte[] bytes, RegistrationWriteContext context)
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
                AtomicWriteOutcome.Cancelled => new OperationStoreError(LocalOperationStore.WriteCancelledCode, result.Message ?? path, 130),
                _ => new OperationStoreError(WriteFailedCode, $"등록 기록을 저장하지 못했습니다 [{result.Code}]: {result.Message}"),
            };
        }
    }

    internal enum RegistrationWriteKind
    {
        Receipt,
        RequestSnapshot,
    }

    /// <summary>테스트 장애 주입 문맥. Receipt 쓰기면 저장하려는 state와 attempts를 담는다.</summary>
    internal sealed record RegistrationWriteContext(RegistrationWriteKind Kind, string? TargetState, int Attempts, AtomicWriteStage Stage);

    /// <summary>잠금을 쥔 열린 등록 operation. 불변 필드는 바꾸지 않고 terminal 뒤에는 바꾸지 않는다.</summary>
    internal sealed class RegistrationHandle : IDisposable
    {
        private readonly RegistrationStore _store;
        private readonly FileStream _lock;
        private bool _disposed;

        internal RegistrationHandle(RegistrationStore store, string receiptPath, RegistrationReceipt receipt, RegisterToolFunctionRequest request, FileStream operationLock)
        {
            _store = store;
            ReceiptPath = receiptPath;
            Receipt = receipt;
            Request = request;
            _lock = operationLock;
        }

        public string ReceiptPath { get; }

        public RegistrationReceipt Receipt { get; private set; }

        /// <summary>저장된 정확한 요청. replay는 이것만 보낸다.</summary>
        public RegisterToolFunctionRequest Request { get; }

        public OperationStoreError? Advance(RegistrationReceipt next)
        {
            ArgumentNullException.ThrowIfNull(next);
            ObjectDisposedException.ThrowIf(_disposed, this);
            var current = Receipt;
            if (next.RequestId != current.RequestId
                || next.Endpoint != current.Endpoint
                || next.SchemaVersion != current.SchemaVersion
                || next.OperationKind != current.OperationKind
                || next.CanonicalizationVersion != current.CanonicalizationVersion
                || next.RequestSha256 != current.RequestSha256
                || !Equals(next.Provenance, current.Provenance))
            {
                return new OperationStoreError(
                    RegistrationStore.MismatchCode,
                    $"저장된 등록 요청(request ID: {current.RequestId})의 불변 필드를 바꿀 수 없습니다.");
            }

            if (RegistrationState.IsTerminal(current.State) || next.Attempts < current.Attempts)
            {
                return new OperationStoreError(
                    RegistrationStore.MismatchCode,
                    $"등록 기록을 {current.State}/{current.Attempts}에서 {next.State}/{next.Attempts}로 바꿀 수 없습니다 (request ID: {current.RequestId}).");
            }

            if (RegistrationStore.Validate(ReceiptPath, next, Request) is { } invalid)
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
