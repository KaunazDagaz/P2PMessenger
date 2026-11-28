using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using P2PMessenger.UI.Controls;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class DesignSystemTests
{
    [Theory]
    [InlineData("Alice", "A")]
    [InlineData("alice", "A")]
    [InlineData("Bob Miller", "BM")]
    [InlineData("  bob   van   miller  ", "BM")]
    [InlineData("Anna-Maria", "A")]
    [InlineData("Ёж Иванов", "ЁИ")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData(null, "?")]
    public void InitialsOf_ADisplayName_GivesTheFirstLettersOfTheFirstAndLastWord(string? name, string expected)
    {
        Assert.Equal(expected, Avatar.InitialsOf(name));
    }

    [Fact]
    public void InitialsOf_ANameStartingWithASurrogatePair_KeepsTheWholeCharacter()
    {
        Assert.Equal("\U0001F600", Avatar.InitialsOf("\U0001F600"));
    }

    [Theory]
    [InlineData("Alice")]
    [InlineData("Bob Miller")]
    [InlineData("")]
    [InlineData(null)]
    public void PaletteIndexOf_TheSameName_GivesTheSameColourEveryTime(string? name)
    {
        Assert.Equal(Avatar.PaletteIndexOf(name), Avatar.PaletteIndexOf(name));
    }

    [Fact]
    public void PaletteIndexOf_ManyNames_StaysInsideThePaletteAndUsesSeveralColours()
    {
        string[] names = [.. Enumerable.Range(0, 200).Select(static number => "Contact " + number)];

        int[] indexes = [.. names.Select(Avatar.PaletteIndexOf)];

        Assert.All(indexes, static index => Assert.InRange(index, 0, 6));
        Assert.True(indexes.Distinct().Count() >= 5);
    }

    [AvaloniaFact]
    public void Tokens_LightAndDarkThemes_DefineTheSameKeys()
    {
        ResourceDictionary tokens = Assert.Single(
            Avalonia.Application.Current!.Resources.MergedDictionaries.OfType<ResourceDictionary>(),
            static dictionary => dictionary.ThemeDictionaries.Count > 0);

        var light = Assert.IsType<ResourceDictionary>(tokens.ThemeDictionaries[ThemeVariant.Light]);
        var dark = Assert.IsType<ResourceDictionary>(tokens.ThemeDictionaries[ThemeVariant.Dark]);

        Assert.NotEmpty(light.Keys);
        Assert.Equal(light.Keys.Select(static key => key.ToString()).Order(), dark.Keys.Select(static key => key.ToString()).Order());
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Tokens_EachTheme_ResolvesTheBrushesTheStylesUse(string theme)
    {
        ThemeVariant variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        string[] keys =
        [
            "AppBackgroundBrush", "SurfaceBrush", "SurfaceAltBrush", "SurfaceHoverBrush", "OutlineBrush", "TextPrimaryBrush",
            "TextMutedBrush", "AccentBrush", "AccentSoftBrush", "AccentTextBrush", "OnAccentBrush", "SuccessBrush",
            "SuccessSoftBrush", "SuccessTextBrush", "WarningBrush", "WarningSoftBrush", "WarningTextBrush", "DangerBrush",
            "DangerSoftBrush", "DangerTextBrush", "BubbleOwnBrush", "BubbleOwnTextBrush", "BubbleTheirsBrush", "DisabledTextBrush", "ScrimBrush",
        ];

        foreach (string key in keys)
        {
            Assert.True(Avalonia.Application.Current!.TryGetResource(key, variant, out object? value), $"{key} is missing in {theme}.");
            Assert.IsAssignableFrom<IBrush>(value);
        }
    }

    [AvaloniaTheory]
    [InlineData(900, false)]
    [InlineData(559, true)]
    [InlineData(360, true)]
    public async Task MainView_ByItsWidth_SwitchesTheCompactLayout(double width, bool compact)
    {
        await using var harness = UiHarness.Create();
        var view = new MainView { DataContext = harness.Shell };

        harness.Show(view, width, 720);

        if (compact)
        {
            Assert.Contains("compact", view.Classes);
        }
        else
        {
            Assert.DoesNotContain("compact", view.Classes);
        }
    }

    [AvaloniaTheory]
    [InlineData(800, 24)]
    [InlineData(1100, 14)]
    [InlineData(360, 14)]
    public async Task MainView_ByItsWidth_GivesThePagesTheirPadding(double width, double padding)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new MainView { DataContext = harness.Shell };

        harness.Show(view, width, 720);

        var container = view.GetVisualDescendants().OfType<PageContainer>().First();
        Assert.Equal(padding, container.Padding.Left);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task MainView_ResizedAcrossTheBreakpoint_FollowsTheWidth()
    {
        await using var harness = UiHarness.Create();
        var view = new MainView { DataContext = harness.Shell };
        Window window = harness.Show(view, 900, 720);
        Assert.DoesNotContain("compact", view.Classes);

        window.Width = 400;
        UiHarness.Flush();
        Assert.Contains("compact", view.Classes);

        window.Width = 700;
        UiHarness.Flush();
        Assert.DoesNotContain("compact", view.Classes);
    }

    [AvaloniaFact]
    public async Task IconicButton_WithAGlyph_ShowsTheIconBesideTheTextAndKeepsTheContentAsText()
    {
        await using var harness = UiHarness.Create();
        var glyph = Assert.IsAssignableFrom<Geometry>(Avalonia.Application.Current!.FindResource("IconTrash"));
        var button = new Button { Content = "Delete", Classes = { "iconic" } };
        Icons.SetGlyph(button, glyph);

        harness.Show(button);

        Assert.Equal("Delete", button.Content);
        Assert.Contains("Delete", UiHarness.Texts(button));
        Assert.Same(glyph, Assert.Single(button.GetVisualDescendants().OfType<Icon>()).Data);
    }

    [AvaloniaFact]
    public async Task PageContainer_ContentWiderThanTheLimit_IsKeptWithinTheMaximumWidth()
    {
        await using var harness = UiHarness.Create();
        var inner = new Border { Height = 20 };
        var container = new PageContainer { ContentMaxWidth = 400, Padding = new Thickness(0), Content = inner };

        harness.Show(container, 1000, 600);

        Assert.Equal(400, inner.Bounds.Width, 1);
    }
}
