using System.Globalization;
using System.Text;
using Android.Content;
using P2PMessenger.Application.Background;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.Android.Background;

internal static class Notifications
{
    public const string ServiceChannel = "p2pm.service";
    public const string MessagesChannel = "p2pm.messages";
    public const string LockScreenChannel = "p2pm.lockscreen";

    private const int OpenRequest = 1;
    private const int QuitRequest = 2;

    private static readonly CompositeFormat GroupMessageFrom = CompositeFormat.Parse(UiText.NotificationGroupMessageFromFormat);
    private static readonly CompositeFormat GroupPreview = CompositeFormat.Parse(UiText.NotificationGroupPreviewFormat);

    public static void EnsureChannels(Context context)
    {
        NotificationManager? manager = Manager(context);
        if (manager is null)
        {
            return;
        }

        using NotificationChannel service = new(ServiceChannel, UiText.NotificationChannelService, NotificationImportance.Low);
        service.SetShowBadge(false);
        service.LockscreenVisibility = NotificationVisibility.Private;
        manager.CreateNotificationChannel(service);

        using NotificationChannel messages = new(MessagesChannel, UiText.NotificationChannelMessages, NotificationImportance.High);
        messages.LockscreenVisibility = NotificationVisibility.Secret;
        manager.CreateNotificationChannel(messages);

        using NotificationChannel lockScreen = new(LockScreenChannel, UiText.NotificationChannelLockScreen, NotificationImportance.Default);
        lockScreen.SetSound(null, null);
        lockScreen.EnableVibration(false);
        lockScreen.SetShowBadge(false);
        lockScreen.LockscreenVisibility = NotificationVisibility.Public;
        manager.CreateNotificationChannel(lockScreen);
    }

    public static Notification BuildService(Context context)
    {
        EnsureChannels(context);
        using Intent quitIntent = new(context, typeof(QuitActivity));
        _ = quitIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ExcludeFromRecents);
        using PendingIntent? quit = PendingIntent.GetActivity(context, QuitRequest, quitIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        using Notification.Action quitAction = new Notification.Action.Builder(null, UiText.NotificationQuit, quit).Build()
            ?? throw new InvalidOperationException("Android returned no notification action.");
        using Notification.Builder builder = new(context, ServiceChannel);
        _ = builder.SetContentTitle(UiText.NotificationServiceTitle)
            ?.SetContentText(UiText.NotificationServiceText)
            ?.SetSmallIcon(global::Android.Resource.Drawable.StatNotifyChat)
            ?.SetOngoing(true)
            ?.SetOnlyAlertOnce(true)
            ?.SetShowWhen(false)
            ?.SetVisibility(NotificationVisibility.Private)
            ?.SetContentIntent(OpenApp(context))
            ?.AddAction(quitAction);
        return builder.Build() ?? throw new InvalidOperationException("Android returned no notification.");
    }

    public static void ShowMessage(Context context, MessageNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        NotificationManager? manager = Manager(context);
        if (manager is null)
        {
            return;
        }

        EnsureChannels(context);
        using Notification.Builder builder = new(context, MessagesChannel);
        _ = builder.SetContentTitle(notice.SenderName)
            ?.SetContentText(notice.Preview ?? UiText.NotificationNewMessage)
            ?.SetSmallIcon(global::Android.Resource.Drawable.StatNotifyChat)
            ?.SetCategory(Notification.CategoryMessage)
            ?.SetVisibility(NotificationVisibility.Secret)
            ?.SetAutoCancel(true)
            ?.SetContentIntent(OpenApp(context));
        if (notice.Preview is { } preview)
        {
            using Notification.BigTextStyle style = new();
            _ = builder.SetStyle(style.BigText(preview));
        }

        using Notification notification = builder.Build() ?? throw new InvalidOperationException("Android returned no notification.");
        manager.Notify(BackgroundRules.MessageNotificationId(notice.ContactKey.Bytes), notification);
        ShowLockScreenNoticeIfLocked(context);
    }

