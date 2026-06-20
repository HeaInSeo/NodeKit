using Avalonia.Controls;
using NodeKit.UI;
using Xunit;

namespace NodeKit.Tests
{
    public class MainWindowFormHelpersTests
    {
        [Fact]
        public void CollectInputSpecs_WhenRequiredCheckBoxUnchecked_MapsRequiredFalse()
        {
            var panel = new StackPanel();
            var row = BuildInputRow(name: "reads", requiredChecked: false);
            panel.Children.Add(row);

            var inputs = MainWindowFormHelpers.CollectInputSpecs(panel);

            var input = Assert.Single(inputs);
            Assert.False(input.Required);
        }

        [Fact]
        public void CollectInputSpecs_WhenRequiredCheckBoxChecked_MapsRequiredTrue()
        {
            var panel = new StackPanel();
            var row = BuildInputRow(name: "reads", requiredChecked: true);
            panel.Children.Add(row);

            var inputs = MainWindowFormHelpers.CollectInputSpecs(panel);

            var input = Assert.Single(inputs);
            Assert.True(input.Required);
        }

        private static Grid BuildInputRow(string name, bool requiredChecked)
        {
            var row = new Grid();
            row.Children.Add(new TextBox { Text = name });
            MainWindowFormHelpers.AddRequiredCheckBox(row, 0).IsChecked = requiredChecked;
            return row;
        }
    }
}
