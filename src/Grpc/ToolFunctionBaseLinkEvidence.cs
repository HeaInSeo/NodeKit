using System;

namespace NodeKit.Grpc
{
    /// <summary>
    /// S3-02-A provenance: 등록에 쓰는 C1/A 쌍이 같은 첫 빌드에서 왔는지 local 근거로만
    /// 분류한다. 어느 결과도 서버 권위의 C1↔A 연결 검증이 아니다. 형식이 맞는 것만으로
    /// 연결을 추정하지 않고, 저장해 둔 라벨을 입력으로 받지 않으므로 저장된 라벨이
    /// 상태를 복원하지도 않는다 — 매번 현재 입력과 receipt 관측으로 다시 계산한다.
    /// </summary>
    internal static class ToolFunctionBaseLinkEvidence
    {
        public const string ObservedSameBuildLabel = "OBSERVED_SAME_BUILD";
        public const string ManualUnverifiedLabel = "MANUAL_UNVERIFIED";

        public static ToolFunctionBaseLinkState Classify(
            string? toolSpecDigest,
            string? baseImageDigest,
            ToolFunctionBaseBuildObservation? observation)
        {
            if (observation is null
                || string.IsNullOrWhiteSpace(observation.Endpoint)
                || string.IsNullOrWhiteSpace(observation.RequestId)
                || string.IsNullOrWhiteSpace(observation.BuildId)
                || string.IsNullOrWhiteSpace(observation.ToolSpecDigest)
                || string.IsNullOrWhiteSpace(observation.ImageDigest))
            {
                return ToolFunctionBaseLinkState.ManualUnverified;
            }

            // 대소문자 변환·접두 제거 없이 정확히 같은 값이어야 한다.
            return string.Equals(toolSpecDigest, observation.ToolSpecDigest, StringComparison.Ordinal)
                && string.Equals(baseImageDigest, observation.ImageDigest, StringComparison.Ordinal)
                ? ToolFunctionBaseLinkState.ObservedSameBuild
                : ToolFunctionBaseLinkState.ManualUnverified;
        }

        public static string Label(ToolFunctionBaseLinkState state) => state switch
        {
            ToolFunctionBaseLinkState.ObservedSameBuild => ObservedSameBuildLabel,
            _ => ManualUnverifiedLabel,
        };
    }
}
