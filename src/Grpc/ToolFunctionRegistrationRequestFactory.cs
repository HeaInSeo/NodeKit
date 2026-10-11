using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NodeKit.Authoring.ToolFunctionRecipes;
using Pb = Nodevault.V1;

namespace NodeKit.Grpc
{
    /// <summary>
    /// P06.raw_typed: ToolFunctionRecipe를 RegisterToolFunctionRequest로 옮기는
    /// 명시적 mapper (v0.9.1 계약 S3-02-A/D/E/F, S3-06; field map은
    /// tests/NodeKit.Cli.Tests/Fixtures/Contract/toolfunction-persisted.json).
    ///
    /// spec(digest 입력)에는 실행 필드만, presentation에는 표시와 output port note,
    /// validation_policy에는 fixture/expected/validation 요구, environment_hints에는
    /// 지원 platform과 enforced 자원을 넣는다. FunctionId/Revision/State/ScriptPath/
    /// BaseToolImageDigest는 wire에 없다. local enum은 0부터 시작하므로 숫자 cast를
    /// 하지 않고 이름별 표로 옮기며, 표에 없는 값은 proto default로 바꾸지 않고
    /// 거부한다. 지정하지 않은 MinimumObservationLevel은 UNSPECIFIED(=canonical
    /// JSON에서 생략)로 두고 Basic을 임의로 채우지 않는다.
    ///
    /// client는 identity(tool_function_digest/cas_hash)나 canonical hash를 계산하지
    /// 않는다. RPC도 하지 않는다 — 실제 전송은 P07/P08 caller 소관이다.
    /// </summary>
    internal static class ToolFunctionRegistrationRequestFactory
    {
        public const string CanonicalizationVersion = "w2-set-v1";

        public const string MissingRequestIdCode = "MISSING_REQUEST_ID";
        public const string MissingToolSpecDigestCode = "MISSING_TOOL_SPEC_DIGEST";
        public const string InvalidToolSpecDigestCode = "INVALID_TOOL_SPEC_DIGEST";
        public const string MissingFunctionImageDigestCode = "MISSING_FUNCTION_IMAGE_DIGEST";
        public const string InvalidFunctionImageDigestCode = "INVALID_FUNCTION_IMAGE_DIGEST";
        public const string UnknownEnumValueCode = "UNKNOWN_ENUM_VALUE";
        public const string DuplicateOutputPresentationCode = "DUPLICATE_OUTPUT_PRESENTATION";
        public const string OrphanOutputPresentationCode = "ORPHAN_OUTPUT_PRESENTATION";
        public const string PortDirectionMismatchCode = "PORT_DIRECTION_MISMATCH";
        public const string NullEnvironmentFieldCode = "NULL_ENVIRONMENT_FIELD";

        // C1은 ResolvedToolSpec의 ToolSpecDigest — 'sha256:' 접두 없는 64자리 소문자 hex.
        private static readonly Regex _toolSpecDigestPattern =
            new(@"\A[0-9a-f]{64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // B는 이미지 digest — raw_spec base_image_digest와 같은 형식.
        private static readonly Regex _imageDigestPattern =
            new(@"\Asha256:[0-9a-fA-F]{64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryBuild(
            ToolFunctionRecipe recipe,
            ToolFunctionRegistrationInputs inputs,
            out ToolFunctionRegistrationPlan? plan,
            out ToolFunctionWireError? error)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            ArgumentNullException.ThrowIfNull(inputs);

            plan = null;
            if (string.IsNullOrWhiteSpace(inputs.RequestId))
            {
                error = new ToolFunctionWireError(MissingRequestIdCode, "등록 요청 ID가 없습니다.", 1);
                return false;
            }

            if (!TryCheckToolSpecDigest(inputs.ToolSpecDigest, out error)
                || !TryCheckFunctionImageDigest(inputs.FunctionImageDigest, out error))
            {
                return false;
            }

            var errors = new MappingErrors();
            var request = new Pb.RegisterToolFunctionRequest
            {
                RequestId = inputs.RequestId,
                BaseToolSpecDigest = inputs.ToolSpecDigest!,
                ImageDigest = inputs.FunctionImageDigest!,
                Spec = MapSpec(recipe, errors),
                Presentation = MapPresentation(recipe, errors),
                ValidationPolicy = MapValidationPolicy(recipe, errors),
                EnvironmentHints = MapEnvironmentHints(recipe),
                CanonicalizationVersion = CanonicalizationVersion,
            };

            if (errors.First is not null)
            {
                error = errors.First;
                return false;
            }

            plan = new ToolFunctionRegistrationPlan(request, inputs.SecondRequestBasis);
            return true;
        }

