using System;
using System.Collections.Generic;

namespace NodeKit.Policy
{
    /// <summary>정책 목록 조회 결과.</summary>
    internal sealed class PolicyListResult
    {
        public string BundleVersion { get; init; } = string.Empty;

        public IReadOnlyList<PolicyEntry> Policies { get; init; } = Array.Empty<PolicyEntry>();
    }
}
