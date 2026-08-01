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
        // quickstart.md 예시("samtools.sort")와 일치하는 형식 — 소문자로 시작하고
        // 소문자/숫자 세그먼트를 '.'/'_'/'-'로 구분.
        private static readonly Regex _functionIdPattern =
            new(@"\A[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*\z", RegexOptions.Compiled);

        // executable에 공백이나 셸 메타문자가 있으면 raw shell 문자열을 그대로
        // 넣으려 한 것으로 간주해 차단한다(FR-006).
        private static readonly char[] _shellMetaCharacters = { ' ', '\t', '|', ';', '>', '<', '&', '\n', '\r' };

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

            if (recipe.InputPorts.Count == 0)
            {
                violations.Add(new ValidationViolation("L1-TFR-006", "최소 1개 이상의 입력 포트가 필요합니다.", nameof(recipe.InputPorts)));
            }

            if (recipe.OutputPorts.Count == 0)
            {
                violations.Add(new ValidationViolation("L1-TFR-006", "최소 1개 이상의 출력 포트가 필요합니다.", nameof(recipe.OutputPorts)));
            }

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
    }
}