        /// <summary>
        /// typed 등록 전 C1 형식 guard (S3-02-E/F). 접두 제거·대소문자 변환·A/B/C2
        /// fallback 없이 값 그대로 검사한다. 형식만 보며 C1의 실재/연결은 판정하지 않는다.
        /// </summary>
        public static bool TryCheckToolSpecDigest(string? toolSpecDigest, out ToolFunctionWireError? error)
        {
            error = null;
            if (string.IsNullOrEmpty(toolSpecDigest))
            {
                error = new ToolFunctionWireError(
                    MissingToolSpecDigestCode,
                    "typed 등록에 필요한 기반 ToolSpec digest(toolSpecDigest, C1)가 없습니다. 이미지 digest나 두 번째 빌드 값에서 추정하지 않습니다 — Recipe의 toolSpecDigest 또는 --tool-spec-digest로 지정하세요.",
                    1);
                return false;
            }

            if (!_toolSpecDigestPattern.IsMatch(toolSpecDigest))
            {
                error = new ToolFunctionWireError(
                    InvalidToolSpecDigestCode,
                    $"toolSpecDigest(C1)는 'sha256:' 접두 없는 소문자 16진수 64자리여야 합니다: '{toolSpecDigest}'. 이미지 digest(sha256:...)는 C1이 아닙니다.",
                    1);
                return false;
            }

            return true;
        }

        /// <summary>
        /// B(두 번째 이미지)가 생기기 전에 typed 등록 mapping 전제를 미리 확인한다
        /// (S3-09 current preflight). TryBuild와 같은 mapper를 돌려 unknown enum,
        /// output note 중복/고아, port 방향 오류를 찾고 만든 객체는 버린다. C1 형식은
        /// TryCheckToolSpecDigest가 따로 본다.
        /// </summary>
        public static bool TryCheckSpecMapping(ToolFunctionRecipe recipe, out ToolFunctionWireError? error)
        {
            ArgumentNullException.ThrowIfNull(recipe);

            var errors = new MappingErrors();
            _ = MapSpec(recipe, errors);
            _ = MapPresentation(recipe, errors);
            _ = MapValidationPolicy(recipe, errors);
            error = errors.First;
            return error is null;
        }

        // ── local enum → proto enum: 이름별 명시 표. local은 0부터, proto는 0=UNSPECIFIED
        //    이므로 숫자 cast는 한 칸씩 어긋난다. 표에 없는 값은 null(=거부)이다(S3-06-B/E).
        internal static Pb.Cardinality? MapCardinality(PortCardinality value) => value switch
        {
            PortCardinality.Single => Pb.Cardinality.Single,
            PortCardinality.Multiple => Pb.Cardinality.Multiple,
            _ => null,
        };

        internal static Pb.ParameterType? MapParameterType(ParameterType value) => value switch
        {
            ParameterType.String => Pb.ParameterType.String,
            ParameterType.Integer => Pb.ParameterType.Integer,
            ParameterType.Number => Pb.ParameterType.Number,
            ParameterType.Boolean => Pb.ParameterType.Boolean,
            ParameterType.Enum => Pb.ParameterType.Enum,
            _ => null,
        };

