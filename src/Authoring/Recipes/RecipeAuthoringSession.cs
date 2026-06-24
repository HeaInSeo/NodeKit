using System;
using System.Collections.Generic;
using System.Linq;
using NodeKit.Validation;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Stateful, step-by-step recipe authoring session — see design doc
    /// Sections 17-19. This is an R5 API subset: ChangeMethod, recovery
    /// planning, and per-item editing of a completed list field are Sprint
    /// R6 scope and intentionally not implemented here.
    /// </summary>
    internal sealed class RecipeAuthoringSession
    {
        private readonly RecipeDocument _document = new();
        private readonly HashSet<string> _filledFields = new(StringComparer.Ordinal);
        private readonly HashSet<string> _skippedOptionalFields = new(StringComparer.Ordinal);
        private readonly HashSet<string> _completedListFields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> _scalarValues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _listItemCounts = new(StringComparer.Ordinal);

        private RecipeMethodId? _selectedMethod;

        public bool IsMethodSelected => _selectedMethod.HasValue;

        public bool IsComplete =>
            _selectedMethod.HasValue
            && RecipeFieldCatalog.FieldsFor(_selectedMethod.Value).All(IsFieldComplete);

        public void SelectMethod(RecipeMethodId method)
        {
            if (_selectedMethod.HasValue)
            {
                throw new InvalidOperationException(
                    "Method already selected. Use ChangeMethod (Sprint R6) to switch methods.");
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

            if (_completedListFields.Contains(fieldName))
            {
                throw new InvalidOperationException(
                    $"{fieldName} is already completed. Per-item editing of a completed list is Sprint R6 scope.");
            }

            var violations = QuickValidate(field, item);
            if (violations.Length > 0)
            {
                return violations;
            }

            field.Apply(_document, item);
            _listItemCounts[fieldName] = GetListItemCount(fieldName) + 1;
            return Array.Empty<ValidationViolation>();
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

            return new RecipeAuthoringSnapshot(
                _selectedMethod,
                values,
                missingRequired,
                defaulted,
                recommendedWarnings,
                Array.Empty<string>());
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
            }

            return violations;
        }

        public RecipeDocument Build()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("Cannot build an incomplete recipe authoring session.");
            }

            foreach (var field in RecipeFieldCatalog.DefaultedFieldsFor(_selectedMethod!.Value))
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

        private int GetListItemCount(string fieldName) =>
            _listItemCounts.TryGetValue(fieldName, out var count) ? count : 0;

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
