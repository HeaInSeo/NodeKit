using System;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Grpc
{
    /// <summary>
    /// ResolveAndBuildAsync의 staged 호출 옵션. 기본값(둘 다 null)은 기존
    /// 동작과 같다 — client가 request ID를 새로 만들고 저장 seam 없이 바로
    /// SubmitToolBuild를 호출한다.
    /// </summary>
    internal sealed class ToolSpecBuildOptions
    {
        /// <summary>
        /// 호출자가 정한 SubmitToolBuild request ID. null이면 client가 새 GUID를
        /// 만든다. 빈 문자열/공백은 허용하지 않는다(ArgumentException).
        /// </summary>
        public string? RequestId { get; init; }

        /// <summary>
        /// ResolveToolSpec 성공 뒤, SubmitToolBuild 호출 직전에 await되는 durable
        /// 저장 seam. 이 delegate가 예외를 던지면(취소 제외) SubmitToolBuild와
        /// WatchToolBuild를 호출하지 않고 Failed 이벤트로 끝난다 — 저장되지 않은
        /// 제출은 서버에 보내지 않는다.
        /// </summary>
        public Func<ToolSpecSubmitBasis, CancellationToken, Task>? BeforeSubmitAsync { get; init; }

        internal bool IsDefault => RequestId is null && BeforeSubmitAsync is null;
    }
}
