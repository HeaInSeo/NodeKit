using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// NODEKIT_HARBOR_IMAGE_MAP는 프로세스 전역 환경변수다. 이 값을 바꾸고 되돌리는
    /// 테스트 class는 모두 이 collection에 넣는다. 기본 병렬 실행에서는 한 class가
    /// 값을 지우거나 원래대로 돌리는 순간 다른 class의 매핑이 사라져, 매핑 없음 테스트가
    /// HTTP를 보내거나 Step4가 0 대신 130으로 끝났다.
    /// </summary>
    [CollectionDefinition(nameof(HarborImageMapEnvironmentCollection), DisableParallelization = true)]
    public sealed class HarborImageMapEnvironmentCollection
    {
    }
}
