using Android.Runtime;
using Avalonia.Android;
using P2PMessenger.UI;

namespace P2PMessenger.Android;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    protected MainApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
