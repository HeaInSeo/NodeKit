using NodeKit.Authoring;
using Xunit;

namespace NodeKit.Tests
{
    public class DataDefinitionTests
    {
        [Fact]
        public void NewInstance_HasNonEmptyUniqueId()
        {
            var first = new DataDefinition();
            var second = new DataDefinition();

            Assert.NotEqual(System.Guid.Empty, first.Id);
            Assert.NotEqual(first.Id, second.Id);
        }

        [Fact]
        public void NewInstance_HasEmptyStringDefaultsAndEmptyDisplayTags()
        {
            var def = new DataDefinition();

            Assert.Equal(string.Empty, def.Name);
            Assert.Equal(string.Empty, def.Version);
            Assert.Equal(string.Empty, def.Description);
            Assert.Equal(string.Empty, def.Format);
            Assert.Equal(string.Empty, def.SourceUri);
            Assert.Equal(string.Empty, def.Checksum);
            Assert.Equal(string.Empty, def.DisplayLabel);
            Assert.Equal(string.Empty, def.DisplayDescription);
            Assert.Equal(string.Empty, def.DisplayCategory);
            Assert.Empty(def.DisplayTags);
        }

        [Fact]
        public void NewInstance_CreatedAtIsUtc()
        {
            var def = new DataDefinition();

            Assert.Equal(System.DateTimeKind.Utc, def.CreatedAt.Kind);
        }
    }
}
