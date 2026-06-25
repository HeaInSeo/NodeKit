using System;
using System.Collections.Generic;
using System.Linq;
using NodeKit.Validation;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Stateful, step-by-step recipe authoring session — see design doc
    /// Sections 15-21: forward progress (SelectMethod/SetField/...), method
    /// revision (ChangeMethod), and final-validation-failure recovery
    /// (BuildRecoveryPlan).
    /// </summary>
    internal sealed class RecipeAuthoringSession
    {
        private static readonly Dictionary<string, string[]> _renderedFieldToCatalogFields =
            new(StringComparer.Ordinal)
            {
                ["Name"] = new[] { "ToolName" },
                ["Version"] = new[] { "ToolVersion" },
                ["Script"] = new[] { "Script" },
                ["ImageUri"] = new[] { "ImageRef", "ImageDigest" },
                ["BioContainerImageUri"] = new[] { "ImageRef", "ImageDigest" },
                ["BaseImage"] = new[] { "ImageRef" },
                ["Packages"] = new[] { "Packages" },
                ["Channels"] = new[] { "Channels" },
                ["PackageMirrorUri"] = new[] { "MirrorUri" },
                ["SourceUri"] = new[] { "SourceUri" },
                ["SourceChecksum"] = new[] { "SourceChecksum" },
                ["SourceBuildCommands"] = new[] { "SourceBuildCommands" },
                ["DockerfileContent"] = new[] { "DockerfileContent" },
                ["DockerfilePath"] = new[] { "DockerfilePath" },
                ["Command"] = new[] { "Command" },
            };

        private static readonly string[] _toolNameVersionFields = { "ToolName", "ToolVersion" };

        private static readonly string[] _inputsOutputsFields = { "Inputs", "Outputs" };

        private readonly RecipeDocument _document = new();
        private readonly HashSet<string> _filledFields = new(StringComparer.Ordinal);
        private readonly HashSet<string> _skippedOptionalFields = new(StringComparer.Ordinal);
        private readonly HashSet<string> _completedListFields = new(StringComparer.Ordinal);
        private readonly HashSet<string> _invalidatedFields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> _scalarValues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<object>> _listItems = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _appliedListItemCounts = new(StringComparer.Ordinal);
        private readonly HashSet<string> _dirtyListFields = new(StringComparer.Ordinal);

        private RecipeMethodId? _selectedMethod;
        private RecipeAuthoringSessionMetadata _metadata = new();

        public bool IsMethodSelected => _selectedMethod.HasValue;

        public RecipeAuthoringSessionMetadata Metadata => _metadata;

        public bool IsComplete =>
            _selectedMethod.HasValue
            && RecipeFieldCatalog.FieldsFor(_selectedMethod.Value).All(IsFieldComplete);

        public void SelectMethod(RecipeMethodId method)
        {
            if (_selectedMethod.HasValue)
            {
                throw new InvalidOperationException(
                    "Method already selected. Use ChangeMethod to switch methods.");
            }

            _selectedMethod = method;
        }

        public RecipeFieldDescriptor? NextField()
        {
            EnsureMethodSelected();
            return RecipeFieldCatalog.FieldsFor(_selectedMethod!.Value).FirstOrDefault(f => !IsFieldComplete(f));
        }

        public IReadOnlyList<ValidationViolation> SetField(string fieldName, object value)
        {
            EnsureMethodSelected();
            var field = GetField(fieldName);

            if (IsListType(field))
            {
                throw new InvalidOperationException($"{fieldName} is a list field — use AppendListItem.");
            }

            var violations = QuickValidate(field, value);
            if (violations.Length > 0)
            {
                return violations;
            }

            field.Apply(_document, value);
            _filledFields.Add(fieldName);
            _scalarValues[fieldName] = value;
            _invalidatedFields.Remove(fieldName);
            return Array.Empty<ValidationViolation>();
        }

        public IReadOnlyList<ValidationViolation> AppendListItem(string fieldName, object item)
        {
            EnsureMethodSelected();
            var field = GetField(fieldName);

            if (!IsListType(field))
            {
                throw new InvalidOperationException($"{fieldName} is not a list field — use SetField.");
            }

            var violations = QuickValidate(field, item);
            if (violations.Length > 0)
            {
                return violations;
            }

            GetOrCreateList(fieldName).Add(item);
            _invalidatedFields.Remove(fieldName);
            return Array.Empty<ValidationViolation>();
        }

        public IReadOnlyList<ValidationViolation> EditListItem(string fieldName, int index, object newValue)
        {
            EnsureMethodSelected();
            var field = GetField(fieldName);

            if (!IsListType(field))
            {
                throw new InvalidOperationException($"{fieldName} is not a list field.");
            }

            var items = GetOrCreateList(fieldName);
            if (index < 0 || index >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"{fieldName} has {items.Count} item(s).");
            }

            var violations = QuickValidate(field, newValue);
            if (violations.Length > 0)
            {
                return violations;
            }

            items[index] = newValue;
            _invalidatedFields.Remove(fieldName);
            _dirtyListFields.Add(fieldName);
            return Array.Empty<ValidationViolation>();
        }

        public void DeleteListItem(string fieldName, int index)
        {
            EnsureMethodSelected();
            var field = GetField(fieldName);

            if (!IsListType(field))
            {
                throw new InvalidOperationException($"{fieldName} is not a list field.");
            }

            var items = GetOrCreateList(fieldName);
            if (index < 0 || index >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"{fieldName} has {items.Count} item(s).");
            }

            if (field.Requirement == RecipeFieldRequirement.Required && items.Count == 1)
            {
                throw new InvalidOperationException($"{fieldName} requires at least one item — cannot delete the last one.");
            }

            items.RemoveAt(index);
            _invalidatedFields.Remove(fieldName);
            _dirtyListFields.Add(fieldName);
        }

        public IReadOnlyList<object> ListItemsFor(string fieldName)
        {
            EnsureMethodSelected();
            var field = GetField(fieldName);

            if (!IsListType(field))
            {
                throw new InvalidOperationException($"{fieldName} is not a list field.");
            }

            return GetListItems(fieldName);
        }

        public void CompleteListField(string fieldName)
        {
            EnsureMethodSelected();
            var field = GetField(fieldName);

            if (!IsListType(field))
            {
                throw new InvalidOperationException($"{fieldName} is not a list field.");
            }

            if (field.Requirement == RecipeFieldRequirement.Required && GetListItemCount(fieldName) == 0)
            {
                throw new InvalidOperationException($"{fieldName} requires at least one item before it can be completed.");
            }

            _completedListFields.Add(fieldName);
        }

        public void SkipOptionalField(string fieldName)
        {
            EnsureMethodSelected();
            var field = GetField(fieldName);

            if (field.Requirement != RecipeFieldRequirement.Optional)
            {
                throw new InvalidOperationException($"{fieldName} is not Optional and cannot be skipped.");
            }

            if (IsListType(field))
            {
                throw new InvalidOperationException($"{fieldName} is a list field — use CompleteListField instead.");
            }

            _skippedOptionalFields.Add(fieldName);
        }

        public ChangeMethodPreview PreviewMethodChange(RecipeMethodId nextMethod)
        {
            EnsureMethodSelected();
            var currentMethod = _selectedMethod!.Value;

            var discarded = MethodSpecificFieldNames(currentMethod)
                .Except(MethodSpecificFieldNames(nextMethod), StringComparer.Ordinal)
                .ToList();

            return new ChangeMethodPreview(
                currentMethod,
                nextMethod,
                _toolNameVersionFields,
                _inputsOutputsFields,
                discarded,
                MetadataFieldNamesFor(currentMethod));
        }

        public void ChangeMethod(RecipeMethodId nextMethod, ChangeMethodDecision decision)
        {
            EnsureMethodSelected();

            if (decision == ChangeMethodDecision.Cancel)
            {
                return;
            }

            var preview = PreviewMethodChange(nextMethod);

            foreach (var fieldName in preview.DiscardedFields)
            {
                _filledFields.Remove(fieldName);
                _scalarValues.Remove(fieldName);
                _completedListFields.Remove(fieldName);
                _listItems.Remove(fieldName);
                _skippedOptionalFields.Remove(fieldName);
                _invalidatedFields.Remove(fieldName);
            }

            foreach (var fieldName in preview.FieldsRequiringRevalidation)
            {
                if (_filledFields.Contains(fieldName) || _completedListFields.Contains(fieldName))
                {
                    _invalidatedFields.Add(fieldName);
                }
            }

            _metadata = ResetMetadataFor(_selectedMethod!.Value, _metadata);
            _selectedMethod = nextMethod;
        }

        public void ConfirmInvalidatedField(string fieldName)
        {
            EnsureMethodSelected();
            _ = GetField(fieldName);
            _invalidatedFields.Remove(fieldName);
        }

        public void AcceptDockerfileWarning() => _metadata = _metadata with { DockerfileWarningAccepted = true };

        public void ShowImageTagWarning() => _metadata = _metadata with { ImageTagWarningShown = true };

        public void AcceptImageTagWarning() => _metadata = _metadata with { ImageTagWarningAccepted = true };

        public RecipeAuthoringSnapshot Snapshot()
        {
            if (!_selectedMethod.HasValue)
            {
                return new RecipeAuthoringSnapshot(
                    null,
                    Array.Empty<RecipeFieldValueSummary>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>());
            }

            var fields = RecipeFieldCatalog.FieldsFor(_selectedMethod.Value);

            var values = fields
                .Where(f => _filledFields.Contains(f.Name) || _completedListFields.Contains(f.Name))
                .Select(f => new RecipeFieldValueSummary(f.Name, DescribeValue(f)))
                .ToList();

            var missingRequired = fields
                .Where(f => f.Requirement == RecipeFieldRequirement.Required && !IsFieldComplete(f))
                .Select(f => f.Name)
                .ToList();

            var defaulted = fields
                .Where(f => f.Requirement == RecipeFieldRequirement.Defaulted && !_filledFields.Contains(f.Name))
                .Select(f => f.Name)
                .ToList();

            var recommendedWarnings = fields
                .Where(f => f.Requirement == RecipeFieldRequirement.Recommended && IsMissingForRecommendedWarning(f))
                .Select(f => f.Name)
                .ToList();

            var invalidated = fields
                .Where(f => _invalidatedFields.Contains(f.Name))
                .Select(f => f.Name)
                .ToList();

            return new RecipeAuthoringSnapshot(
                _selectedMethod,
                values,
                missingRequired,
                defaulted,
                recommendedWarnings,
                invalidated);
        }

        public IReadOnlyList<ValidationViolation> ValidateDraft()
        {
            if (!_selectedMethod.HasValue)
            {
                return new List<ValidationViolation>
                {
                    new("AUTHORING-METHOD-001", "메서드를 먼저 선택해야 합니다."),
                };
            }

            var violations = new List<ValidationViolation>();
            foreach (var field in RecipeFieldCatalog.FieldsFor(_selectedMethod.Value))
            {
                if (field.Requirement == RecipeFieldRequirement.Required && !IsFieldComplete(field))
                {
                    violations.Add(new ValidationViolation(
                        "AUTHORING-REQUIRED-001",
                        $"{field.Name} 값이 필요합니다.",
                        field.Name));
                }
                else if (field.Requirement == RecipeFieldRequirement.Recommended && IsMissingForRecommendedWarning(field))
                {
                    violations.Add(new ValidationViolation(
                        "AUTHORING-RECOMMENDED-001",
                        $"{field.Name} 값을 채우는 것을 권장합니다.",
                        field.Name));
                }
                else if (_invalidatedFields.Contains(field.Name))
                {
                    violations.Add(new ValidationViolation(
                        "AUTHORING-INVALIDATED-001",
                        $"{field.Name} 값은 method 변경 이전 입력입니다. 새 method 기준으로 다시 확인하세요.",
                        field.Name));
                }
            }

            return violations;
        }

        public RecipeValidationRecoveryPlan BuildRecoveryPlan(IReadOnlyList<ValidationViolation> violations)
        {
            ArgumentNullException.ThrowIfNull(violations);

            var actionsByKey = new Dictionary<string, RecipeValidationRecoveryAction>(StringComparer.Ordinal);
            var unmapped = new List<ValidationViolation>();

            foreach (var violation in violations)
            {
                RecipeValidationRecoveryAction action;

                if (violation.Field is "Inputs" or "Outputs")
                {
                    action = ReviewSectionAction();
                }
                else if (violation.Field != null && _renderedFieldToCatalogFields.TryGetValue(violation.Field, out var catalogFields))
                {
                    action = catalogFields.Length == 1
                        ? EditSingleFieldAction(catalogFields[0])
                        : EditRelatedFieldsAction(catalogFields);
                }
                else
                {
                    unmapped.Add(violation);
                    action = ShowExplanationOnlyAction();
                }

                actionsByKey[$"{action.Kind}|{string.Join(",", action.RelatedFields)}"] = action;
            }

            return new RecipeValidationRecoveryPlan(actionsByKey.Values.ToList(), unmapped);
        }

        public RecipeDocument Build()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("Cannot build an incomplete recipe authoring session.");
            }

            // Build() can run more than once for the same session — the interactive
            // recovery loop (RunRecoveryLoop) re-Builds after each fix attempt. Only
            // apply items added since the last Build() call, or a retry would Add()
            // every previously-applied item a second time onto the same _document.
            // EditListItem/DeleteListItem mutate already-applied indices in place,
            // which the delta loop below would never revisit — those fields are
            // marked dirty and get a full ClearList + reapply instead.
            foreach (var field in RecipeFieldCatalog.FieldsFor(_selectedMethod!.Value).Where(IsListType))
            {
                var items = GetListItems(field.Name);

                if (_dirtyListFields.Remove(field.Name))
                {
                    field.ClearList?.Invoke(_document);
                    foreach (var item in items)
                    {
                        field.Apply(_document, item);
                    }

                    _appliedListItemCounts[field.Name] = items.Count;
                    continue;
                }

                var alreadyApplied = _appliedListItemCounts.GetValueOrDefault(field.Name);
                for (var i = alreadyApplied; i < items.Count; i++)
                {
                    field.Apply(_document, items[i]);
                }

                _appliedListItemCounts[field.Name] = items.Count;
            }

            foreach (var field in RecipeFieldCatalog.DefaultedFieldsFor(_selectedMethod.Value))
            {
                if (!_filledFields.Contains(field.Name))
                {
                    field.Apply(_document, field.DefaultValue!);
                }
            }

            // ImageRef (BaseImage) and ImageDigest are separate Required fields during
            // authoring (Section 17 ImageRef/ImageDigest), but RecipeRenderer/RecipeValidator
            // read the Container method's BioContainer build kind from the single combined
            // BioContainerImageUri string — see RecipeDocument.ImageDigest's "first reader" note.
            if (_selectedMethod == RecipeMethodId.Container)
            {
                _document.BioContainerImageUri = $"{_document.BaseImage}@{_document.ImageDigest}";
            }

            return _document;
        }

        private static bool IsListType(RecipeFieldDescriptor field) =>
            field.Type is RecipeFieldType.StringList or RecipeFieldType.InputList or RecipeFieldType.OutputList;

        private static ValidationViolation[] QuickValidate(RecipeFieldDescriptor field, object value)
        {
            if (field.QuickValidate != null)
            {
                var violation = field.QuickValidate(value);
                return violation != null ? new[] { violation } : Array.Empty<ValidationViolation>();
            }

            if (value is string stringValue && string.IsNullOrWhiteSpace(stringValue))
            {
                return new[]
                {
                    new ValidationViolation("AUTHORING-EMPTY-001", $"{field.Name} 값은 비워둘 수 없습니다.", field.Name),
                };
            }

            if (field.Type == RecipeFieldType.Choice
                && field.Choices.Count > 0
                && value is string choiceValue
                && !field.Choices.Any(c => c.Value == choiceValue))
            {
                return new[]
                {
                    new ValidationViolation("AUTHORING-CHOICE-001", $"{field.Name} 값은 허용된 선택지 중 하나여야 합니다.", field.Name),
                };
            }

            return Array.Empty<ValidationViolation>();
        }

        private static List<string> MethodSpecificFieldNames(RecipeMethodId method) =>
            RecipeFieldCatalog.MethodFields[method].Select(f => f.Name).ToList();

        private static string[] MetadataFieldNamesFor(RecipeMethodId method) => method switch
        {
            RecipeMethodId.Dockerfile => new[] { nameof(RecipeAuthoringSessionMetadata.DockerfileWarningAccepted) },
            RecipeMethodId.Container => new[]
            {
                nameof(RecipeAuthoringSessionMetadata.ImageTagWarningShown),
                nameof(RecipeAuthoringSessionMetadata.ImageTagWarningAccepted),
            },
            _ => Array.Empty<string>(),
        };

        private static RecipeAuthoringSessionMetadata ResetMetadataFor(
            RecipeMethodId leavingMethod, RecipeAuthoringSessionMetadata current) => leavingMethod switch
            {
                RecipeMethodId.Dockerfile => current with { DockerfileWarningAccepted = false },
                RecipeMethodId.Container => current with { ImageTagWarningShown = false, ImageTagWarningAccepted = false },
                _ => current,
            };

        private static LocalizedText Text(string ko, string en) =>
            new(new Dictionary<string, string> { ["ko"] = ko, ["en"] = en });

        private static RecipeValidationRecoveryAction EditSingleFieldAction(string field) => new(
            $"{field} 항목 수정",
            RecoveryActionKind.EditSingleField,
            new[] { field },
            Text(
                $"{field} 값에 문제가 있어 최종 검증을 통과하지 못했습니다.",
                $"The {field} value has a problem and failed final validation."),
            Text(
                $"{field} 값을 다시 확인하고 수정하세요.",
                $"Re-check and fix the {field} value."));

        private static RecipeValidationRecoveryAction EditRelatedFieldsAction(IReadOnlyList<string> fields)
        {
            var joined = string.Join(", ", fields);
            return new RecipeValidationRecoveryAction(
                $"{joined} 항목 함께 수정",
                RecoveryActionKind.EditRelatedFields,
                fields,
                Text(
                    $"{joined} 값이 서로 맞지 않아 최종 검증을 통과하지 못했습니다.",
                    $"The {joined} values don't match each other and failed final validation."),
                Text(
                    $"{joined} 값이 함께 맞아야 합니다.",
                    $"The {joined} values must agree with each other."));
        }

        private static RecipeValidationRecoveryAction ReviewSectionAction() => new(
            "입력/출력 섹션 확인",
            RecoveryActionKind.ReviewSection,
            _inputsOutputsFields,
            Text(
                "입력/출력 정의가 렌더링된 build request와 맞지 않습니다.",
                "The input/output definitions don't match the rendered build request."),
            Text(
                "직접 입력한 role/format/shape/class가 특수하면 기본 preset으로 다시 선택해보세요.",
                "If the role/format/shape/class you entered manually is unusual, try reselecting a default preset."));

        private RecipeValidationRecoveryAction ShowExplanationOnlyAction()
        {
            var relatedFields = _selectedMethod.HasValue
                ? MethodSpecificFieldNames(_selectedMethod.Value).Concat(_inputsOutputsFields).ToList()
                : new List<string>(_inputsOutputsFields);

            return new RecipeValidationRecoveryAction(
                "전체 recipe 구조 확인",
                RecoveryActionKind.ShowExplanationOnly,
                relatedFields,
                Text(
                    "이 오류는 한 필드만의 문제가 아닐 수 있습니다.",
                    "This error may not be limited to a single field."),
                Text(
                    "작성 방법에 필요한 필드가 모두 있는지, 입력/출력 preset이 적절한지, package/source/dockerfile 정보가 서로 맞는지 확인하세요.",
                    "Check that every field your method needs is present, that the input/output presets fit, and that your package/source/dockerfile information is mutually consistent."));
        }

        private void EnsureMethodSelected()
        {
            if (!_selectedMethod.HasValue)
            {
                throw new InvalidOperationException("SelectMethod must be called first.");
            }
        }

        private RecipeFieldDescriptor GetField(string fieldName)
        {
            var field = RecipeFieldCatalog.FieldsFor(_selectedMethod!.Value).FirstOrDefault(f => f.Name == fieldName);
            if (field == null)
            {
                throw new ArgumentException($"Unknown field '{fieldName}' for method {_selectedMethod}.", nameof(fieldName));
            }

            return field;
        }

        private bool IsFieldComplete(RecipeFieldDescriptor field)
        {
            if (IsListType(field))
            {
                return _completedListFields.Contains(field.Name);
            }

            return field.Requirement switch
            {
                RecipeFieldRequirement.Required => _filledFields.Contains(field.Name),
                RecipeFieldRequirement.Defaulted => true,
                RecipeFieldRequirement.Recommended => true,
                RecipeFieldRequirement.Optional =>
                    _filledFields.Contains(field.Name) || _skippedOptionalFields.Contains(field.Name),
                _ => throw new ArgumentOutOfRangeException(nameof(field), field.Requirement, "Unsupported requirement tier."),
            };
        }

        private bool IsMissingForRecommendedWarning(RecipeFieldDescriptor field) =>
            IsListType(field) ? GetListItemCount(field.Name) == 0 : !_filledFields.Contains(field.Name);

        private List<object> GetOrCreateList(string fieldName)
        {
            if (!_listItems.TryGetValue(fieldName, out var items))
            {
                items = new List<object>();
                _listItems[fieldName] = items;
            }

            return items;
        }

        private IReadOnlyList<object> GetListItems(string fieldName) =>
            _listItems.TryGetValue(fieldName, out var items) ? items : Array.Empty<object>();

        private int GetListItemCount(string fieldName) => GetListItems(fieldName).Count;

        private string DescribeValue(RecipeFieldDescriptor field)
        {
            if (IsListType(field))
            {
                return $"{GetListItemCount(field.Name)}개 항목";
            }

            return _scalarValues.TryGetValue(field.Name, out var value) ? value.ToString() ?? string.Empty : string.Empty;
        }
    }
}
