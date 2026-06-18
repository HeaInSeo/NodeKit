using System.Collections.Generic;
using System.Linq;
using NodeKit.Authoring;
using NodeKit.Policy;
using NodeKit.Validation;
using ReactiveUI;

namespace NodeKit.UI.ViewModels
{
    internal sealed class ValidationViewModel : ReactiveObject
    {
        private readonly ImageUriValidator _imageUriValidator;
        private readonly DockerfileStructureValidator _dockerfileStructureValidator;
        private readonly PackageVersionValidator _packageVersionValidator;
        private readonly RequiredFieldsValidator _requiredFieldsValidator;
        private readonly ValidatedDefinitionState _validatedDefinitionState;
        private IPolicyChecker? _policyChecker;
        private IReadOnlyList<ValidationViolation> _violations = new List<ValidationViolation>();
        private string _statusMessage = string.Empty;
        private bool _isValidationPassVisible;
        private bool _isValidationResultVisible;
        private bool _canSubmitBuild;

        public ValidationViewModel(
            RequiredFieldsValidator requiredFieldsValidator,
            ImageUriValidator imageUriValidator,
            DockerfileStructureValidator dockerfileStructureValidator,
            PackageVersionValidator packageVersionValidator,
            ValidatedDefinitionState validatedDefinitionState,
            IPolicyChecker? policyChecker)
        {
            _requiredFieldsValidator = requiredFieldsValidator;
            _imageUriValidator = imageUriValidator;
            _dockerfileStructureValidator = dockerfileStructureValidator;
            _packageVersionValidator = packageVersionValidator;
            _validatedDefinitionState = validatedDefinitionState;
            _policyChecker = policyChecker;
        }

        public IReadOnlyList<ValidationViolation> Violations
        {
            get => _violations;
            private set => this.RaiseAndSetIfChanged(ref _violations, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool IsValidationPassVisible
        {
            get => _isValidationPassVisible;
            private set => this.RaiseAndSetIfChanged(ref _isValidationPassVisible, value);
        }

        public bool IsValidationResultVisible
        {
            get => _isValidationResultVisible;
            private set => this.RaiseAndSetIfChanged(ref _isValidationResultVisible, value);
        }

        public bool CanSubmitBuild
        {
            get => _canSubmitBuild;
            private set => this.RaiseAndSetIfChanged(ref _canSubmitBuild, value);
        }

        public bool HasValidatedDefinition => _validatedDefinitionState.HasValidatedDefinition;

        public void SetPolicyChecker(IPolicyChecker? policyChecker)
        {
            _policyChecker = policyChecker;
        }

        public void Invalidate()
        {
            _validatedDefinitionState.Invalidate();
            CanSubmitBuild = false;
        }

        public void MarkDefinitionChanged()
        {
            Invalidate();
            IsValidationPassVisible = false;
            StatusMessage = "입력값이 검증 이후 변경되었습니다. 다시 L1 검증을 실행하세요.";
        }

        public bool Matches(ToolDefinition definition) =>
            _validatedDefinitionState.Matches(definition);

        public void Validate(ToolDefinition definition)
        {
            Invalidate();
            StatusMessage = "검증 중...";

            var staticResults = new[]
            {
                _requiredFieldsValidator.Validate(definition),
                _imageUriValidator.Validate(definition),
                _dockerfileStructureValidator.Validate(definition),
                _packageVersionValidator.Validate(definition),
            };
            var staticCombined = ValidationResult.Combine(staticResults);
            var allViolations = new List<ValidationViolation>(staticCombined.Violations);

            if (!string.IsNullOrWhiteSpace(definition.DockerfileContent))
            {
                AddPolicyViolations(definition.DockerfileContent, allViolations);
            }

            Violations = allViolations;
            if (allViolations.Count == 0)
            {
                IsValidationResultVisible = false;
                IsValidationPassVisible = true;
                _validatedDefinitionState.MarkValidated(definition);
                CanSubmitBuild = true;
                StatusMessage = "L1 검증 통과 - 빌드 요청 준비 완료";
                return;
            }

            IsValidationPassVisible = false;
            IsValidationResultVisible = true;
            StatusMessage = $"L1 검증 실패 - {allViolations.Count}개 위반";
        }

        private void AddPolicyViolations(string dockerfileContent, List<ValidationViolation> allViolations)
        {
            if (_policyChecker == null)
            {
                allViolations.Add(new ValidationViolation(
                    "POLICY-UNAVAIL",
                    "DockGuard 정책 번들을 로드할 수 없습니다 (assets/policy/dockguard.wasm 확인 필요).",
                    "DockerfileContent"));
                return;
            }

            var policyResult = _policyChecker.Check(dockerfileContent);
            allViolations.AddRange(policyResult.Violations.Select(pv =>
                new ValidationViolation(pv.RuleId, pv.Message, "DockerfileContent")));
        }
    }
}
