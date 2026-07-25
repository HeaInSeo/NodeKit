using System;
using System.Collections.Generic;
using System.Linq;
using NodeKit.Authoring.ToolFunctionRecipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit function-recipe create 대화형 마법사 — contracts/cli-function-recipe-commands.md
    /// §create 순서(functionId→revision→표시정보→스크립트 경로→command→입력
    /// 포트→출력 포트→fixture→예상 결과→중간파일 정책→parameter→enforced
    /// 자원→실행 환경→validationRequirements)를 그대로 따른다.
    /// RecipeCreateFlow와 달리 build-method 분기가 없는 단일 선형 시퀀스다 —
    /// ToolFunctionRecipe에는 "빌드 방식 선택"이 없기 때문이다(spec.md Key
    /// Entities).
    /// </summary>
    internal static class ToolFunctionRecipeCreateFlow
    {
        public static ToolFunctionRecipe Run(IRecipeConsole console, string toolSpecDigest, string baseToolImageDigest)
        {
            var recipe = new ToolFunctionRecipe
            {
                ToolSpecDigest = toolSpecDigest,
                BaseToolImageDigest = baseToolImageDigest,
            };

            console.BeginStep();
            console.WriteLine("ToolFunctionRecipe 작성");
            console.WriteLine($"  toolSpecDigest: {toolSpecDigest}");
            console.WriteLine($"  baseToolImageDigest: {baseToolImageDigest}");
            console.WriteLine();

            recipe.FunctionId = ReadLine(console, "functionId (예: samtools.sort): ");
            recipe.Revision = ReadLine(console, "revision (예: v1): ");
            recipe.DisplayLabel = ReadLine(console, "표시 이름 (선택, Enter로 건너뜀): ");
            recipe.DisplayDescription = ReadLine(console, "설명 (선택, Enter로 건너뜀): ");
            recipe.DisplayCategory = ReadLine(console, "카테고리 (선택, Enter로 건너뜀): ");
            recipe.DisplayTags = ReadCsv(console, "태그 (쉼표로 구분, 선택, Enter로 건너뜀): ");
            recipe.ScriptPath = ReadLine(console, "스크립트 경로 (로컬 파일 참조): ");

            ReadCommand(console, recipe.Command);
            ReadPorts(console, recipe.InputPorts, PortDirection.Input);
            ReadPorts(console, recipe.OutputPorts, PortDirection.Output);
            ReadFixtures(console, recipe.FixtureReferences);
            ReadExpectedResults(console, recipe.OutputPorts, recipe.ExpectedResults);
            ReadIntermediateFilePolicies(console, recipe.IntermediateFilePolicies);
            ReadParameters(console, recipe.Parameters);
            ReadEnforcedResources(console, recipe.EnforcedResources);
            ReadExecutionEnvironment(console, recipe.ExecutionEnvironment);
            ReadValidationRequirements(console, recipe.ValidationRequirements);

            return recipe;
        }

        private static void ReadCommand(IRecipeConsole console, CommandContract command)
        {
            console.WriteLine();
            console.WriteLine("-- Command --");
            command.Executable = ReadLine(console, "  executable (예: samtools): ");
            command.Arguments = ReadCsv(console, "  arguments (쉼표로 구분, 순서 유지, 선택): ");
            command.WorkingDirectory = ReadLine(console, "  workingDirectory (선택, Enter로 건너뜀): ");

            console.WriteHints("  environment allowlist 항목을 추가하세요 (완료하려면 이름을 비우고 Enter):");
            while (true)
            {
                var name = ReadLine(console, "    환경변수 이름 (완료하려면 Enter): ");
                if (string.IsNullOrEmpty(name))
                {
                    break;
                }

                var source = ReadLine(console, "    출처: ");
                command.Environment.Add(new EnvironmentEntry { Name = name, Source = source });
            }

            var exitCodesRaw = ReadLine(console, "  success exit codes (쉼표로 구분, 기본값 0): ");
            command.SuccessExitCodes = string.IsNullOrEmpty(exitCodesRaw)
                ? new List<int> { 0 }
                : exitCodesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(int.Parse)
                    .ToList();

            var softTimeoutRaw = ReadLine(console, "  soft timeout(초, 선택, Enter로 건너뜀): ");
            var hardTimeoutRaw = ReadLine(console, "  hard timeout(초, 선택, Enter로 건너뜀): ");
            if (!string.IsNullOrEmpty(softTimeoutRaw) || !string.IsNullOrEmpty(hardTimeoutRaw))
            {
                command.TimeoutPolicy = new TimeoutPolicy
                {
                    SoftSeconds = string.IsNullOrEmpty(softTimeoutRaw) ? null : int.Parse(softTimeoutRaw),
                    HardSeconds = string.IsNullOrEmpty(hardTimeoutRaw) ? null : int.Parse(hardTimeoutRaw),
                };
            }
        }

        private static void ReadPorts(IRecipeConsole console, List<PortContract> ports, PortDirection direction)
        {
            var label = direction == PortDirection.Input ? "입력" : "출력";
            console.WriteLine();
            console.WriteLine($"-- {label} 포트 (완료하려면 이름을 비우고 Enter) --");

            while (true)
            {
                var name = ReadLine(console, $"  {label} 포트 이름 (완료하려면 Enter): ");
                if (string.IsNullOrEmpty(name))
                {
                    break;
                }

                var port = new PortContract { Name = name, Direction = direction };
                port.DataFormat = ReadLine(console, "    데이터 형식 (선택): ");
                var cardinalityRaw = ReadLine(console, "    cardinality (single|multiple, 기본 single): ");
                port.Cardinality = string.Equals(cardinalityRaw, "multiple", StringComparison.OrdinalIgnoreCase)
                    ? PortCardinality.Multiple
                    : PortCardinality.Single;

                if (direction == PortDirection.Input)
                {
                    var requiredRaw = ReadLine(console, "    필수 여부 (y/n, 기본 y): ");
                    port.Required = !string.Equals(requiredRaw, "n", StringComparison.OrdinalIgnoreCase);
                    port.PathPlacementRule = ReadLine(console, "    경로 배치 규칙 (선택): ");
                    port.CompanionFiles = ReadCsv(console, "    companion files (쉼표로 구분, 선택): ");
                }
                else
                {
                    port.PathOrGlob = ReadLine(console, "    출력 경로/glob: ");
                    port.CompletionCheck = ReadLine(console, "    완료 검증법 (선택): ");
                    port.DownstreamCompatibilityNote = ReadLine(console, "    downstream 호환성 메모 (선택): ");
                }

                ports.Add(port);
            }
        }

        private static void ReadFixtures(IRecipeConsole console, List<FixtureReference> fixtures)
        {
            console.WriteLine();
            console.WriteLine("-- Fixture 참조 (완료하려면 경로/digest를 비우고 Enter) --");

            while (true)
            {
                var localPath = ReadLine(console, "  로컬 경로 (완료하려면 Enter): ");
                if (string.IsNullOrEmpty(localPath))
                {
                    var contentDigest = ReadLine(console, "  content digest (로컬 경로 대신, 완료하려면 Enter): ");
                    if (string.IsNullOrEmpty(contentDigest))
                    {
                        break;
                    }

                    fixtures.Add(new FixtureReference { ContentDigest = contentDigest });
                    continue;
                }

                fixtures.Add(new FixtureReference { LocalPath = localPath });
            }
        }

        private static void ReadExpectedResults(IRecipeConsole console, List<PortContract> outputPorts, List<ExpectedResult> expectedResults)
        {
            if (outputPorts.Count == 0)
            {
                return;
            }

            console.WriteLine();
            console.WriteLine("-- 예상 결과 (출력 포트당, 선택) --");
            foreach (var port in outputPorts)
            {
                var rule = ReadLine(console, $"  '{port.Name}' 예상 결과/비교 규칙 (선택, Enter로 건너뜀): ");
                if (!string.IsNullOrEmpty(rule))
                {
                    expectedResults.Add(new ExpectedResult { OutputPortName = port.Name, ExpectedValueOrRule = rule });
                }
            }
        }

        private static void ReadIntermediateFilePolicies(IRecipeConsole console, List<IntermediateFilePolicyEntry> policies)
        {
            console.WriteLine();
            console.WriteLine("-- 중간파일 정책 (선택, 완료하려면 경로를 비우고 Enter) --");

            while (true)
            {
                var pathOrPattern = ReadLine(console, "  경로/패턴 (완료하려면 Enter): ");
                if (string.IsNullOrEmpty(pathOrPattern))
                {
                    break;
                }

                var policyRaw = ReadLine(console, "  정책 (ephemeral|cache|checkpoint|sidecaroutput|sensitivetemp): ");
                if (!Enum.TryParse<IntermediateFilePolicyKind>(policyRaw, ignoreCase: true, out var policy))
                {
                    policy = IntermediateFilePolicyKind.Ephemeral;
                }

                policies.Add(new IntermediateFilePolicyEntry { PathOrPattern = pathOrPattern, Policy = policy });
            }
        }

        private static void ReadParameters(IRecipeConsole console, List<ParameterContract> parameters)
        {
            console.WriteLine();
            console.WriteLine("-- Parameter (선택, 완료하려면 이름을 비우고 Enter) --");

            while (true)
            {
                var name = ReadLine(console, "  이름 (완료하려면 Enter): ");
                if (string.IsNullOrEmpty(name))
                {
                    break;
                }

                var typeRaw = ReadLine(console, "  타입 (string|integer|number|boolean|enum, 기본 string): ");
                if (!Enum.TryParse<ParameterType>(typeRaw, ignoreCase: true, out var type))
                {
                    type = ParameterType.String;
                }

                var parameter = new ParameterContract
                {
                    Name = name,
                    Type = type,
                    DefaultValue = ReadLine(console, "  기본값 (선택): "),
                    AllowedRange = ReadLine(console, "  허용 범위 (선택): "),
                    CliArgumentMapping = ReadLine(console, "  CLI 인자 매핑 (선택): "),
                    MutuallyExclusiveGroup = ReadLine(console, "  상호배타 그룹 (선택): "),
                };

                var requiredRaw = ReadLine(console, "  필수 여부 (y/n, 기본 n): ");
                parameter.Required = string.Equals(requiredRaw, "y", StringComparison.OrdinalIgnoreCase);

                parameters.Add(parameter);
            }
        }

        private static void ReadEnforcedResources(IRecipeConsole console, ResourceContract resources)
        {
            console.WriteLine();
            console.WriteLine("-- Enforced 자원 --");
            resources.CpuRequest = ReadLine(console, "  CPU request (예: 500m): ");
            resources.CpuLimit = ReadLine(console, "  CPU limit (예: 2000m): ");
            resources.MemoryRequest = ReadLine(console, "  Memory request (예: 256Mi): ");
            resources.MemoryLimit = ReadLine(console, "  Memory limit (예: 1Gi): ");
            resources.StorageRequest = ReadLine(console, "  Storage request (선택): ");
            resources.StorageLimit = ReadLine(console, "  Storage limit (선택): ");

            var maxExecRaw = ReadLine(console, "  최대 실행시간(초, 선택): ");
            resources.MaxExecutionTimeSeconds = string.IsNullOrEmpty(maxExecRaw) ? null : int.Parse(maxExecRaw);

            var parallelismRaw = ReadLine(console, "  병렬성(선택): ");
            resources.Parallelism = string.IsNullOrEmpty(parallelismRaw) ? null : int.Parse(parallelismRaw);
        }

        private static void ReadExecutionEnvironment(IRecipeConsole console, ExecutionEnvironmentContract environment)
        {
            console.WriteLine();
            console.WriteLine("-- 실행 환경 (선택) --");
            environment.SupportedPlatforms = ReadCsv(console, "  지원 플랫폼 (쉼표로 구분, 예: linux/amd64): ");
            environment.WritablePaths = ReadCsv(console, "  writable paths (쉼표로 구분): ");
            environment.NetworkPolicy = ReadLine(console, "  network policy (선택): ");
            var requiresRootRaw = ReadLine(console, "  root 권한 필요 여부 (y/n, 기본 n): ");
            environment.RequiresRoot = string.Equals(requiresRootRaw, "y", StringComparison.OrdinalIgnoreCase);
            environment.RequiredCapabilities = ReadCsv(console, "  필요한 capability (쉼표로 구분): ");
        }

        private static void ReadValidationRequirements(IRecipeConsole console, ValidationRequirements requirements)
        {
            console.WriteLine();
            console.WriteLine("-- Validation Requirements (선택) --");
            var levelRaw = ReadLine(console, "  minimum observation level (basic|enhanced|full, 선택): ");
            if (Enum.TryParse<ObservationLevel>(levelRaw, ignoreCase: true, out var level))
            {
                requirements.MinimumObservationLevel = level;
            }

            foreach (var coverage in new[] { "resourceSamples", "processEvents", "fileEvents", "networkEvents" })
            {
                var raw = ReadLine(console, $"  requiredCoverage.{coverage} (y/n, 기본 n): ");
                if (string.Equals(raw, "y", StringComparison.OrdinalIgnoreCase))
                {
                    requirements.RequiredCoverage[coverage] = true;
                }
            }
        }

        private static string ReadLine(IRecipeConsole console, string prompt)
        {
            console.Write(prompt);
            return (console.ReadLine() ?? string.Empty).Trim();
        }

        private static List<string> ReadCsv(IRecipeConsole console, string prompt)
        {
            var raw = ReadLine(console, prompt);
            return string.IsNullOrEmpty(raw)
                ? new List<string>()
                : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }
}
