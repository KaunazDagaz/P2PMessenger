using System.Collections;
using System.Globalization;
using System.Reflection;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.Tests.Resources;

public sealed class UiTextTests
{
    [Fact]
    public void UiText_DesignerClass_HasExactlyTheNamesOfTheResxFile()
    {
        var resources = UiText.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        Assert.NotNull(resources);

        var inResx = resources.Cast<DictionaryEntry>().Select(static entry => (string)entry.Key).Order(StringComparer.Ordinal);
        var inDesigner = Texts().Select(static property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(inResx, inDesigner);
    }

    [Fact]
    public void UiText_EveryText_IsNotEmpty()
    {
        Assert.All(Texts(), static property => Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(null)), property.Name));
    }

    private static IEnumerable<PropertyInfo> Texts() =>
        typeof(UiText).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(static property => property.PropertyType == typeof(string));
}
