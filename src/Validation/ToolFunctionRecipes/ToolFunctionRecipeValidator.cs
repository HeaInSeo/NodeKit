using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NodeKit.Authoring.ToolFunctionRecipes;

namespace NodeKit.Validation.ToolFunctionRecipes
{
    /// <summary>
    /// ToolFunctionRecipe L1 정적 검증 (data-model.md 검증 규칙 표, research.md §2-6).
    /// RecipeValidator와 동일하게 IValidator를 구현하지 않는다 — ToolFunctionRecipe는
    /// ToolDefinition으로 렌더링되지 않으므로 그 인터페이스의 대상이 아니다.
    /// </summary>
    internal static class ToolFunctionRecipeValidator
    {
        // O-1 authoring binding bridge(architecture §Authoring binding bridge, CLOSED MINIMUM):
        // 참조는 argument 요소 하나 전체를 차지하고 세 namespace 중 하나를 정확히 쓴다.
        private const string ParamNamespace = "param";
        private const string InputNamespace = "input";
        private const string OutputNamespace = "output";

        private const string ReferenceNamePattern = @"[A-Za-z][A-Za-z0-9._-]*";

        // quickstart.md 예시("samtools.sort")와 일치하는 형식 — 소문자로 시작하고
        // 소문자/숫자 세그먼트를 '.'/'_'/'-'로 구분.
        private static readonly Regex _functionIdPattern =
            new(@"\A[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*\z", RegexOptions.Compiled);

        // executable에 공백이나 셸 메타문자가 있으면 raw shell 문자열을 그대로
        // 넣으려 한 것으로 간주해 차단한다(FR-006).
        private static readonly char[] _shellMetaCharacters = { ' ', '\t', '|', ';', '>', '<', '&', '\n', '\r' };

        private static readonly Regex _referenceNamePattern =
            new(@"\A" + ReferenceNamePattern + @"\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex _wholeElementReferencePattern =
            new(@"\A\{(param|input|output)\.(" + ReferenceNamePattern + @")\}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // 참조처럼 보이는 요소: '{' 바로 뒤에 namespace 후보 + '.'가 오는 부분이 있으면
        // 참조 시도로 본다. `{}`, `{foo}`, `{ param.t }` 같은 경계는 설계상 OPEN(#115)이라
        // 여기서 새로 판정하지 않고 literal로 둔다.
        private static readonly Regex _referenceLikePattern =
            new(@"\{[A-Za-z]+\.", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static ValidationResult Validate(ToolFunctionRecipe recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);

            var violations = new List<ValidationViolation>();

            ValidateDigestReferences(recipe, violations);
            ValidateFunctionId(recipe, violations);
            ValidateCommandStructure(recipe, violations);
            ValidatePortNameUniqueness(recipe, violations);
            ValidateEnforcedResources(recipe, violations);
            ValidateRequiredFields(recipe, violations);
            ValidateExpectedResultReferences(recipe, violations);
            ValidateArgumentReferences(recipe, violations);

            return new ValidationResult(violations);
        }

        // L1-TFR-001
        private static void ValidateDigestReferences(ToolFunctionRecipe recipe, List<ValidationViolation> violations)
        {
            if (string.IsNullOrWhiteSpace(recipe.ToolSpecDigest))
            {
                violations.Add(new ValidationViolation(
                    "L1-TFR-001", "toolSpecDigest 참조가 필요합니다.", nameof(recipe.ToolSpecDigest)));
            }

            if (string.IsNullOrWhiteSpace(recipe.BaseToolImageDigest))
            {
                violations.Add(new ValidationViolation(
                    "L1-TFR-001", "baseToolImageDigest 참조가 필요합니다.", nameof(recipe.BaseToolImageDigest)));
            }
        }

        // L1-TFR-002
        private static void ValidateFunctionId(ToolFunctionRecipe recipe, List<ValidationViolation> violations)
        {
            if (string.IsNullOrWhiteSpace(recipe.FunctionId))
            {
                // 값 자체의 부재는 L1-TFR-006(필수 필드 누락)이 담당한다 — 여기서는
                // 형식만 본다.
                return;
            }

            if (!_functionIdPattern.IsMatch(recipe.FunctionId))
            {
                violations.Add(new ValidationViolation(
                    "L1-TFR-002",
                    $"functionId 형식이 올바르지 않습니다. 소문자로 시작하고 소문자/숫자 세그먼트를 '.', '_', '-'로 구분해야 합니다: '{recipe.FunctionId}'",
                    nameof(recipe.FunctionId)));
            }
        }

        // L1-TFR-003
        private static void ValidateCommandStructure(ToolFunctionRecipe recipe, List<ValidationViolation> violations)
        {
            var executable = recipe.Command?.Executable;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return;
            }

            if (executable.IndexOfAny(_shellMetaCharacters) >= 0)
            {
                violations.Add(new ValidationViolation(
                    "L1-TFR-003",
                    $"executable에 공백/셸 메타문자를 포함할 수 없습니다. arguments 배열로 분리하세요: '{executable}'",
                    "Command.Executable"));
            }
        }

        // L1-TFR-004
        private static void ValidatePortNameUniqueness(ToolFunctionRecipe recipe, List<ValidationViolation> violations)
        {
            var allNames = recipe.InputPorts.Concat(recipe.OutputPorts)
                .Select(p => p.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name));

            var duplicates = allNames
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var duplicate in duplicates)
            {
                violations.Add(new ValidationViolation(
                    "L1-TFR-004",
                    $"입력/출력 포트 이름이 중복되었습니다: '{duplicate}'",
                    "InputPorts/OutputPorts"));
            }
        }

