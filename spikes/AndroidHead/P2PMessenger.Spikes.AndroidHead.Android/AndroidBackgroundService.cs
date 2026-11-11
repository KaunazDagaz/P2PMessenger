using Android.Content;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal sealed class AndroidBackgroundService(Context context) : IBackgroundService
{
    public string Start(ServiceRequest request)
    {
        try
        {
            using var intent = new Intent(context, typeof(SpikeService));
            intent.PutExtra(SpikeService.ExtraKind, request.Kind.ToString());
            intent.PutExtra(SpikeService.ExtraWakeLock, request.WakeLock);
            intent.PutExtra(SpikeService.ExtraPort, request.Port);
            _ = context.StartForegroundService(intent);
            return $"Start requested: type {request.Kind}, wake lock {(request.WakeLock ? "yes" : "no")}, port {request.Port}";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return $"Start refused by Android: {exception.GetType().FullName}: {exception.Message}";
        }
    }

    public string Halt()
    {
        using var intent = new Intent(context, typeof(SpikeService));
        return context.StopService(intent) ? "Stop requested" : "The service was not running";
    }
}
