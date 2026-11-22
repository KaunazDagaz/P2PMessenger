using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed class DeleteAllDataPageViewModel : PageViewModel
{
    public DeleteAllDataPageViewModel(DeleteAllDataViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        Panel = panel;
    }

    public DeleteAllDataViewModel Panel { get; }

    public override string Title => UiText.DeleteAllDataTitle;
}
