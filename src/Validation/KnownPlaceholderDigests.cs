namespace NodeKit.Validation
{
    /// <summary>
    /// 실재하지 않는(placeholder) 이미지 digest 상수의 단일 정의처.
    /// stub resolver가 발급하는 고정 digest 등, 형식은 맞지만 어떤 레지스트리에도
    /// 존재하지 않는 값을 여기 모아 두고 L1(ImageUriValidator)이 거절한다.
    /// StubImageDigestResolver도 이 상수를 참조하므로 정의처가 한 곳으로 유지된다.
    /// </summary>
    internal static class KnownPlaceholderDigests
    {
        /// <summary>
        /// NODEKIT_BASE_IMAGE_STUB resolver가 발급하는 고정 digest.
        /// 레지스트리 없이 저작 흐름을 돌려보기 위한 값이므로, 이 digest로 저작된
        /// 레시피는 제출 가능한 산출물이 되어서는 안 된다(ImageUriValidator L1-IMG-007).
        /// </summary>
        public const string BaseImageStub =
            "sha256:0000000000000000000000000000000000000000000000000000000000000001";
    }
}
