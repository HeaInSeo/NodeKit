using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NodeKit.Cli.Operations;
using NodeKit.Grpc;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit function-recipe submit &lt;path&gt; [--url &lt;nodevault-url&gt;] [--receipt &lt;path&gt;]
    /// [--connect-timeout &lt;seconds&gt;] [--watch-timeout &lt;duration&gt;] [--format human|jsonl]
    ///
    /// P07.build (v0.9.1 S3-05): ToolFunctionRecipe의 두 번째(function) 이미지 B를 NodeVault
    /// BuildService client로 요청한다. 순서는 P06 current preflight(Ready·현재 내용 full L1·
    /// base image A·ScriptPath source·raw_spec v1·typed 등록 전제) → P04 durable receipt
    /// (operation_kind=toolfunction_image_build) → P05 ResolveToolSpec → SubmitToolBuild →
    /// WatchToolBuild다. ResolveToolSpec의 tool_name/version은 FunctionId/Revision이고 raw_spec은
    /// exact 4키 kind 2 payload다.
    ///
    /// 빌드 성공은 승인(Approved)이 아니며 typed 등록(RegisterToolFunction)은 하지 않는다 — 그것은
    /// 후속 P08 경로다. Failed/Interrupted, terminal 없는 종료, full B 없는 Succeeded는 모두 등록에
    /// 쓸 수 있는 성공이 아니다(exit 1). preflight 실패는 RPC를 보내지 않는다. image 생성이나
    /// Kubernetes 호출은 하지 않는다 — NodeKit은 NodeVault client로만 동작한다.
    ///
    /// `nodekit submit`과 달리 로컬 입력·preflight 검사를 NodeVault 주소 확인보다 먼저 한다.
    /// </summary>
    internal static class ToolFunctionRecipeSubmitCommand
    {
        private const string UsageLine =
            "사용법: nodekit function-recipe submit <path> [--url <nodevault-url>] [--receipt <path>] [--connect-timeout <seconds>] [--watch-timeout <duration>] [--format human|jsonl]";

        /// <summary>current preflight가 Recipe 내용을 거부했다 — 진단은 stderr에 있다. RPC0.</summary>
        internal const string PreflightFailedCode = "PREFLIGHT_FAILED";

        /// <param name="toolSpecClient">테스트 전용 주입 client. 없으면 실제 gRPC client를 만든다.</param>
        /// <param name="requestIdProvider">테스트 전용 고정 request ID.</param>
        /// <remarks>
        /// SubmitCommand와 같은 receipt 규칙이다: 실제 CLI는 항상 durable receipt를 남기고, 주입 client로
        /// 도는 테스트는 --receipt나 requestIdProvider를 줄 때만 기록한다.
        /// </remarks>
        public static int Run(
            string[] args,
            TextWriter stdout,
            TextWriter stderr,
            IToolSpecBuildClient? toolSpecClient = null,
            Func<string>? requestIdProvider = null)
        {
            if (args.Any(a => a is "--help" or "-h"))
            {
                stdout.WriteLine(UsageLine);
                return 0;
            }

            if (args.Length < 3)
            {
                stderr.WriteLine(UsageLine);
                return 2;
            }

            var recipePath = args[2];
            if (!SubmitCommand.TryParseOptions(
                    args,
                    startIndex: 3,
                    allowStrictReproducible: false,
                    stderr,
                    out var urlOption,
                    out var receiptOption,
                    out var connectTimeout,
                    out var watchTimeout,
                    out _,
                    out var jsonl))
            {
                return 2;
            }

            var journal = toolSpecClient is null || receiptOption is not null || requestIdProvider is not null;

            // 주소 확인보다 로컬 입력 검사를 먼저 한다 — Draft/invalid/SchemaVersion 오류는 NodeVault
            // 주소 설정 여부와 무관하게 같은 진단·exit로 끝나야 한다(RPC0).
            // validate/render와 같은 loader — 읽기/파싱/SchemaVersion 오류는 RPC 전에 exit 2로 끝난다.
            if (!ToolFunctionRecipeCliIo.TryLoad(recipePath, out var recipe, out var loadError, out var recipeBytes))
            {
                if (jsonl)
                {
                    SubmitCommand.WriteJsonl(
                        stdout,
                        SubmitJsonlRecord.Completed("Failed", errorCode: loadError!.Code, message: loadError.Message, recovery: RecoveryDisposition.Terminal));
                    return 2;
                }

                stderr.WriteLine(loadError!.Message);
                return 2;
            }

            // S3-09: 저장된 Ready 표지만 믿지 않고 현재 파일 내용으로 새 요청 basis를 만든다. 이 제출은
            // 이후 typed 등록에 쓰일 B를 만드는 것이므로 C1 형식과 typed mapping 전제도 여기서 확인한다.
            if (!ToolFunctionSubmissionPreflight.TryRun(recipe!, recipePath, requireTypedRegistration: true, stderr, out var preflight, out var preflightExit))
            {
                if (jsonl)
                {
                    SubmitCommand.WriteJsonl(
                        stdout,
                        SubmitJsonlRecord.Completed(
                            "Failed",
                            errorCode: PreflightFailedCode,
                            message: "제출 전 검증에 실패했습니다. 요청을 보내지 않았습니다 — 진단은 stderr를 확인하세요.",
                            recovery: RecoveryDisposition.Terminal));
                }

                return preflightExit;
            }

            if (!SubmitCommand.TryRequireUrl(urlOption, toolSpecClient, jsonl, "nodekit function-recipe submit recipe.json", stdout, stderr, out var url))
            {
                return 2;
            }

            var source = preflight!.Source;
            var request = new SubmitOperationRequest
            {
                RecipePath = recipePath,
                Url = url,
                ReceiptPath = receiptOption,
                ConnectTimeout = connectTimeout,
                WatchTimeout = watchTimeout,
                Jsonl = jsonl,
                Journal = journal,
                OperationKind = OperationReceipt.ToolFunctionImageBuildKind,
                Envelope = new OperationEnvelope
                {
                    ToolName = recipe!.FunctionId,
                    Version = recipe.Revision,
                    RawSpec = preflight.RawSpecJson,
                },

                // Recipe 파일 bytes와, raw_spec script로 보낸 materialize된 script 내용을 함께 고정한다.
                SourceFiles = new[]
                {
                    (Path.GetFileName(recipePath), recipeBytes!),
                    (source.ScriptPath, Encoding.UTF8.GetBytes(source.Script)),
                },
                SecondImage = new SecondImageBuildContext(source.BaseImageDigest, recipe.ToolSpecDigest),
            };

            return SubmitCommand.RunOperation(request, stdout, stderr, toolSpecClient, requestIdProvider, CancellationToken.None);
        }
    }
}
