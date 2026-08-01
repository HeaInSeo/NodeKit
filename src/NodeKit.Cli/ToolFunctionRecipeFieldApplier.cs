using System;
using System.Globalization;
using System.Text.RegularExpressions;
using NodeKit.Authoring.ToolFunctionRecipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit function-recipe create --non-interactive --field 파서 —
    /// T001 기술 스파이크 결정: RecipeFieldCatalog(평평한 필드명 카탈로그)는
    /// 인덱스/중첩 경로를 지원하지 않으므로, quickstart.md가 요구하는
    /// `InputPorts[0].Name=bam` 같은 문법을 위해 ToolFunctionRecipe 전용
    /// 파서를 새로 둔다(옵션 (a)). RecipeFieldCatalog는 이 목적에 재사용하지
    /// 않는다 — 그 카탈로그는 build-method별 분기가 있는 RecipeDocument 전용
    /// 개념이라 ToolFunctionRecipe의 반복 구조(포트/파라미터/fixture)와
    /// 맞지 않는다.
    /// </summary>
    internal static class ToolFunctionRecipeFieldApplier
    {
        private static readonly Regex _indexSuffix = new(@"\[(\d+)\]\z", RegexOptions.Compiled);

        public static bool TryApply(ToolFunctionRecipe recipe, string name, string value, out string? error)
        {
            error = null;
            var segments = name.Split('.');
            var topKey = StripIndex(segments[0]);

            try
            {
                switch (topKey)
                {
                    case "FunctionId": recipe.FunctionId = value; return true;
                    case "Revision": recipe.Revision = value; return true;
                    case "DisplayLabel": recipe.DisplayLabel = value; return true;
                    case "DisplayDescription": recipe.DisplayDescription = value; return true;
                    case "DisplayCategory": recipe.DisplayCategory = value; return true;
                    case "DisplayTags": recipe.DisplayTags.Add(value); return true;
                    case "ScriptPath": recipe.ScriptPath = value; return true;
                    case "Command": return ApplyCommand(recipe.Command, segments, value, out error);
                    case "InputPorts": return ApplyPort(recipe.InputPorts, PortDirection.Input, segments, value, out error);
                    case "OutputPorts": return ApplyPort(recipe.OutputPorts, PortDirection.Output, segments, value, out error);
                    case "FixtureReferences": return ApplyFixture(recipe.FixtureReferences, segments, value, out error);
                    case "ExpectedResults": return ApplyExpectedResult(recipe.ExpectedResults, segments, value, out error);
                    case "IntermediateFilePolicies": return ApplyIntermediatePolicy(recipe.IntermediateFilePolicies, segments, value, out error);
                    case "Parameters": return ApplyParameter(recipe.Parameters, segments, value, out error);
                    case "EnforcedResources": return ApplyEnforcedResources(recipe.EnforcedResources, segments, value, out error);
                    case "ExecutionEnvironment": return ApplyExecutionEnvironment(recipe.ExecutionEnvironment, segments, value, out error);
                    case "ValidationRequirements": return ApplyValidationRequirements(recipe.ValidationRequirements, segments, value, out error);
                    default:
                        error = $"알 수 없는 필드입니다: {name}";
                        return false;
                }
            }
            catch (FormatException)
            {
                error = $"필드 값 형식이 올바르지 않습니다: {name}={value}";
                return false;
            }
        }

        private static bool ApplyCommand(CommandContract command, string[] segments, string value, out string? error)
        {
            error = null;
            if (segments.Length < 2)
            {
                error = "Command 필드는 Command.Field 형식이어야 합니다.";
                return false;
            }

            switch (segments[1])
            {
                case "Executable": command.Executable = value; return true;
                case "Arguments": command.Arguments.Add(value); return true;
                case "WorkingDirectory": command.WorkingDirectory = value; return true;
                case "SuccessExitCodes": command.SuccessExitCodes.Add(int.Parse(value, CultureInfo.InvariantCulture)); return true;
                default:
                    error = $"알 수 없는 Command 필드입니다: {segments[1]}";
                    return false;
            }
        }

        private static bool ApplyPort(
            System.Collections.Generic.List<PortContract> ports, PortDirection direction, string[] segments, string value, out string? error)
        {
            error = null;
            if (segments.Length < 2 || !TryParseIndex(segments[0], out var index))
            {
                error = $"포트 필드는 {(direction == PortDirection.Input ? "InputPorts" : "OutputPorts")}[N].Field 형식이어야 합니다: {string.Join('.', segments)}";
                return false;
            }

            while (ports.Count <= index)
            {
                ports.Add(new PortContract { Direction = direction });
            }

            var port = ports[index];
            switch (segments[1])
            {
                case "Name": port.Name = value; return true;
                case "DataFormat": port.DataFormat = value; return true;
                case "Cardinality":
                    if (!Enum.TryParse<PortCardinality>(value, ignoreCase: true, out var cardinality))
                    {
                        error = $"Cardinality 값이 올바르지 않습니다: {value} (Single|Multiple)";
                        return false;
                    }

                    port.Cardinality = cardinality;
                    return true;
                case "Required": port.Required = ParseBool(value); return true;
                case "PathPlacementRule": port.PathPlacementRule = value; return true;
                case "CompanionFiles": port.CompanionFiles.Add(value); return true;
                case "PathOrGlob": port.PathOrGlob = value; return true;
                case "CompletionCheck": port.CompletionCheck = value; return true;
                case "DownstreamCompatibilityNote": port.DownstreamCompatibilityNote = value; return true;
                default:
                    error = $"알 수 없는 포트 필드입니다: {segments[1]}";
                    return false;
            }
        }

        private static bool ApplyFixture(
            System.Collections.Generic.List<FixtureReference> fixtures, string[] segments, string value, out string? error)
        {
            error = null;
            if (segments.Length < 2 || !TryParseIndex(segments[0], out var index))
            {
                error = $"fixture 필드는 FixtureReferences[N].Field 형식이어야 합니다: {string.Join('.', segments)}";
                return false;
            }

            while (fixtures.Count <= index)
            {
                fixtures.Add(new FixtureReference());
            }

            var fixture = fixtures[index];
            switch (segments[1])
            {
                case "LocalPath": fixture.LocalPath = value; return true;
                case "ContentDigest": fixture.ContentDigest = value; return true;
                default:
                    error = $"알 수 없는 fixture 필드입니다: {segments[1]}";
                    return false;
            }
        }

        private static bool ApplyExpectedResult(
            System.Collections.Generic.List<ExpectedResult> expectedResults, string[] segments, string value, out string? error)
        {
            error = null;
            if (segments.Length < 2 || !TryParseIndex(segments[0], out var index))
            {
                error = $"예상 결과 필드는 ExpectedResults[N].Field 형식이어야 합니다: {string.Join('.', segments)}";
                return false;
            }

            while (expectedResults.Count <= index)
            {
                expectedResults.Add(new ExpectedResult());
            }

            var expected = expectedResults[index];
            switch (segments[1])
            {
                case "OutputPortName": expected.OutputPortName = value; return true;
                case "ExpectedValueOrRule": expected.ExpectedValueOrRule = value; return true;
                default:
                    error = $"알 수 없는 예상 결과 필드입니다: {segments[1]}";
                    return false;
            }
        }

        private static bool ApplyIntermediatePolicy(
            System.Collections.Generic.List<IntermediateFilePolicyEntry> policies, string[] segments, string value, out string? error)
        {
            error = null;
            if (segments.Length < 2 || !TryParseIndex(segments[0], out var index))
            {
                error = $"중간파일 정책 필드는 IntermediateFilePolicies[N].Field 형식이어야 합니다: {string.Join('.', segments)}";
                return false;
            }

            while (policies.Count <= index)
            {
                policies.Add(new IntermediateFilePolicyEntry());
            }

            var policy = policies[index];
            switch (segments[1])
            {
                case "PathOrPattern": policy.PathOrPattern = value; return true;
                case "Policy":
                    if (!Enum.TryParse<IntermediateFilePolicyKind>(value, ignoreCase: true, out var kind))
                    {
                        error = $"Policy 값이 올바르지 않습니다: {value} (Ephemeral|Cache|Checkpoint|SidecarOutput|SensitiveTemp)";
                        return false;
                    }

                    policy.Policy = kind;
                    return true;
                default:
                    error = $"알 수 없는 중간파일 정책 필드입니다: {segments[1]}";
                    return false;
            }
        }

        private static bool ApplyParameter(
            System.Collections.Generic.List<ParameterContract> parameters, string[] segments, string value, out string? error)
        {
            error = null;
            if (segments.Length < 2 || !TryParseIndex(segments[0], out var index))
            {
                error = $"parameter 필드는 Parameters[N].Field 형식이어야 합니다: {string.Join('.', segments)}";
                return false;
            }

            while (parameters.Count <= index)
            {
                parameters.Add(new ParameterContract());
            }

            var parameter = parameters[index];
            switch (segments[1])
            {
                case "Name": parameter.Name = value; return true;
                case "Type":
                    if (!Enum.TryParse<ParameterType>(value, ignoreCase: true, out var type))
                    {
                        error = $"Type 값이 올바르지 않습니다: {value} (String|Integer|Number|Boolean|Enum)";
                        return false;
                    }

                    parameter.Type = type;
                    return true;
                case "DefaultValue": parameter.DefaultValue = value; return true;
                case "AllowedRange": parameter.AllowedRange = value; return true;
                case "Required": parameter.Required = ParseBool(value); return true;
                case "CliArgumentMapping": parameter.CliArgumentMapping = value; return true;
                case "MutuallyExclusiveGroup": parameter.MutuallyExclusiveGroup = value; return true;
                default:
                    error = $"알 수 없는 parameter 필드입니다: {segments[1]}";
                    return false;
            }
        }

        private static bool ApplyEnforcedResources(ResourceContract resources, string[] segments, string value, out string? error)
        {
            error = null;
            if (segments.Length < 2)
            {
                error = "EnforcedResources 필드는 EnforcedResources.Field 형식이어야 합니다.";
                return false;
            }

            switch (segments[1])
            {
                case "CpuRequest": resources.CpuRequest = value; return true;
                case "CpuLimit": resources.CpuLimit = value; return true;
                case "MemoryRequest": resources.MemoryRequest = value; return true;
                case "MemoryLimit": resources.MemoryLimit = value; return true;
                case "StorageRequest": resources.StorageRequest = value; return true;
                case "StorageLimit": resources.StorageLimit = value; return true;
                case "MaxExecutionTimeSeconds": resources.MaxExecutionTimeSeconds = int.Parse(value, CultureInfo.InvariantCulture); return true;
                case "Parallelism": resources.Parallelism = int.Parse(value, CultureInfo.InvariantCulture); return true;
                default:
                    error = $"알 수 없는 EnforcedResources 필드입니다: {segments[1]}";
                    return false;
            }
        }

        private static bool ApplyExecutionEnvironment(ExecutionEnvironmentContract environment, string[] segments, string value, out string? error)
        {
            error = null;
            if (segments.Length < 2)
            {
                error = "ExecutionEnvironment 필드는 ExecutionEnvironment.Field 형식이어야 합니다.";
                return false;
            }

            switch (segments[1])
            {
                case "SupportedPlatforms": environment.SupportedPlatforms.Add(value); return true;
                case "WritablePaths": environment.WritablePaths.Add(value); return true;
                case "NetworkPolicy": environment.NetworkPolicy = value; return true;
                case "RequiresRoot": environment.RequiresRoot = ParseBool(value); return true;
                case "RequiredCapabilities": environment.RequiredCapabilities.Add(value); return true;
                default:
                    error = $"알 수 없는 ExecutionEnvironment 필드입니다: {segments[1]}";
                    return false;
            }
        }

        private static bool ApplyValidationRequirements(ValidationRequirements requirements, string[] segments, string value, out string? error)
        {
            error = null;
            if (segments.Length < 2)
            {
                error = "ValidationRequirements 필드는 ValidationRequirements.Field 형식이어야 합니다.";
                return false;
            }

            switch (segments[1])
            {
                case "MinimumObservationLevel":
                    if (!Enum.TryParse<ObservationLevel>(value, ignoreCase: true, out var level))
                    {
                        error = $"MinimumObservationLevel 값이 올바르지 않습니다: {value} (Basic|Enhanced|Full)";
                        return false;
                    }

                    requirements.MinimumObservationLevel = level;
                    return true;
                case "RequiredCoverage":
                    if (segments.Length < 3)
                    {
                        error = "RequiredCoverage 필드는 ValidationRequirements.RequiredCoverage.<key> 형식이어야 합니다.";
                        return false;
                    }

                    requirements.RequiredCoverage[segments[2]] = ParseBool(value);
                    return true;
                default:
                    error = $"알 수 없는 ValidationRequirements 필드입니다: {segments[1]}";
                    return false;
            }
        }

        private static bool ParseBool(string value) => value.Trim().ToLowerInvariant() switch
        {
            "true" or "y" or "yes" or "1" => true,
            "false" or "n" or "no" or "0" => false,
            _ => throw new FormatException($"boolean 값이 아닙니다: {value}"),
        };

        private static string StripIndex(string segment)
        {
            var bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
            return bracketIndex < 0 ? segment : segment[..bracketIndex];
        }

        private static bool TryParseIndex(string segment, out int index)
        {
            var match = _indexSuffix.Match(segment);
            if (!match.Success)
            {
                index = -1;
                return false;
            }

            index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return true;
        }
    }
}