        // L1-TFR-005
        private static void ValidateEnforcedResources(ToolFunctionRecipe recipe, List<ValidationViolation> violations)
        {
            var resources = recipe.EnforcedResources;
            if (resources is null)
            {
                return;
            }

            CompareIfPresent(
                resources.CpuRequest,
                resources.CpuLimit,
                ParseCpuMillicores,
                "CpuLimit",
                violations);
            CompareIfPresent(
                resources.MemoryRequest,
                resources.MemoryLimit,
                ParseMemoryBytes,
                "MemoryLimit",
                violations);
        }

        private static void CompareIfPresent(
            string request,
            string limit,
            Func<string, double?> parse,
            string limitFieldName,
            List<ValidationViolation> violations)
        {
            if (string.IsNullOrWhiteSpace(request) || string.IsNullOrWhiteSpace(limit))
            {
                return;
            }

            var requestValue = parse(request);
            var limitValue = parse(limit);
            if (requestValue is null || limitValue is null)
            {
                return;
            }

            if (limitValue.Value < requestValue.Value)
            {
                violations.Add(new ValidationViolation(
                    "L1-TFR-005",
                    $"{limitFieldName}({limit})은 대응하는 request({request}) 이상이어야 합니다.",
                    limitFieldName));
            }
        }

        // K8s 스타일 CPU quantity: 접미사 없으면 코어, "m" 접미사면 밀리코어.
        private static double? ParseCpuMillicores(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.EndsWith('m'))
            {
                return double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
                    ? m
                    : null;
            }

            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var cores)
                ? cores * 1000
                : null;
        }

        // K8s 스타일 메모리 quantity: Ki/Mi/Gi/Ti(2진) 또는 K/M/G/T(10진), 접미사
        // 없으면 바이트.
        private static double? ParseMemoryBytes(string value)
        {
            var trimmed = value.Trim();
            (string Suffix, double Multiplier)[] units =
            {
                ("Ki", 1024), ("Mi", 1024d * 1024), ("Gi", 1024d * 1024 * 1024), ("Ti", 1024d * 1024 * 1024 * 1024),
                ("K", 1000), ("M", 1000d * 1000), ("G", 1000d * 1000 * 1000), ("T", 1000d * 1000 * 1000 * 1000),
            };

            foreach (var (suffix, multiplier) in units)
            {
                if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
                {
                    var numberPart = trimmed[..^suffix.Length];
                    return double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                        ? n * multiplier
                        : null;
                }
            }

            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var bytes)
                ? bytes
                : null;
        }

        // L1-TFR-006
        private static void ValidateRequiredFields(ToolFunctionRecipe recipe, List<ValidationViolation> violations)
        {
            if (string.IsNullOrWhiteSpace(recipe.FunctionId))
            {
                violations.Add(new ValidationViolation("L1-TFR-006", "functionId가 필요합니다.", nameof(recipe.FunctionId)));
            }

            if (string.IsNullOrWhiteSpace(recipe.Command?.Executable))
            {
                violations.Add(new ValidationViolation("L1-TFR-006", "command.executable이 필요합니다.", "Command.Executable"));
            }

            // 입력/출력 포트 개수에는 최소값이 없다: input-only, output-only, 포트 없는
            // 함수도 유효하다(NodeVault/proto와 동일). 포트 이름 중복은 L1-TFR-004가 담당한다.
            if (recipe.FixtureReferences.Count == 0)
            {
                violations.Add(new ValidationViolation("L1-TFR-006", "최소 1개 이상의 샘플 데이터/fixture 참조가 필요합니다.", nameof(recipe.FixtureReferences)));
            }

            var resources = recipe.EnforcedResources;
            if (resources is null
                || string.IsNullOrWhiteSpace(resources.CpuRequest)
                || string.IsNullOrWhiteSpace(resources.CpuLimit)
                || string.IsNullOrWhiteSpace(resources.MemoryRequest)
                || string.IsNullOrWhiteSpace(resources.MemoryLimit))
            {
                violations.Add(new ValidationViolation(
                    "L1-TFR-006",
                    "enforced 자원(CpuRequest/CpuLimit/MemoryRequest/MemoryLimit)이 모두 필요합니다.",
                    nameof(recipe.EnforcedResources)));
            }
        }

        // L1-TFR-007: 모든 ExpectedResult는 남아 있는 OutputPorts 이름을 참조해야 한다
        // (data-model.md). 출력 포트가 없으면 ExpectedResults도 비어 있어야 한다.
        private static void ValidateExpectedResultReferences(ToolFunctionRecipe recipe, List<ValidationViolation> violations)
        {
            var outputNames = new HashSet<string>(
                recipe.OutputPorts.Select(p => p.Name).Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);

            for (var i = 0; i < recipe.ExpectedResults.Count; i++)
            {
                var portName = recipe.ExpectedResults[i].OutputPortName;
                if (!outputNames.Contains(portName))
                {
                    violations.Add(new ValidationViolation(
                        "L1-TFR-007",
                        $"ExpectedResult가 존재하지 않는 출력 포트를 참조합니다: '{portName}'",
                        $"ExpectedResults[{i}].OutputPortName"));
                }
            }
        }

        // L1-TFR-008 ~ L1-TFR-012: O-1 argument reference와 parameter 선언의 참조 무결성.
        //   008 참조 형식 오류(embedded/namespace/이름 문법)
        //   009 참조 대상이 해당 namespace에 없음
        //   010 {input.*}/{output.*} 직접 참조 — 승인된 B profile 전에는 runnable 아님
        //   011 parameter 이름이 참조 불가능하거나 중복
        //   012 선언된 parameter가 어떤 {param.<name>} 요소로도 참조되지 않음(consume-all)
        // CliArgumentMapping, Environment, script는 참조 위치가 아니다.
        private static void ValidateArgumentReferences(ToolFunctionRecipe recipe, List<ValidationViolation> violations)
        {
            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            var referenceableParameters = new List<(int Index, string Name)>();
            for (var j = 0; j < recipe.Parameters.Count; j++)
            {
                var name = recipe.Parameters[j]?.Name ?? string.Empty;
                if (!_referenceNamePattern.IsMatch(name))
                {
                    violations.Add(new ValidationViolation(
                        "L1-TFR-011",
                        $"parameter 이름은 '{ReferenceNamePattern}' 형식이어야 {{param.<name>}}로 참조할 수 있습니다: '{name}'",
                        $"Parameters[{j}].Name"));
                    continue;
                }

                if (!parameterNames.Add(name))
                {
                    violations.Add(new ValidationViolation(
                        "L1-TFR-011",
                        $"parameter 이름이 중복되었습니다: '{name}'",
                        $"Parameters[{j}].Name"));
                    continue;
                }

                referenceableParameters.Add((j, name));
            }

            var inputNames = new HashSet<string>(recipe.InputPorts.Select(p => p.Name), StringComparer.Ordinal);
            var outputNames = new HashSet<string>(recipe.OutputPorts.Select(p => p.Name), StringComparer.Ordinal);
            var consumedParameters = new HashSet<string>(StringComparer.Ordinal);

            var arguments = recipe.Command?.Arguments ?? new List<string>();
            for (var i = 0; i < arguments.Count; i++)
            {
                var element = arguments[i] ?? string.Empty;
                var field = $"Command.Arguments[{i}]";

                var match = _wholeElementReferencePattern.Match(element);
                if (!match.Success)
                {
                    if (_referenceLikePattern.IsMatch(element))
                    {
                        violations.Add(new ValidationViolation(
                            "L1-TFR-008",
                            $"참조는 argument 요소 전체를 {{param.<name>}}, {{input.<name>}}, {{output.<name>}} 중 하나로 써야 합니다(embedded/다른 namespace/이름 형식 오류): '{element}'",
                            field));
                    }

                    continue;
                }

                var ns = match.Groups[1].Value;
                var target = match.Groups[2].Value;
                var targets = ns switch
                {
                    InputNamespace => inputNames,
                    OutputNamespace => outputNames,
                    _ => parameterNames,
                };

                if (!targets.Contains(target))
                {
                    violations.Add(new ValidationViolation(
                        "L1-TFR-009",
                        $"참조 대상이 {ns} namespace에 없습니다: '{element}'",
                        field));
                    continue;
                }

                if (ns == ParamNamespace)
                {
                    consumedParameters.Add(target);
                    continue;
                }

                violations.Add(new ValidationViolation(
                    "L1-TFR-010",
                    $"{{input.*}}/{{output.*}} 직접 참조는 승인된 runtime finalization profile 전에는 실행 가능하지 않습니다(Draft 유지): '{element}'",
                    field));
            }

            foreach (var (index, name) in referenceableParameters)
            {
                if (!consumedParameters.Contains(name))
                {
                    violations.Add(new ValidationViolation(
                        "L1-TFR-012",
                        $"parameter가 어떤 argument 요소에서도 {{param.{name}}}로 참조되지 않습니다(CliArgumentMapping만으로는 삽입되지 않음): '{name}'",
                        $"Parameters[{index}]"));
                }
            }
        }
    }
}
