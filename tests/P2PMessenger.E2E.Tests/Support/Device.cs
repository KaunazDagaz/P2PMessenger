using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Application;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Tests.Shared;
using P2PMessenger.Transport.Connections;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.E2E.Tests.Support;

internal sealed class Device : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMilliseconds(500);

    private static readonly ConnectionOptions FastReconnects = new()
    {
        ReconnectDelayMin = TimeSpan.FromMilliseconds(50),
        ReconnectDelayMax = TimeSpan.FromMilliseconds(400),
    };

    private readonly LoopbackDiscovery hub;
    private readonly DeviceKeyStore keyStore = new();
    private readonly string root;
    private AppServices? services;
    private Window? window;

    public Device(LoopbackDiscovery hub, string name)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        this.hub = hub;
        Name = name;
        root = Path.Combine(Path.GetTempPath(), string.Create(CultureInfo.InvariantCulture, $"p2pmessenger-e2e-{Guid.NewGuid():N}"));
        DataFolder = Path.Combine(root, name);
    }

    public string Name { get; }

    public string DataFolder { get; }

    public string DatabasePath => Path.Combine(DataFolder, DatabaseLocalData.FileName);

    public bool IsRunning => window is not null;

    public AppServices Services => services ?? throw new InvalidOperationException($"The app of {Name} is not running.");

    public Window Window => window ?? throw new InvalidOperationException($"The app of {Name} is not running.");

    public string? Title => TryNamed<TextBlock>("TitleText")?.Text;

    public async Task LaunchAsync()
    {
        if (window is not null)
        {
            throw new InvalidOperationException($"The app of {Name} is already running.");
        }

        Directory.CreateDirectory(DataFolder);
        LoopbackDiscovery.Node node = hub.CreateNode();
        MessengerSetup setup = MessengerSetup.InMemory() with
        {
            ListenAddress = IPAddress.Loopback,
            ListenPort = 0,
            Connection = FastReconnects,
            Discovery = node.Setup,
            Loggers = NullLoggerFactory.Instance,
        };
        TopLevelServices topLevelServices = new(() => window);
        services = new AppServices(new DatabaseLocalData(DatabasePath, keyStore), setup, new AvaloniaUiThread(), topLevelServices, topLevelServices, NoQrScanner.Instance);
        MainViewModel shell = new(services);
        window = new MainWindow { DataContext = shell, Width = 900, Height = 1000 };
        window.Show();
        Settle();
        await shell.StartAsync(CancellationToken.None);
        Settle();
    }

    public async Task QuitAsync()
    {
        Window? closing = window;
        AppServices? stopping = services;
        window = null;
        services = null;
        closing?.Close();
        if (stopping is not null)
        {
            await stopping.DisposeAsync();
        }

        Dispatcher.UIThread.RunJobs();
    }

    public IReadOnlyList<string> Texts()
    {
        Dispatcher.UIThread.RunJobs();
        IEnumerable<string> blocks = Window.GetVisualDescendants().OfType<TextBlock>().Where(static block => block.IsEffectivelyVisible).Select(static block => block.Text ?? string.Empty);
        IEnumerable<string> labels = Window.GetVisualDescendants().OfType<Button>().Where(static button => button.IsEffectivelyVisible).Select(static button => button.Content).OfType<string>();
        return [.. blocks, .. labels];
    }

    public bool Shows(string text) => Texts().Contains(text, StringComparer.Ordinal);

    public string Summary() => string.Join(" | ", Texts());

    public T? TryNamed<T>(string name)
        where T : Control =>
        Window.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.IsEffectivelyVisible && string.Equals(control.Name, name, StringComparison.Ordinal));

    public T Named<T>(string name)
        where T : Control =>
        TryNamed<T>(name) ?? throw new InvalidOperationException($"The window of {Name} shows no {typeof(T).Name} named {name}. It shows: {Summary()}");

    public Button? ButtonLabelled(string label) =>
        Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyVisible && string.Equals(button.Content as string, label, StringComparison.Ordinal));

    public Button? ContactButton(string contactName) =>
        TryNamed<ItemsControl>("ContactItems")?.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.IsEffectivelyVisible && string.Equals(AutomationProperties.GetName(button), contactName, StringComparison.Ordinal));

    public IReadOnlyList<string> ContactNames() =>
        [.. Named<ItemsControl>("ContactItems").GetVisualDescendants().OfType<Button>().Select(static button => AutomationProperties.GetName(button)).OfType<string>()];

    public void Click(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!TryClick(target, out string? problem))
        {
            throw new InvalidOperationException(problem);
        }
    }

    public void ClickButton(string buttonName) => Click(Named<Button>(buttonName));

    public void Clear(string boxName)
    {
        Named<TextBox>(boxName).Text = string.Empty;
        Dispatcher.UIThread.RunJobs();
    }

    public void Type(string boxName, string text)
    {
        TextBox box = Named<TextBox>(boxName);
        Click(box);
        Window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
        if (!string.Equals(box.Text, text, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Name} typed into {boxName}, but the box does not hold the typed text.");
        }
    }

    public async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        ArgumentNullException.ThrowIfNull(condition);

        DateTime deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Within the time allowed, this did not happen: {description}. The window of {Name} shows: {Summary()}");
            }

            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }
    }

    public async Task ClickUntilAsync(Func<Control?> find, Func<bool> done, string description)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(done);

        DateTime deadline = DateTime.UtcNow + Patience;
        string? lastProblem = null;
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (done())
            {
                return;
            }

            Settle();
            if (find() is { } target && !TryClick(target, out string? problem))
            {
                lastProblem = problem;
            }

            DateTime settled = DateTime.UtcNow + RetryAfter;
            while (DateTime.UtcNow < settled)
            {
                Dispatcher.UIThread.RunJobs();
                if (done())
                {
                    return;
                }

                await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
            }

            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Within the time allowed, clicking did not do this: {description}. The last click that could not be made: {lastProblem ?? "none"}. The window of {Name} shows: {Summary()}");
            }
        }
    }

    public Task WaitForTitleAsync(string title) =>
        WaitUntilAsync(() => string.Equals(Title, title, StringComparison.Ordinal), $"{Name} shows the page titled {title}");

    public Task WaitForTextAsync(string text) =>
        WaitUntilAsync(() => Shows(text), $"{Name} shows the text {text}");

    public async Task<bool> DatabaseFilesContainAsync(string text)
    {
        byte[] needle = Encoding.UTF8.GetBytes(text);
        foreach (string path in Directory.EnumerateFiles(DataFolder, DatabaseLocalData.FileName + "*"))
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] content = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(content, TestContext.Current.CancellationToken);
            if (content.AsSpan().IndexOf(needle) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await QuitAsync();
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
    }

    private static string Describe(Control control) =>
        string.IsNullOrEmpty(control.Name) ? control.GetType().Name : $"{control.GetType().Name} {control.Name}";

    private bool TryClick(Control target, [NotNullWhen(false)] out string? problem)
    {
        Settle();
        if (!target.IsEffectivelyVisible || !target.IsEffectivelyEnabled)
        {
            problem = $"{Name} cannot click {Describe(target)}: it is hidden or disabled. The window shows: {Summary()}";
            return false;
        }

        Point? centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window);
        if (centre is not { } point)
        {
            problem = $"{Name} cannot find {Describe(target)} in the window.";
            return false;
        }

        Window.MouseMove(point);
        Window.MouseDown(point, MouseButton.Left);
        Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        problem = null;
        return true;
    }

    private void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
