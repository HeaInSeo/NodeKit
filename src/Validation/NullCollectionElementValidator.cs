using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace NodeKit.Validation
{
    /// <summary>
    /// 외부 JSON에서 온 authoring 문서의 목록 원소 null을 L1 위반으로 찾는다
    /// (S1-02-C05 SourceBuildCommands [null], S4-03-C InputPorts/OutputPorts/
    /// Parameters/FixtureReferences [null]).
    ///
    /// System.Text.Json은 List&lt;string&gt;/List&lt;T&gt;의 null 원소를 그대로
    /// 넣는다. Normalize()는 목록 자체의 null만 고치므로, 원소 null은 이후
    /// validator/renderer의 c.Contains(...)나 port.Name 접근에서
    /// NullReferenceException이 된다. 파이프라인은 이 검사를 먼저 하고, 위반이
    /// 있으면 렌더링 전에 결과를 돌려준다. 원소를 조용히 지우지 않는다.
    ///
    /// NodeKit.Authoring 네임스페이스의 객체만 따라 내려간다 — 문서 구조만
    /// 보고 다른 타입을 탐색하지 않는다.
    /// </summary>
    internal static class NullCollectionElementValidator
    {
        private const string AuthoringNamespace = "NodeKit.Authoring";
        private const int MaxDepth = 16;

        public static ValidationResult Validate(object document, string ruleId)
        {
            var violations = new List<ValidationViolation>();
            Walk(document, prefix: string.Empty, ruleId, violations, depth: 0);
            return new ValidationResult(violations);
        }

        private static void Walk(object node, string prefix, string ruleId, List<ValidationViolation> violations, int depth)
        {
            if (depth > MaxDepth)
            {
                return;
            }

            foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                {
                    continue;
                }

                var value = property.GetValue(node);
                var path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

                if (value is IList list && value is not string)
                {
                    for (var i = 0; i < list.Count; i++)
                    {
                        var element = list[i];
                        var elementPath = $"{path}[{i}]";
                        if (element is null)
                        {
                            violations.Add(new ValidationViolation(
                                ruleId,
                                $"{elementPath} 원소가 null입니다. 목록에는 null 값을 넣을 수 없습니다 — 해당 원소를 지우거나 값을 채우세요.",
                                elementPath));
                        }
                        else if (IsAuthoringObject(element))
                        {
                            Walk(element, elementPath, ruleId, violations, depth + 1);
                        }
                    }
                }
                else if (value is not null && IsAuthoringObject(value))
                {
                    Walk(value, path, ruleId, violations, depth + 1);
                }
            }
        }

        private static bool IsAuthoringObject(object value)
        {
            var type = value.GetType();
            return type.IsClass
                && type.Namespace is { } ns
                && (ns == AuthoringNamespace || ns.StartsWith(AuthoringNamespace + ".", System.StringComparison.Ordinal));
        }
    }
}