        internal static Pb.IntermediateFilePolicyKind? MapIntermediateFilePolicyKind(IntermediateFilePolicyKind value) => value switch
        {
            IntermediateFilePolicyKind.Ephemeral => Pb.IntermediateFilePolicyKind.Ephemeral,
            IntermediateFilePolicyKind.Cache => Pb.IntermediateFilePolicyKind.Cache,
            IntermediateFilePolicyKind.Checkpoint => Pb.IntermediateFilePolicyKind.Checkpoint,
            IntermediateFilePolicyKind.SidecarOutput => Pb.IntermediateFilePolicyKind.SidecarOutput,
            IntermediateFilePolicyKind.SensitiveTemp => Pb.IntermediateFilePolicyKind.SensitiveTemp,
            _ => null,
        };

        internal static Pb.ObservationLevel? MapObservationLevel(ObservationLevel value) => value switch
        {
            ObservationLevel.Basic => Pb.ObservationLevel.Basic,
            ObservationLevel.Enhanced => Pb.ObservationLevel.Enhanced,
            ObservationLevel.Full => Pb.ObservationLevel.Full,
            _ => null,
        };

        private static bool TryCheckFunctionImageDigest(string? imageDigest, out ToolFunctionWireError? error)
        {
            error = null;
            if (string.IsNullOrEmpty(imageDigest))
            {
                error = new ToolFunctionWireError(
                    MissingFunctionImageDigestCode,
                    "두 번째(function) 이미지 digest(B)가 없습니다. 기반 이미지 digest로 대신하지 않습니다.",
                    1);
                return false;
            }

            if (!_imageDigestPattern.IsMatch(imageDigest))
            {
                error = new ToolFunctionWireError(
                    InvalidFunctionImageDigestCode,
                    $"두 번째 이미지 digest(B)는 'sha256:' 뒤에 16진수 64자리여야 합니다: '{imageDigest}'.",
                    1);
                return false;
            }

            return true;
        }

