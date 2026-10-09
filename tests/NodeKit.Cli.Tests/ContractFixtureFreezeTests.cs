using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using NodeKit.Authoring.Recipes;
using NodeKit.Authoring.ToolFunctionRecipes;
using Nodevault.V1;
using Xunit;
using RecipeKind = NodeKit.Authoring.Recipes.RecipeKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P01.contract: tests/NodeKit.Cli.Tests/Fixtures/Contract의 공통 계약 fixture가 실제로 파싱되고
    /// 현재 코드(method catalog·kind resolver·Recipe 모델)·벤더 proto descriptor·provenance와 서로 일치하는지 확인한다.
    /// fixture의 기대값은 후속 stage의 목표이며 이 테스트는 그 기대값을 PASS로 만들지 않는다 — 동결된 계약이
    /// 코드/원본과 어긋나지 않았는지만 본다.
    /// </summary>
    public class ContractFixtureFreezeTests
    {
        private static readonly string[] _fixtureFiles =
        {
            "cli-acceptance-contract.json",
            "toolfunction-persisted.json",
            "contract-source-manifest.json",
            "portable-authoring-transfer.json",
        };

        private static readonly string[] _modes = { "guided", "quick", "non-interactive" };

        [Fact]
        public void AllFixtures_Parse_AndDeclareP01ContractRevision()
        {
            foreach (var root in _fixtureFiles.Select(LoadRoot))
            {
                Assert.Equal("v0.9.1", root.GetProperty("contractRevision").GetString());
                Assert.Equal("P01.contract", root.GetProperty("stage").GetString());
            }
        }

        [Fact]
        public void SupportTable_Has18UniqueCells_WithSingleNoDirectRoute()
        {
            using var doc = Load("cli-acceptance-contract.json");
            var root = doc.RootElement;
            var methods = root.GetProperty("methods").EnumerateArray()
                .ToDictionary(m => m.GetProperty("publicName").GetString()!, m => m.GetProperty("fixture").GetString()!, StringComparer.Ordinal);
            var cells = root.GetProperty("supportTable").GetProperty("cells").EnumerateArray().ToArray();

            Assert.Equal(18, cells.Length);
            var keys = cells.Select(c => $"{c.GetProperty("method").GetString()}/{c.GetProperty("mode").GetString()}").ToArray();
            Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            foreach (var method in methods.Keys)
            {
                foreach (var mode in _modes)
                {
                    Assert.Contains($"{method}/{mode}", keys);
                }
            }

            var noDirect = Assert.Single(cells, c => c.GetProperty("status").GetString() == "NO_DIRECT_ROUTE");
            Assert.Equal("source", noDirect.GetProperty("method").GetString());
            Assert.Equal("guided", noDirect.GetProperty("mode").GetString());
            Assert.Equal("source-structured", noDirect.GetProperty("redirectsTo").GetString());

            foreach (var cell in cells)
            {
                Assert.Equal("NOT_RUN", cell.GetProperty("verification").GetString());
                if (cell.GetProperty("status").GetString() == "REFERENCE")
                {
                    Assert.Equal(methods[cell.GetProperty("method").GetString()!], cell.GetProperty("fixture").GetString());
                }
            }

            var fixtureIds = root.GetProperty("fixtures").EnumerateArray().Select(f => f.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);
            Assert.All(methods.Values, f => Assert.Contains(f, fixtureIds));
        }

        [Fact]
        public void Methods_MatchCodeCatalog_PublicNames_AndKindResolver()
        {
            using var doc = Load("cli-acceptance-contract.json");
            var methods = doc.RootElement.GetProperty("methods").EnumerateArray().ToArray();

            var methodIds = methods.Select(m => Enum.Parse<RecipeMethodId>(m.GetProperty("methodId").GetString()!)).ToArray();
            Assert.Equal(Enum.GetValues<RecipeMethodId>().OrderBy(v => v), methodIds.OrderBy(v => v));
            Assert.Equal(methodIds.OrderBy(v => v), RecipeMethodCatalog.Methods.Select(m => m.Method).OrderBy(v => v));

            var publicNames = (IReadOnlyDictionary<string, RecipeMethodId>)typeof(RecipeCreateCommand)
                .GetField("_publicMethodNames", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
            Assert.Equal(publicNames.Count, methods.Length);

            var allKinds = new HashSet<RecipeKind>();
            foreach (var method in methods)
            {
                var id = Enum.Parse<RecipeMethodId>(method.GetProperty("methodId").GetString()!);
                Assert.Equal(id, publicNames[method.GetProperty("publicName").GetString()!]);

                var reference = Enum.Parse<RecipeKind>(method.GetProperty("referenceKind").GetString()!);
                Assert.Equal(reference, RecipeKindResolver.Resolve(id, new RecipeDocument { PackageEngine = "conda" }));

                var kinds = method.GetProperty("internalKinds").EnumerateArray().Select(k => Enum.Parse<RecipeKind>(k.GetString()!)).ToArray();
                Assert.Contains(reference, kinds);
                allKinds.UnionWith(kinds);
            }

            Assert.Equal(RecipeKind.Micromamba, RecipeKindResolver.Resolve(RecipeMethodId.Package, new RecipeDocument { PackageEngine = "micromamba" }));
            Assert.Equal(Enum.GetValues<RecipeKind>().OrderBy(k => k), allKinds.OrderBy(k => k));
        }

        [Fact]
        public void AuthoringSchema_AcceptedValueMatchesInitializers_AndVariantsAreConsistent()
        {
            using var doc = Load("cli-acceptance-contract.json");
            var schema = doc.RootElement.GetProperty("authoringSchema");
            var accepted = schema.GetProperty("acceptedValue").GetString();

            Assert.Equal(accepted, new RecipeDocument().SchemaVersion);
            Assert.Equal(accepted, new ToolFunctionRecipe().SchemaVersion);
            Assert.False(schema.GetProperty("initializerFill").GetBoolean());
            Assert.False(schema.GetProperty("autoMigration").GetBoolean());

            var variants = schema.GetProperty("variants").EnumerateArray().ToArray();
            Assert.Equal(variants.Length, variants.Select(v => v.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());

            foreach (var variant in variants)
            {
                var expected = variant.GetProperty("expected");
                var snippet = variant.GetProperty("json");
                string? value = null;
                var present = false;
                if (snippet.ValueKind == JsonValueKind.String)
                {
                    using var parsed = JsonDocument.Parse("{" + snippet.GetString() + "}");
                    var property = Assert.Single(parsed.RootElement.EnumerateObject());
                    Assert.Equal("SchemaVersion", property.Name, ignoreCase: true);
                    present = true;
                    value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                }

                var isAccepted = present && string.Equals(value, accepted, StringComparison.Ordinal);
                if (isAccepted)
                {
                    Assert.Equal("ACCEPTED_TO_L1", expected.GetProperty("outcome").GetString());
                }
                else
                {
                    Assert.Equal("REJECTED", expected.GetProperty("outcome").GetString());
                    Assert.Equal(2, expected.GetProperty("exit").GetInt32());
                    Assert.Equal(0, expected.GetProperty("businessRpc").GetInt32());
                    Assert.True(expected.GetProperty("existingFilePreserved").GetBoolean());
                }
            }

            Assert.Contains(variants, v => v.GetProperty("json").ValueKind == JsonValueKind.Null);
        }

        [Fact]
        public void SubmitBlockDisposition_AnchorsExistInCurrentRepo()
        {
            using var doc = Load("cli-acceptance-contract.json");
            var entries = doc.RootElement.GetProperty("submitBlockDisposition").GetProperty("entries").EnumerateArray().ToArray();
            var ids = entries.Select(e => e.GetProperty("id").GetString()).ToArray();

            foreach (var required in new[] { "FR-020", "FR-021", "SC-005" })
            {
                Assert.Contains(required, ids);
            }

            foreach (var entry in entries)
            {
                var path = Path.Join(RepoRoot(), entry.GetProperty("file").GetString());
                Assert.True(File.Exists(path), $"missing file: {path}");
                var anchor = entry.GetProperty("anchor").GetString()!;
                Assert.Contains(anchor, File.ReadAllText(path), StringComparison.Ordinal);
                Assert.Equal("KEEP_UNTIL_P07", entry.GetProperty("disposition").GetString());
            }
        }

        [Fact]
        public void FieldMap_TypedFields_MatchVendoredProtoDescriptor()
        {
            using var doc = Load("toolfunction-persisted.json");
            var root = doc.RootElement;

            AssertFieldsMatch(root.GetProperty("typedRegistration").GetProperty("fields"), RegisterToolFunctionRequest.Descriptor.Fields.InFieldNumberOrder());
            AssertFieldsMatch(root.GetProperty("typedResponse").GetProperty("fields"), RegisterToolFunctionResponse.Descriptor.Fields.InFieldNumberOrder());

            var version = root.GetProperty("typedRegistration").GetProperty("fields").EnumerateArray()
                .Single(f => f.GetProperty("number").GetInt32() == RegisterToolFunctionRequest.CanonicalizationVersionFieldNumber);
            Assert.Equal("w2-set-v1", version.GetProperty("value").GetString());
        }

        [Fact]
        public void FieldMap_ClassifiesEveryToolFunctionRecipeProperty()
        {
            using var doc = Load("toolfunction-persisted.json");
            var root = doc.RootElement;
            var classified = new HashSet<string>(StringComparer.Ordinal);

            foreach (var field in root.GetProperty("typedRegistration").GetProperty("fields").EnumerateArray())
            {
                if (field.TryGetProperty("recipeProperties", out var props))
                {
                    classified.UnionWith(props.EnumerateArray().Select(p => p.GetString()!));
                }

                if (field.TryGetProperty("portProperties", out var portProps))
                {
                    Assert.All(portProps.EnumerateArray(), p => Assert.NotNull(typeof(PortContract).GetProperty(p.GetString()!)));
                }
            }

            classified.UnionWith(root.GetProperty("recipePropertiesNotOnWire").EnumerateArray().Select(p => p.GetProperty("property").GetString()!));
            classified.UnionWith(root.GetProperty("identityRoles").EnumerateArray()
                .Select(r => r.GetProperty("recipeProperty"))
                .Where(p => p.ValueKind == JsonValueKind.String)
                .Select(p => p.GetString()!));

            var actual = typeof(ToolFunctionRecipe).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Empty(actual.Except(classified));
            Assert.Empty(classified.Except(actual));
        }

        [Fact]
        public void FieldMap_RawSpecKeys_MatchPinnedSchemaFacts()
        {
            using var map = Load("toolfunction-persisted.json");
            using var manifest = Load("contract-source-manifest.json");
            var raw = map.RootElement.GetProperty("rawSpecV1");
            var schema = manifest.RootElement.GetProperty("producer").GetProperty("sources").EnumerateArray()
                .Single(s => s.GetProperty("path").GetString()!.EndsWith("nodevault.build.raw_spec.v1.schema.json", StringComparison.Ordinal))
                .GetProperty("frozenFacts");

            var keys = raw.GetProperty("exactKeys").EnumerateArray().Select(k => k.GetString()).ToArray();
            Assert.Equal(schema.GetProperty("required").EnumerateArray().Select(k => k.GetString()), keys);
            Assert.Equal(keys, raw.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("key").GetString()));
            Assert.False(raw.GetProperty("additionalProperties").GetBoolean());

            var fields = raw.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("key").GetString()!, StringComparer.Ordinal);
            Assert.Equal(schema.GetProperty("schemaVersionConst").GetString(), fields["schema_version"].GetProperty("value").GetString());
            Assert.Equal(schema.GetProperty("kindConst").GetInt32(), fields["kind"].GetProperty("value").GetInt32());
            Assert.Equal(schema.GetProperty("baseImageDigestPattern").GetString(), fields["base_image_digest"].GetProperty("pattern").GetString());
            Assert.Equal("A", fields["base_image_digest"].GetProperty("role").GetString());
        }

        [Fact]
        public void IdentityRoles_SamplesAreDistinct_AndMatchDeclaredPatterns()
        {
            using var doc = Load("toolfunction-persisted.json");
            var roles = doc.RootElement.GetProperty("identityRoles").EnumerateArray()
                .ToDictionary(r => r.GetProperty("role").GetString()!, StringComparer.Ordinal);

            Assert.Equal(new[] { "A", "B", "C1", "C2" }, roles.Keys.OrderBy(k => k, StringComparer.Ordinal));
            var samples = roles.Values.Select(r => r.GetProperty("sample").GetString()!).ToArray();
            Assert.Equal(samples.Length, samples.Distinct(StringComparer.Ordinal).Count());

            foreach (var role in roles.Values)
            {
                var pattern = role.GetProperty("pattern");
                if (pattern.ValueKind == JsonValueKind.String)
                {
                    Assert.Matches(new Regex(pattern.GetString()!, RegexOptions.CultureInvariant), role.GetProperty("sample").GetString()!);
                }
            }

            var a = roles["A"].GetProperty("sample").GetString()!;
            var c1 = roles["C1"].GetProperty("sample").GetString()!;
            Assert.NotEqual(a["sha256:".Length..], c1);
            Assert.DoesNotMatch(new Regex(roles["C1"].GetProperty("pattern").GetString()!, RegexOptions.CultureInvariant), a);

            var states = doc.RootElement.GetProperty("linkEvidence").GetProperty("states").EnumerateArray().ToArray();
            var same = states.Single(s => s.GetProperty("id").GetString() == "OBSERVED_SAME_BUILD");
            Assert.Equal(new[] { "endpoint", "request_id", "build_id", "C1", "A" }, same.GetProperty("requires").EnumerateArray().Select(r => r.GetString()));
            Assert.Contains(states, s => s.GetProperty("id").GetString() == "MANUAL_UNVERIFIED");
            Assert.False(doc.RootElement.GetProperty("linkEvidence").GetProperty("storedLabelRestoresState").GetBoolean());
        }

        [Fact]
        public void SourceManifest_ProtoPinEqualsProvenance_AndVendoredBytes()
        {
            using var manifest = Load("contract-source-manifest.json");
            using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Join(RepoRoot(), "protos", "provenance.json")));
            var producer = manifest.RootElement.GetProperty("producer");
            var protoEntry = producer.GetProperty("sources").EnumerateArray()
                .Single(s => s.GetProperty("path").GetString() == "protos/nodevault/v1/nodevault.proto");
            var pinned = provenance.RootElement.GetProperty("sources").EnumerateArray()
                .Single(s => s.GetProperty("consumerPath").GetString() == protoEntry.GetProperty("consumerPath").GetString());

            Assert.Equal(pinned.GetProperty("producer").GetProperty("revision").GetString(), producer.GetProperty("revision").GetString());
            Assert.Equal(pinned.GetProperty("producer").GetProperty("gitBlobSha1").GetString(), protoEntry.GetProperty("gitBlobSha1").GetString());
            Assert.Equal(pinned.GetProperty("producer").GetProperty("sha256").GetString(), protoEntry.GetProperty("sha256").GetString());

            var vendored = File.ReadAllBytes(Path.Join(RepoRoot(), protoEntry.GetProperty("consumerPath").GetString()));
            Assert.Equal(protoEntry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(vendored)));

            foreach (var path in manifest.RootElement.GetProperty("consumerCode").EnumerateArray())
            {
                Assert.True(File.Exists(Path.Join(RepoRoot(), path.GetString())), $"missing consumer source: {path.GetString()}");
            }
        }

        [Fact]
        public void PortableTransfer_PropertiesExistOnModelTypes_AndLayoutKindsHold()
        {
            using var doc = Load("portable-authoring-transfer.json");
            var root = doc.RootElement;
            var assembly = typeof(ToolFunctionRecipe).Assembly;

            foreach (var group in new[] { "hostRelativeProperties", "imageInternalProperties" })
            {
                foreach (var entry in root.GetProperty(group).EnumerateArray())
                {
                    var type = assembly.GetType("NodeKit.Authoring.ToolFunctionRecipes." + entry.GetProperty("type").GetString(), throwOnError: true)!;
                    Assert.NotNull(type.GetProperty(entry.GetProperty("property").GetString()!));
                }
            }

            var layout = root.GetProperty("exampleLayout");
            Assert.All(layout.GetProperty("companions").EnumerateArray(), c =>
            {
                var value = c.GetProperty("value").GetString()!;
                Assert.False(Path.IsPathRooted(value), value);
                Assert.StartsWith("./", value, StringComparison.Ordinal);
            });
            Assert.All(layout.GetProperty("imageInternal").EnumerateArray(), c => Assert.StartsWith("/", c.GetProperty("value").GetString()!, StringComparison.Ordinal));

            var absolute = root.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("id").GetString() == "transfer-absolute-host-path");
            Assert.Equal(2, absolute.GetProperty("expected").GetProperty("exit").GetInt32());
            Assert.Equal(0, absolute.GetProperty("expected").GetProperty("businessRpc").GetInt32());
            Assert.All(root.GetProperty("variants").EnumerateArray(), v => Assert.Equal("NOT_RUN", v.GetProperty("verification").GetString()));
        }

        [Fact]
        public void CriterionStageMap_CoversTheTenRequiredIds()
        {
            using var doc = Load("cli-acceptance-contract.json");
            var map = doc.RootElement.GetProperty("criterionStageMap").EnumerateArray()
                .ToDictionary(u => u.GetProperty("unit").GetString()!, StringComparer.Ordinal);

            Assert.Equal("P01.support:R", map["S1-01"].GetProperty("closesWith").GetString());
            Assert.Equal("P01.contract:R", map["S3-01"].GetProperty("closesWith").GetString());

            var ids = map.Values.SelectMany(u => u.GetProperty("criteria").EnumerateArray().Select(c => c.GetString()!)).ToArray();
            var expected = Enumerable.Range(1, 7).Select(i => $"S1-01-C{i:D2}").Concat(new[] { "S3-01-A", "S3-01-B", "S3-01-C" });
            Assert.Equal(expected, ids);
        }

        private static void AssertFieldsMatch(JsonElement fixtureFields, IList<Google.Protobuf.Reflection.FieldDescriptor> descriptorFields)
        {
            var fixture = fixtureFields.EnumerateArray()
                .Select(f => (Number: f.GetProperty("number").GetInt32(), Name: f.GetProperty("name").GetString()!))
                .ToArray();
            var descriptor = descriptorFields.Select(f => (Number: f.FieldNumber, f.Name)).ToArray();
            Assert.Equal(descriptor, fixture);
        }

        private static JsonElement LoadRoot(string fileName)
        {
            using var doc = Load(fileName);
            return doc.RootElement.Clone();
        }

        private static JsonDocument Load(string fileName)
        {
            var path = Path.Join(RepoRoot(), "tests", "NodeKit.Cli.Tests", "Fixtures", "Contract", fileName);
            return JsonDocument.Parse(File.ReadAllText(path));
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Join(dir.FullName, "NodeKit.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new FileNotFoundException("repo root(NodeKit.sln)를 찾지 못했습니다.");
        }
    }
}
