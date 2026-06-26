using System;
using System.Linq;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Validation;
using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeAuthoringSessionTests
    {
        [Fact]
        public void SelectMethod_CalledTwice_Throws()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);

            var ex = Assert.Throws<InvalidOperationException>(() => session.SelectMethod(RecipeMethodId.Package));
            Assert.Contains("ChangeMethod", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void NextField_BeforeMethodSelected_Throws()
        {
            var session = new RecipeAuthoringSession();

            Assert.Throws<InvalidOperationException>(() => session.NextField());
        }

        [Fact]
        public void NextField_SkipsDefaultedAndRecommendedScalarFields()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            session.AppendListItem("Packages", "bwa=0.7.17=h5bf99c6_8");
            session.CompleteListField("Packages");
            session.AppendListItem("Channels", "bioconda");
            session.CompleteListField("Channels");

            // PackageEngine (Defaulted) must never surface even though unset.
            var next = session.NextField();

            Assert.Equal("Inputs", next!.Name);
        }

        [Fact]
        public void NextField_SurfacesOptionalScalarFieldUntilSkippedOrSet()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0");
            session.SetField("ImageDigest", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

            Assert.Equal("Command", session.NextField()!.Name);

            session.CompleteListField("Command");

            Assert.Equal("Inputs", session.NextField()!.Name);
        }

        [Fact]
        public void SetField_RejectsListField()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);

            Assert.Throws<InvalidOperationException>(() => session.SetField("Packages", "bwa"));
        }

        [Fact]
        public void SetField_EmptyString_ReturnsViolationWithoutApplying()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);

            var violations = session.SetField("ImageRef", "   ");

            Assert.NotEmpty(violations);
            Assert.DoesNotContain(session.Snapshot().Values, v => v.FieldName == "ImageRef");
        }

        [Fact]
        public void SetField_InvalidChoiceValue_ReturnsViolationWithoutApplying()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);

            var violations = session.SetField("PackageEngine", "pip");

            Assert.NotEmpty(violations);
            Assert.DoesNotContain(session.Snapshot().Values, v => v.FieldName == "PackageEngine");
        }

        [Fact]
        public void SetField_ValidChoiceValue_Applies()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);

            var violations = session.SetField("PackageEngine", "micromamba");

            Assert.Empty(violations);
            Assert.Contains(session.Snapshot().Values, v => v.FieldName == "PackageEngine" && v.DisplayValue == "micromamba");
        }

        [Fact]
        public void AppendListItem_RejectsScalarField()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);

            Assert.Throws<InvalidOperationException>(() => session.AppendListItem("ImageRef", "x"));
        }

        [Fact]
        public void AppendListItem_AfterCompleteListField_AppendsItemAndStaysComplete()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Packages", "bwa=0.7.17=h5bf99c6_8");
            session.CompleteListField("Packages");

            var violations = session.AppendListItem("Packages", "samtools=1.19=h50ea8bc_0");

            Assert.Empty(violations);
            Assert.Equal("2개 항목", session.Snapshot().Values.Single(v => v.FieldName == "Packages").DisplayValue);
        }

        [Fact]
        public void EditListItem_ReplacesItemAtIndex()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Channels", "biocnda");
            session.CompleteListField("Channels");

            var violations = session.EditListItem("Channels", 0, "bioconda");

            Assert.Empty(violations);
        }

        [Fact]
        public void EditListItem_OutOfRangeIndex_Throws()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Channels", "bioconda");

            Assert.Throws<ArgumentOutOfRangeException>(() => session.EditListItem("Channels", 5, "conda-forge"));
        }

        [Fact]
        public void EditListItem_InvalidValue_ReturnsViolationWithoutMutating()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Channels", "bioconda");

            var violations = session.EditListItem("Channels", 0, "   ");

            Assert.NotEmpty(violations);
        }

        [Fact]
        public void DeleteListItem_OnRequiredFieldDownToZero_Throws()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Packages", "bwa=0.7.17=h5bf99c6_8");
            session.CompleteListField("Packages");

            var ex = Assert.Throws<InvalidOperationException>(() => session.DeleteListItem("Packages", 0));
            Assert.Contains("at least one item", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DeleteListItem_OnOptionalFieldDownToZero_Succeeds()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            session.AppendListItem("Command", "bwa");
            session.CompleteListField("Command");

            session.DeleteListItem("Command", 0);

            Assert.Equal("0개 항목", session.Snapshot().Values.Single(v => v.FieldName == "Command").DisplayValue);
        }

        [Fact]
        public void CompleteListField_RequiredFieldWithZeroItems_Throws()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);

            var ex = Assert.Throws<InvalidOperationException>(() => session.CompleteListField("Packages"));
            Assert.Contains("at least one item", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void CompleteListField_OptionalFieldWithZeroItems_Succeeds()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);

            session.CompleteListField("Command");

            Assert.NotEqual("Command", session.NextField()?.Name);
        }

        [Fact]
        public void SkipOptionalField_OnRequiredField_Throws()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);

            Assert.Throws<InvalidOperationException>(() => session.SkipOptionalField("ImageRef"));
        }

        [Fact]
        public void SkipOptionalField_OnListField_Throws()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);

            var ex = Assert.Throws<InvalidOperationException>(() => session.SkipOptionalField("Command"));
            Assert.Contains("CompleteListField", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void IsComplete_TrueOnlyAfterAllRequiredAndOptionalDecisionsMade()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            Assert.False(session.IsComplete);

            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0");
            session.SetField("ImageDigest", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            Assert.False(session.IsComplete);

            session.CompleteListField("Command");
            Assert.False(session.IsComplete);

            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            Assert.False(session.IsComplete);

            session.AppendListItem("Outputs", new ToolOutput { Name = "bam", Role = "alignment", Format = "bam", Shape = "single", Class = "primary" });
            session.CompleteListField("Outputs");
            Assert.True(session.IsComplete);
        }

        [Fact]
        public void Build_BeforeIsComplete_Throws()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            session.SetField("ToolName", "bwa-mem");

            Assert.Throws<InvalidOperationException>(() => session.Build());
        }

        [Fact]
        public void Build_AppliesDefaultedFieldNotExplicitlySet()
        {
            var session = CompletePackageSession();

            var document = session.Build();

            Assert.Equal("conda", document.PackageEngine);
        }

        [Fact]
        public void Build_DoesNotOverrideExplicitlySetDefaultedField()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            session.SetField("PackageEngine", "micromamba");
            session.AppendListItem("Packages", "bwa=0.7.17=h5bf99c6_8");
            session.CompleteListField("Packages");
            session.AppendListItem("Channels", "bioconda");
            session.CompleteListField("Channels");
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            session.AppendListItem("Outputs", new ToolOutput { Name = "bam", Role = "alignment", Format = "bam", Shape = "single", Class = "primary" });
            session.CompleteListField("Outputs");

            var document = session.Build();

            Assert.Equal("micromamba", document.PackageEngine);
        }

        [Fact]
        public void Build_DoesNotResolveBuildKind()
        {
            var session = CompletePackageSession();

            var document = session.Build();

            Assert.Null(document.BuildKind);
        }

        [Fact]
        public void Snapshot_NoMethodSelected_ReturnsEmptySnapshot()
        {
            var session = new RecipeAuthoringSession();

            var snapshot = session.Snapshot();

            Assert.Null(snapshot.SelectedMethod);
            Assert.Empty(snapshot.Values);
            Assert.Empty(snapshot.MissingRequiredFields);
        }

        [Fact]
        public void Snapshot_IncompleteSession_ListsMissingRequiredAndDefaultedFields()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.SetField("ToolName", "bwa-mem");

            var snapshot = session.Snapshot();

            Assert.Equal(RecipeMethodId.Package, snapshot.SelectedMethod);
            Assert.Contains("ToolVersion", snapshot.MissingRequiredFields);
            Assert.Contains("Packages", snapshot.MissingRequiredFields);
            Assert.Contains("PackageEngine", snapshot.DefaultedFields);
        }

        [Fact]
        public void Snapshot_NeverAppliesDefaultedValue()
        {
            var session = CompletePackageSession();

            var snapshotBeforeBuild = session.Snapshot();

            Assert.Contains("PackageEngine", snapshotBeforeBuild.DefaultedFields);
            Assert.DoesNotContain(snapshotBeforeBuild.Values, v => v.FieldName == "PackageEngine");
        }

        [Fact]
        public void Snapshot_RecommendedMissingField_ReportsWarningNotMissingRequired()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Source);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("SourceUri", "https://example.org/bwa.tar.gz");
            session.SetField("SourceChecksum", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            session.AppendListItem("SourceBuildCommands", "make");
            session.CompleteListField("SourceBuildCommands");

            var snapshot = session.Snapshot();

            Assert.Contains("BuildDependencies", snapshot.RecommendedWarnings);
            Assert.DoesNotContain("BuildDependencies", snapshot.MissingRequiredFields);
        }

        [Fact]
        public void ValidateDraft_NoMethodSelected_ReturnsMethodViolation()
        {
            var session = new RecipeAuthoringSession();

            var violations = session.ValidateDraft();

            Assert.Single(violations);
            Assert.Equal("AUTHORING-METHOD-001", violations[0].RuleId);
        }

        [Fact]
        public void ValidateDraft_NeverProducesL1RuleIds()
        {
            var session = CompletePackageSession();

            var violations = session.ValidateDraft();

            Assert.DoesNotContain(violations, v => v.RuleId.StartsWith("L1-RCP-", StringComparison.Ordinal));
        }

        [Fact]
        public void ValidateDraft_DoesNotInvokeResolverEvenWhenFieldComplete()
        {
            // PackageEngine is Defaulted, so the session reports IsComplete == true
            // without it ever being explicitly set — PackageEngine stays "" on the
            // document. RecipeBuildKindResolver.Resolve(Package, ...) throws when
            // PackageEngine is blank (see RecipeBuildKindResolverTests), so if
            // ValidateDraft() ever called the resolver internally, this would throw.
            var session = CompletePackageSession();
            Assert.True(session.IsComplete);

            var violations = session.ValidateDraft();

            Assert.Empty(violations);
        }

        [Fact]
        public void EndToEnd_ContainerSessionBuild_PassesNodeKitValidate()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0");
            session.SetField("ImageDigest", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            session.CompleteListField("Command");
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            session.AppendListItem("Outputs", new ToolOutput { Name = "bam", Role = "alignment", Format = "bam", Shape = "single", Class = "primary" });
            session.CompleteListField("Outputs");

            Assert.True(session.IsComplete);

            var document = session.Build();
            document.BuildKind = RecipeBuildKindResolver.Resolve(RecipeMethodId.Container, document);

            var result = RecipeValidationPipeline.ValidateRecipe(document);

            var dump = string.Join(" | ", result.Violations.Select(v => $"{v.RuleId}: {v.Message}"));
            Assert.True(result.IsValid, dump);
        }

        [Fact]
        public void EndToEnd_ContainerSessionWithoutDigest_CannotReachBuild()
        {
            // ImageDigest is Required (not Recommended/Optional) precisely so a
            // session can never Build() — and therefore never reach final
            // validate — without it. See design doc Section 19.3.
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            session.CompleteListField("Command");
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            session.AppendListItem("Outputs", new ToolOutput { Name = "bam", Role = "alignment", Format = "bam", Shape = "single", Class = "primary" });
            session.CompleteListField("Outputs");

            Assert.False(session.IsComplete);
            Assert.Throws<InvalidOperationException>(() => session.Build());
        }

        [Fact]
        public void PreviewMethodChange_PackageToSource_DiscardsPackageSpecificFields()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);

            var preview = session.PreviewMethodChange(RecipeMethodId.Source);

            Assert.Contains("Packages", preview.DiscardedFields);
            Assert.Contains("Channels", preview.DiscardedFields);
            Assert.Contains("PackageEngine", preview.DiscardedFields);
        }

        [Fact]
        public void PreviewMethodChange_SourceToPackage_DiscardsSourceSpecificFields()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Source);

            var preview = session.PreviewMethodChange(RecipeMethodId.Package);

            Assert.Contains("SourceUri", preview.DiscardedFields);
            Assert.Contains("SourceChecksum", preview.DiscardedFields);
            Assert.Contains("SourceBuildCommands", preview.DiscardedFields);
            Assert.Contains("BuildDependencies", preview.DiscardedFields);
        }

        [Fact]
        public void PreviewMethodChange_DockerfileToPackage_DiscardsDockerfileSpecificFields()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Dockerfile);

            var preview = session.PreviewMethodChange(RecipeMethodId.Package);

            Assert.Contains("DockerfilePath", preview.DiscardedFields);
            Assert.Contains("DockerfileContent", preview.DiscardedFields);
            Assert.Contains("BuildContext", preview.DiscardedFields);
        }

        [Fact]
        public void PreviewMethodChange_NeverListsToolNameOrVersionAsDiscarded()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);

            var preview = session.PreviewMethodChange(RecipeMethodId.Source);

            Assert.DoesNotContain("ToolName", preview.DiscardedFields);
            Assert.DoesNotContain("ToolVersion", preview.DiscardedFields);
            Assert.Contains("ToolName", preview.PreservedFields);
            Assert.Contains("ToolVersion", preview.PreservedFields);
        }

        [Fact]
        public void ChangeMethod_Cancel_LeavesSessionUnchanged()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Packages", "bwa=0.7.17=h5bf99c6_8");
            session.CompleteListField("Packages");

            session.ChangeMethod(RecipeMethodId.Source, ChangeMethodDecision.Cancel);

            Assert.Equal(RecipeMethodId.Package, session.Snapshot().SelectedMethod);
            Assert.Contains(session.Snapshot().Values, v => v.FieldName == "Packages");
        }

        [Fact]
        public void ChangeMethod_Proceed_DiscardsMethodSpecificFieldsAndSwitchesMethod()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Packages", "bwa=0.7.17=h5bf99c6_8");
            session.CompleteListField("Packages");
            session.AppendListItem("Channels", "bioconda");
            session.CompleteListField("Channels");

            session.ChangeMethod(RecipeMethodId.Source, ChangeMethodDecision.Proceed);

            var snapshot = session.Snapshot();
            Assert.Equal(RecipeMethodId.Source, snapshot.SelectedMethod);
            Assert.DoesNotContain(snapshot.Values, v => v.FieldName == "Packages");
            Assert.DoesNotContain(snapshot.Values, v => v.FieldName == "Channels");
        }

        [Fact]
        public void ChangeMethod_Proceed_PreservesSharedFieldsButInvalidatesThem()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");

            session.ChangeMethod(RecipeMethodId.Source, ChangeMethodDecision.Proceed);

            var snapshot = session.Snapshot();
            Assert.Contains(snapshot.Values, v => v.FieldName == "ToolName");
            Assert.Contains(snapshot.Values, v => v.FieldName == "Inputs");
            Assert.Contains("Inputs", snapshot.InvalidatedFields);
            Assert.DoesNotContain("ToolName", snapshot.InvalidatedFields);
        }

        [Fact]
        public void ChangeMethod_Proceed_DoesNotBlockIsCompleteForInvalidatedFields()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0");
            session.SetField("ImageDigest", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            session.CompleteListField("Command");
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            session.AppendListItem("Outputs", new ToolOutput { Name = "bam", Role = "alignment", Format = "bam", Shape = "single", Class = "primary" });
            session.CompleteListField("Outputs");

            session.ChangeMethod(RecipeMethodId.Dockerfile, ChangeMethodDecision.Proceed);
            session.SetField("DockerfilePath", "./Dockerfile");
            session.SetField("DockerfileContent", "FROM scratch");

            Assert.True(session.IsComplete);
            Assert.Contains("Inputs", session.Snapshot().InvalidatedFields);
        }

        [Fact]
        public void ChangeMethod_Proceed_ResetsMethodSpecificMetadata()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Dockerfile);
            session.AcceptDockerfileWarning();
            Assert.True(session.Metadata.DockerfileWarningAccepted);

            session.ChangeMethod(RecipeMethodId.Package, ChangeMethodDecision.Proceed);

            Assert.False(session.Metadata.DockerfileWarningAccepted);
        }

        [Fact]
        public void ChangeMethod_Proceed_ResetsContainerImageTagWarningMetadata()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            session.ShowImageTagWarning();
            session.AcceptImageTagWarning();

            session.ChangeMethod(RecipeMethodId.Package, ChangeMethodDecision.Proceed);

            Assert.False(session.Metadata.ImageTagWarningShown);
            Assert.False(session.Metadata.ImageTagWarningAccepted);
        }

        [Fact]
        public void ConfirmInvalidatedField_ClearsInvalidatedState()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            session.ChangeMethod(RecipeMethodId.Source, ChangeMethodDecision.Proceed);
            Assert.Contains("Inputs", session.Snapshot().InvalidatedFields);

            session.ConfirmInvalidatedField("Inputs");

            Assert.DoesNotContain("Inputs", session.Snapshot().InvalidatedFields);
        }

        [Fact]
        public void EditListItem_OnInvalidatedListField_ClearsInvalidatedState()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            session.ChangeMethod(RecipeMethodId.Source, ChangeMethodDecision.Proceed);

            session.EditListItem("Inputs", 0, new ToolInput { Name = "reads2", Role = "reads", Format = "fastq", Shape = "pair", Required = true });

            Assert.DoesNotContain("Inputs", session.Snapshot().InvalidatedFields);
        }

        [Fact]
        public void BuildRecoveryPlan_RequiredFieldViolation_ProducesEditSingleFieldAction()
        {
            var session = CompletePackageSession();
            var violations = new[] { new ValidationViolation("L1-RCP-001", "Packages 값이 필요합니다.", "Packages") };

            var plan = session.BuildRecoveryPlan(violations);

            Assert.Single(plan.Actions);
            Assert.Equal(RecoveryActionKind.EditSingleField, plan.Actions[0].Kind);
            Assert.Contains("Packages", plan.Actions[0].RelatedFields);
            Assert.Empty(plan.UnmappedViolations);
        }

        [Fact]
        public void BuildRecoveryPlan_MissingScriptViolation_ProducesEditSingleFieldAction()
        {
            var session = CompletePackageSession();
            var violations = new[] { new ValidationViolation("L1-REQ-003", "실행 스크립트는 필수입니다.", "Script") };

            var plan = session.BuildRecoveryPlan(violations);

            Assert.Single(plan.Actions);
            Assert.Equal(RecoveryActionKind.EditSingleField, plan.Actions[0].Kind);
            Assert.Contains("Script", plan.Actions[0].RelatedFields);
            Assert.Empty(plan.UnmappedViolations);
        }

        [Fact]
        public void BuildRecoveryPlan_UnpinnedDigestViolation_ProducesEditRelatedFieldsAction()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            var violations = new[] { new ValidationViolation("L1-IMG-004", "ImageUri에 digest가 없습니다.", "ImageUri") };

            var plan = session.BuildRecoveryPlan(violations);

            Assert.Single(plan.Actions);
            Assert.Equal(RecoveryActionKind.EditRelatedFields, plan.Actions[0].Kind);
            Assert.Contains("ImageRef", plan.Actions[0].RelatedFields);
            Assert.Contains("ImageDigest", plan.Actions[0].RelatedFields);
            Assert.Empty(plan.UnmappedViolations);
        }

        [Fact]
        public void BuildRecoveryPlan_ForMissingImageDigest_IncludesBeginnerHint()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            var violations = new[] { new ValidationViolation("L1-IMG-004", "ImageUri에 digest가 없습니다.", "ImageUri") };

            var plan = session.BuildRecoveryPlan(violations);

            Assert.Single(plan.Actions);
            Assert.Equal("이미지 digest 입력하기", plan.Actions[0].Label);
            Assert.Contains("Quay 또는 Harbor", plan.Actions[0].BeginnerHint.Get("ko"));
            Assert.Contains("ImageRef", plan.Actions[0].RelatedFields);
            Assert.Contains("ImageDigest", plan.Actions[0].RelatedFields);
        }

        [Fact]
        public void BuildRecoveryPlan_ForMissingSourceChecksum_IncludesCurlSha256sumHint()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Source);
            var violations = new[] { new ValidationViolation("L1-SRC-001", "SourceChecksum이 필요합니다.", "SourceChecksum") };

            var plan = session.BuildRecoveryPlan(violations);

            Assert.Single(plan.Actions);
            Assert.Equal("소스 코드 검증값 입력하기", plan.Actions[0].Label);
            Assert.Contains("curl -fsSL", plan.Actions[0].BeginnerHint.Get("ko"));
            Assert.Contains("sha256sum", plan.Actions[0].BeginnerHint.Get("ko"));
            Assert.Contains("SourceChecksum", plan.Actions[0].RelatedFields);
        }

        [Fact]
        public void BuildRecoveryPlan_ForUnpinnedPackage_IncludesBiocondaVersionHint()
        {
            var session = CompletePackageSession();
            var violations = new[] { new ValidationViolation("L1-PKG-001", "패키지 버전이 고정되지 않았습니다.", "Packages") };

            var plan = session.BuildRecoveryPlan(violations);

            Assert.Single(plan.Actions);
            Assert.Equal("패키지 버전 고정하기", plan.Actions[0].Label);
            Assert.Contains("bwa=0.7.17", plan.Actions[0].BeginnerHint.Get("ko"));
            Assert.Contains("bioconda", plan.Actions[0].BeginnerHint.Get("ko"));
            Assert.Contains("Packages", plan.Actions[0].RelatedFields);
        }

        [Fact]
        public void BuildRecoveryPlan_InputsOutputsViolation_ProducesReviewSectionAction()
        {
            var session = CompletePackageSession();
            var violations = new[] { new ValidationViolation("L1-RCP-010", "Inputs와 렌더링된 build request가 맞지 않습니다.", "Inputs") };

            var plan = session.BuildRecoveryPlan(violations);

            Assert.Single(plan.Actions);
            Assert.Equal(RecoveryActionKind.ReviewSection, plan.Actions[0].Kind);
            Assert.Empty(plan.UnmappedViolations);
        }

        [Fact]
        public void BuildRecoveryPlan_UnmappedField_ProducesShowExplanationOnlyActionAndUnmappedViolation()
        {
            var session = CompletePackageSession();
            var violations = new[] { new ValidationViolation("L1-RCP-099", "BuildKind 값에 문제가 있습니다.", "BuildKind") };

            var plan = session.BuildRecoveryPlan(violations);

            Assert.Single(plan.Actions);
            Assert.Equal(RecoveryActionKind.ShowExplanationOnly, plan.Actions[0].Kind);
            Assert.Single(plan.UnmappedViolations);
            Assert.Equal("BuildKind", plan.UnmappedViolations[0].Field);
        }

        [Fact]
        public void BuildRecoveryPlan_NoFieldOnViolation_ProducesShowExplanationOnlyAction()
        {
            var session = CompletePackageSession();
            var violations = new[] { new ValidationViolation("L1-RCP-098", "알 수 없는 오류입니다.") };

            var plan = session.BuildRecoveryPlan(violations);

            Assert.Single(plan.Actions);
            Assert.Equal(RecoveryActionKind.ShowExplanationOnly, plan.Actions[0].Kind);
            Assert.Single(plan.UnmappedViolations);
        }

        private static RecipeAuthoringSession CompletePackageSession()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            session.AppendListItem("Packages", "bwa=0.7.17=h5bf99c6_8");
            session.CompleteListField("Packages");
            session.AppendListItem("Channels", "bioconda");
            session.CompleteListField("Channels");
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            session.AppendListItem("Outputs", new ToolOutput { Name = "bam", Role = "alignment", Format = "bam", Shape = "single", Class = "primary" });
            session.CompleteListField("Outputs");
            return session;
        }
    }
}