        private static Pb.ToolFunctionSpec MapSpec(ToolFunctionRecipe recipe, MappingErrors errors)
        {
            var command = recipe.Command;
            var pbCommand = new Pb.CommandContract
            {
                Executable = command.Executable,
                WorkingDirectory = command.WorkingDirectory,
            };
            pbCommand.Arguments.AddRange(command.Arguments);

            // EnvironmentEntry는 Normalize가 없어 JSON의 null이 그대로 남는다. protobuf
            // string setter는 null에서 ArgumentNullException을 던지므로 그 전에 거부한다.
            // 빈 문자열로 바꿔 채우지 않는다.
            for (var i = 0; i < command.Environment.Count; i++)
            {
                var entry = command.Environment[i];
                if (entry.Name is null || entry.Source is null)
                {
                    errors.Add(new ToolFunctionWireError(
                        NullEnvironmentFieldCode,
                        $"Command.Environment[{i}]의 name/source가 null입니다. 값을 채우거나 해당 항목을 지우세요.",
                        1));
                    continue;
                }

                pbCommand.Environment.Add(new Pb.EnvironmentEntry { Name = entry.Name, Source = entry.Source });
            }

            pbCommand.SuccessExitCodes.AddRange(command.SuccessExitCodes);

            // 미지정(null)과 0은 모두 unset이다(N1). 두 값이 모두 unset인 빈 그룹
            // ({} / {"softSeconds":0})은 timeout 없음과 같은 의미이므로 timeout_policy를
            // 아예 만들지 않는다 — NodeVault canonicalizer는 present-empty와 absent를
            // 다른 digest로 구분한다(S3-06-C).
            var softSeconds = command.TimeoutPolicy?.SoftSeconds ?? 0;
            var hardSeconds = command.TimeoutPolicy?.HardSeconds ?? 0;
            if (softSeconds != 0 || hardSeconds != 0)
            {
                pbCommand.TimeoutPolicy = new Pb.TimeoutPolicy
                {
                    SoftSeconds = softSeconds,
                    HardSeconds = hardSeconds,
                };
            }

            var spec = new Pb.ToolFunctionSpec { Command = pbCommand };

            // Direction은 PortContract의 판별자다. 담긴 목록과 다르면 어느 쪽 의미로
            // 등록할지 정할 수 없으므로 목록 기준으로 조용히 옮기지 않고 거부한다.
            errors.RequireDirection(recipe.InputPorts, PortDirection.Input, nameof(recipe.InputPorts));
            errors.RequireDirection(recipe.OutputPorts, PortDirection.Output, nameof(recipe.OutputPorts));

            // PortContract는 입력/출력 공용 모양이다. 각 방향에서 의미 있는 필드만
            // 옮긴다 — Required/PathPlacementRule/CompanionFiles는 입력 전용,
            // PathOrGlob/CompletionCheck는 출력 전용(PortContract 주석). 출력 note는
            // digest 밖 presentation 소속이다.
            spec.Inputs.AddRange(recipe.InputPorts.Select(port =>
            {
                var pbPort = new Pb.FunctionPortSpec
                {
                    Name = port.Name,
                    DataFormat = port.DataFormat,
                    Cardinality = errors.Require(MapCardinality(port.Cardinality), nameof(PortCardinality), (int)port.Cardinality),
                    Required = port.Required,
                    PathPlacementRule = port.PathPlacementRule,
                };
                pbPort.CompanionFiles.AddRange(port.CompanionFiles);
                return pbPort;
            }));
            spec.Outputs.AddRange(recipe.OutputPorts.Select(port => new Pb.FunctionPortSpec
            {
                Name = port.Name,
                DataFormat = port.DataFormat,
                Cardinality = errors.Require(MapCardinality(port.Cardinality), nameof(PortCardinality), (int)port.Cardinality),
                PathOrGlob = port.PathOrGlob,
                CompletionCheck = port.CompletionCheck,
            }));
            spec.Parameters.AddRange(recipe.Parameters.Select(parameter => new Pb.ParameterSpec
            {
                Name = parameter.Name,
                Type = errors.Require(MapParameterType(parameter.Type), nameof(ParameterType), (int)parameter.Type),
                DefaultValue = parameter.DefaultValue,
                AllowedRange = parameter.AllowedRange,
                Required = parameter.Required,
                CliArgumentMapping = parameter.CliArgumentMapping,
                MutuallyExclusiveGroup = parameter.MutuallyExclusiveGroup,
            }));
            spec.IntermediateFilePolicies.AddRange(recipe.IntermediateFilePolicies.Select(policy => new Pb.IntermediateFilePolicy
            {
                PathOrPattern = policy.PathOrPattern,
                Policy = errors.Require(MapIntermediateFilePolicyKind(policy.Policy), nameof(IntermediateFilePolicyKind), (int)policy.Policy),
            }));

            // supported_platforms는 digest 밖 environment_hints 소속이다(proto 주석).
            var environment = recipe.ExecutionEnvironment;
            var pbEnvironment = new Pb.ExecutionEnvironmentSpec
            {
                NetworkPolicy = environment.NetworkPolicy,
                RequiresRoot = environment.RequiresRoot,
            };
            pbEnvironment.WritablePaths.AddRange(environment.WritablePaths);
            pbEnvironment.RequiredCapabilities.AddRange(environment.RequiredCapabilities);
            spec.ExecutionEnvironment = pbEnvironment;
            return spec;
        }

        private static Pb.ToolFunctionPresentation MapPresentation(ToolFunctionRecipe recipe, MappingErrors errors)
        {
            var presentation = new Pb.ToolFunctionPresentation
            {
                Label = recipe.DisplayLabel,
                Description = recipe.DisplayDescription,
                Category = recipe.DisplayCategory,
            };
            presentation.Tags.AddRange(recipe.DisplayTags);

            // note는 출력 port에만 붙는다(W1-CQ4 referential integrity). 입력 port의 note는
            // 가리킬 output이 없는 orphan이다. note가 없으면 entry를 생략한다.
            foreach (var port in recipe.InputPorts.Where(port => !string.IsNullOrEmpty(port.DownstreamCompatibilityNote)))
            {
                errors.Add(new ToolFunctionWireError(
                    OrphanOutputPresentationCode,
                    $"downstream compatibility note는 출력 port에만 둘 수 있습니다: 입력 port '{port.Name}'.",
                    1));
            }

            foreach (var port in recipe.OutputPorts.Where(port => !string.IsNullOrEmpty(port.DownstreamCompatibilityNote)))
            {
                // port_name은 outputs 중 정확히 하나와 맞아야 하고 entry는 이름마다 하나다.
                if (recipe.OutputPorts.Count(other => string.Equals(other.Name, port.Name, StringComparison.Ordinal)) != 1)
                {
                    errors.Add(new ToolFunctionWireError(
                        DuplicateOutputPresentationCode,
                        $"note가 가리키는 출력 port 이름이 하나로 정해지지 않습니다: '{port.Name}'.",
                        1));
                    continue;
                }

                presentation.OutputPortPresentations.Add(new Pb.OutputPortPresentation
                {
                    PortName = port.Name,
                    DownstreamCompatibilityNote = port.DownstreamCompatibilityNote,
                });
            }

            return presentation;
        }