    public static void ShowGroupMessage(Context context, GroupMessageNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        NotificationManager? manager = Manager(context);
        if (manager is null)
        {
            return;
        }

        EnsureChannels(context);
        string text = notice.Preview is { } preview
            ? string.Format(CultureInfo.CurrentCulture, GroupPreview, notice.AuthorName, preview)
            : string.Format(CultureInfo.CurrentCulture, GroupMessageFrom, notice.AuthorName);
        using Notification.Builder builder = new(context, MessagesChannel);
        _ = builder.SetContentTitle(notice.GroupName)
            ?.SetContentText(text)
            ?.SetSmallIcon(global::Android.Resource.Drawable.StatNotifyChat)
            ?.SetCategory(Notification.CategoryMessage)
            ?.SetVisibility(NotificationVisibility.Secret)
            ?.SetAutoCancel(true)
            ?.SetContentIntent(OpenApp(context));
        if (notice.Preview is not null)
        {
            using Notification.BigTextStyle style = new();
            _ = builder.SetStyle(style.BigText(text));
        }

        using Notification notification = builder.Build() ?? throw new InvalidOperationException("Android returned no notification.");
        manager.Notify(BackgroundRules.MessageNotificationId(notice.GroupId.Bytes), notification);
        ShowLockScreenNoticeIfLocked(context);
    }

    public static void ShowLockScreenNotice(Context context)
    {
        NotificationManager? manager = Manager(context);
        if (manager is null)
        {
            return;
        }

        EnsureChannels(context);
        using Notification.Builder builder = new(context, LockScreenChannel);
        _ = builder.SetContentTitle(UiText.NotificationNewMessage)
            ?.SetSmallIcon(global::Android.Resource.Drawable.StatNotifyChat)
            ?.SetVisibility(NotificationVisibility.Public)
            ?.SetAutoCancel(true)
            ?.SetContentIntent(OpenApp(context));
        using Notification notification = builder.Build() ?? throw new InvalidOperationException("Android returned no notification.");
        manager.Notify(BackgroundRules.LockScreenNoticeId, notification);
    }

    public static void CancelLockScreenNotice(Context context) => Manager(context)?.Cancel(BackgroundRules.LockScreenNoticeId);

    public static IReadOnlyList<int> ShownIds(Context context)
    {
        if (Manager(context)?.GetActiveNotifications() is not { } shown)
        {
            return [];
        }

        return [.. shown.Select(notification => notification.Id)];
    }

    public static void ShowUnlockNeeded(Context context)
    {
        NotificationManager? manager = Manager(context);
        if (manager is null)
        {
            return;
        }

        EnsureChannels(context);
        using Notification.Builder builder = new(context, ServiceChannel);
        _ = builder.SetContentTitle(UiText.NotificationUnlockTitle)
            ?.SetContentText(UiText.NotificationUnlockText)
            ?.SetSmallIcon(global::Android.Resource.Drawable.StatNotifyChat)
            ?.SetVisibility(NotificationVisibility.Public)
            ?.SetAutoCancel(true)
            ?.SetContentIntent(OpenApp(context));
        using Notification notification = builder.Build() ?? throw new InvalidOperationException("Android returned no notification.");
        manager.Notify(BackgroundRules.UnlockNotificationId, notification);
    }

    public static void CancelUnlockNeeded(Context context) => Manager(context)?.Cancel(BackgroundRules.UnlockNotificationId);

    private static void ShowLockScreenNoticeIfLocked(Context context)
    {
        if (context.GetSystemService(Context.KeyguardService) is KeyguardManager keyguard && keyguard.IsDeviceLocked)
        {
            ShowLockScreenNotice(context);
        }
    }

    private static PendingIntent? OpenApp(Context context)
    {
        using Intent open = new(context, typeof(MainActivity));
        _ = open.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(context, OpenRequest, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
    }

    private static NotificationManager? Manager(Context context) =>
        context.GetSystemService(Context.NotificationService) as NotificationManager;
}
