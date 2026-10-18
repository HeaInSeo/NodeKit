using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// #109 / S4-05-A: NODEKIT_HARBOR_IMAGE_MAP를 바꾸는 test class가 공유 non-parallel collection에서 빠지면
    /// 경합은 타이밍이 맞을 때만 실패한다(CI 반복 실행 20회도 놓칠 수 있다). 그래서 배선 자체를 결정적으로 확인한다.
    /// </summary>
    public class HarborImageMapEnvironmentIsolationTests
    {
        private const string SetMapCall = "SetEnvironmentVariable(\"NODEKIT_HARBOR_IMAGE_MAP\"";

        [Fact]
        public void CollectionDefinition_DisablesParallelization()
        {
            var definition = typeof(HarborImageMapEnvironmentCollection).GetCustomAttributesData()
                .Single(a => a.AttributeType == typeof(CollectionDefinitionAttribute));

            Assert.Equal(nameof(HarborImageMapEnvironmentCollection), definition.ConstructorArguments.Single().Value);
            var disable = definition.NamedArguments.Single(a => a.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization));
            Assert.Equal(true, disable.TypedValue.Value);
        }

        [Fact]
        public void EveryClassThatSetsTheMap_IsInTheSharedCollection()
        {
            var testDir = Path.Join(RepoRoot(), "tests", "NodeKit.Cli.Tests");
            var settingFiles = Directory.EnumerateFiles(testDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains(SetMapCall, StringComparison.Ordinal))
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            // 새 class가 이 env를 바꾸면 여기에 추가하고 같은 collection에 넣는다.
            Assert.Equal(
                new[] { nameof(HarborBaseImageSelectionTests), nameof(MappedHarborImageDigestResolverTests) },
                settingFiles);

            foreach (var type in new[] { typeof(HarborBaseImageSelectionTests), typeof(MappedHarborImageDigestResolverTests) })
            {
                var collection = type.GetCustomAttributesData()
                    .SingleOrDefault(a => a.AttributeType == typeof(CollectionAttribute));
                Assert.True(collection is not null, $"{type.Name}에 [Collection]이 없습니다.");
                Assert.Equal(nameof(HarborImageMapEnvironmentCollection), collection!.ConstructorArguments.Single().Value);
            }
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