        private static Pb.ToolFunctionValidationPolicy MapValidationPolicy(ToolFunctionRecipe recipe, MappingErrors errors)
        {
            var policy = new Pb.ToolFunctionValidationPolicy();
            policy.FixtureReferences.AddRange(recipe.FixtureReferences.Select(fixture => new Pb.FixtureReference
            {
                LocalPath = fixture.LocalPath,
                ContentDigest = fixture.ContentDigest,
            }));
            policy.ExpectedResults.AddRange(recipe.ExpectedResults.Select(expected => new Pb.ExpectedResult
            {
                OutputPortName = expected.OutputPortName,
                ExpectedValueOrRule = expected.ExpectedValueOrRule,
            }));

            var requirements = recipe.ValidationRequirements;
            var pbRequirements = new Pb.ValidationRequirements
            {
                MinimumObservationLevel = requirements.MinimumObservationLevel is { } level
                    ? errors.Require(MapObservationLevel(level), nameof(ObservationLevel), (int)level)
                    : Pb.ObservationLevel.Unspecified,
            };
            pbRequirements.RequiredCoverage.Add(requirements.RequiredCoverage);
            policy.ValidationRequirements = pbRequirements;
            return policy;
        }

        private static Pb.ToolFunctionEnvironmentHints MapEnvironmentHints(ToolFunctionRecipe recipe)
        {
            var resources = recipe.EnforcedResources;
            var hints = new Pb.ToolFunctionEnvironmentHints
            {
                EnforcedResources = new Pb.ResourceContract
                {
                    CpuRequest = resources.CpuRequest,
                    CpuLimit = resources.CpuLimit,
                    MemoryRequest = resources.MemoryRequest,
                    MemoryLimit = resources.MemoryLimit,
                    StorageRequest = resources.StorageRequest,
                    StorageLimit = resources.StorageLimit,
                    MaxExecutionTimeSeconds = resources.MaxExecutionTimeSeconds ?? 0,
                    Parallelism = resources.Parallelism ?? 0,
                },
            };
            hints.SupportedPlatforms.AddRange(recipe.ExecutionEnvironment.SupportedPlatforms);
            return hints;
        }

        /// <summary>mapping 중 첫 실패를 모은다. 실패가 하나라도 있으면 요청을 돌려주지 않는다.</summary>
        private sealed class MappingErrors
        {
            public ToolFunctionWireError? First { get; private set; }

            public void Add(ToolFunctionWireError error) => First ??= error;

            public void RequireDirection(IEnumerable<PortContract> ports, PortDirection expected, string listName)
            {
                foreach (var port in ports.Where(port => port.Direction != expected))
                {
                    Add(new ToolFunctionWireError(
                        PortDirectionMismatchCode,
                        $"{listName}의 port '{port.Name}' direction이 {port.Direction}입니다. {expected}여야 합니다.",
                        1));
                }
            }

            public T Require<T>(T? mapped, string enumName, int value)
                where T : struct, Enum
            {
                if (mapped is { } result)
                {
                    return result;
                }

                Add(new ToolFunctionWireError(
                    UnknownEnumValueCode,
                    $"{enumName}에 정의되지 않은 값입니다: {value}. proto 기본값으로 바꾸지 않습니다.",
                    1));
                return default;
            }
        }
    }
}
