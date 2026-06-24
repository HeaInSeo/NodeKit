using System;
using System.Linq;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
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
        public void AppendListItem_AfterCompleteListField_Throws()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.AppendListItem("Packages", "bwa=0.7.17=h5bf99c6_8");
            session.CompleteListField("Packages");

            var ex = Assert.Throws<InvalidOperationException>(() => session.AppendListItem("Packages", "samtools=1.19=h50ea8bc_0"));
            Assert.Contains("Sprint R6", ex.Message, StringComparison.Ordinal);
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

            Assert.Equal(default(RecipeBuildKind), document.BuildKind);
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
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0");
            session.SetField("ImageDigest", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            session.CompleteListField("Command");
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            session.AppendListItem("Outputs", new ToolOutput { Name = "bam", Role = "alignment", Format = "bam", Shape = "single", Class = "primary" });
            session.CompleteListField("Outputs");

            Assert.True(session.IsComplete);

            var document = session.Build();

            // Script has no RecipeFieldCatalog field yet (pre-existing gap, out of R5
            // scope) — set directly so this test isolates the digest-pinning invariant.
            document.Script = "run.sh";
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
            session.SetField("ImageRef", "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            session.CompleteListField("Command");
            session.AppendListItem("Inputs", new ToolInput { Name = "reads", Role = "reads", Format = "fastq", Shape = "pair", Required = true });
            session.CompleteListField("Inputs");
            session.AppendListItem("Outputs", new ToolOutput { Name = "bam", Role = "alignment", Format = "bam", Shape = "single", Class = "primary" });
            session.CompleteListField("Outputs");

            Assert.False(session.IsComplete);
            Assert.Throws<InvalidOperationException>(() => session.Build());
        }

        private static RecipeAuthoringSession CompletePackageSession()
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Package);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
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
