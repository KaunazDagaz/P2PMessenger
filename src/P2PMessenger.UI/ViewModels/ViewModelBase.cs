using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    internal FailureLog FailureLog { get; set; } = FailureLog.None;

    protected void ReportFailure(Exception exception, [CallerMemberName] string operation = "") =>
        FailureLog.Report(GetType(), operation, exception);
}
