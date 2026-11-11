using Android.Content;
using Android.Net.Wifi;
using P2PMessenger.Spikes.Nearby.Net;

namespace P2PMessenger.Spikes.Nearby.Android;

internal sealed class AndroidHooks(Context context, bool useMulticastLock, bool useNsd) : IPlatformHooks
{
    public IReadOnlyList<LocalInterface>? EnumerateInterfaces()
    {
        try
        {
            IReadOnlyList<LocalInterface> java = AndroidInterfaces.EnumerateJava();
            if (java.Count > 0)
            {
                return java;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.WriteLine($"The Java interface list failed ({exception.GetType().Name}: {exception.Message}); using .NET's.");
        }

        return null;
    }

    public IDisposable? AcquireMulticastLock()
    {
        if (!useMulticastLock)
        {
            Console.WriteLine("MulticastLock: not held (switched off in the app).");
            return null;
        }

        if (context.GetSystemService(Context.WifiService) is not WifiManager wifi)
        {
            Console.WriteLine("MulticastLock: no WifiManager.");
            return null;
        }

        WifiManager.MulticastLock? multicastLock = wifi.CreateMulticastLock("nearby-spike");
        if (multicastLock is null)
        {
            Console.WriteLine("MulticastLock: Android returned none.");
            return null;
        }

        multicastLock.SetReferenceCounted(false);
        multicastLock.Acquire();
        return new LockHandle(multicastLock);
    }

    public IExtraDiscovery? CreateExtraDiscovery()
    {
        if (!useNsd)
        {
            Console.WriteLine("NsdManager: switched off in the app.");
            return null;
        }

        return new NsdDiscovery(context);
    }

    private sealed class LockHandle(WifiManager.MulticastLock multicastLock) : IDisposable
    {
        public void Dispose()
        {
            if (multicastLock.IsHeld)
            {
                multicastLock.Release();
            }

            multicastLock.Dispose();
        }
    }
}
