using System.Diagnostics;

namespace SpinNaviApp;

/// <summary>Lokale Abbiege-Notification während der Navigation. Lokale Notifications werden
/// automatisch auf gekoppelte Uhren gespiegelt (Apple Watch, Wear OS, viele Bluetooth-Uhren)
/// – so erscheint der nächste Abbiege-Hinweis ohne eigene Watch-App auf dem Handgelenk.</summary>
public static class NaviNotif
{
    private const int Id = 1001;
    private const string Kennung = "navi_manoever";
    private static bool _kanal;

    /// <summary>Zeigt/aktualisiert den Abbiege-Hinweis (in-place, gleiche ID → kein Spam).</summary>
    public static void Zeige(string titel, string text)
    {
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            var mgr = (Android.App.NotificationManager)ctx.GetSystemService(Android.Content.Context.NotificationService)!;
            Android.App.Notification.Builder b;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                if (!_kanal)
                {
                    var kanal = new Android.App.NotificationChannel(Kennung, "Navigation", Android.App.NotificationImportance.Low);
                    kanal.SetShowBadge(false);
                    mgr.CreateNotificationChannel(kanal);
                    _kanal = true;
                }
                b = new Android.App.Notification.Builder(ctx, Kennung);
            }
            else
            {
#pragma warning disable CA1422
                b = new Android.App.Notification.Builder(ctx);
#pragma warning restore CA1422
            }
            b.SetContentTitle(titel).SetContentText(text)
             .SetSmallIcon(ctx.ApplicationInfo!.Icon)
             .SetOngoing(true).SetOnlyAlertOnce(true);
            mgr.Notify(Id, b.Build());
        }
        catch (System.Exception ex) { Debug.WriteLine(ex); }
#elif IOS || MACCATALYST
        try
        {
            var content = new UserNotifications.UNMutableNotificationContent { Title = titel, Body = text };
            var req = UserNotifications.UNNotificationRequest.FromIdentifier(Kennung, content, null);
            UserNotifications.UNUserNotificationCenter.Current.AddNotificationRequest(req, null);
        }
        catch (System.Exception ex) { Debug.WriteLine(ex); }
#endif
    }

    /// <summary>Kurzer Hinweis-Ton (Benachrichtigungston) für die Einstellung „Benachrichtigungstöne":
    /// wird bei einer Neuberechnung der Route und bei einer neuen Abbiege-Ansage gespielt. Einfacher,
    /// plattformnaher Weg: Android = System-Benachrichtigungston, iOS/macOS = System-Sound. Unter
    /// Windows ist kein einfacher Standard-Beep verfügbar → dort bewusst ohne Ton.</summary>
    public static void Signalton()
    {
#if ANDROID
        try
        {
            var uri = Android.Media.RingtoneManager.GetDefaultUri(Android.Media.RingtoneType.Notification);
            var rt = Android.Media.RingtoneManager.GetRingtone(Android.App.Application.Context, uri);
            rt?.Play();
        }
        catch (System.Exception ex) { Debug.WriteLine(ex); }
#elif IOS || MACCATALYST
        try { new AudioToolbox.SystemSound(1007).PlaySystemSound(); }
        catch (System.Exception ex) { Debug.WriteLine(ex); }
#endif
    }

    /// <summary>Entfernt den Abbiege-Hinweis (Navigation beendet/Ziel erreicht).</summary>
    public static void Aus()
    {
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            var mgr = (Android.App.NotificationManager)ctx.GetSystemService(Android.Content.Context.NotificationService)!;
            mgr.Cancel(Id);
        }
        catch (System.Exception ex) { Debug.WriteLine(ex); }
#elif IOS || MACCATALYST
        try
        {
            UserNotifications.UNUserNotificationCenter.Current.RemoveDeliveredNotifications(new[] { Kennung });
            UserNotifications.UNUserNotificationCenter.Current.RemovePendingNotificationRequests(new[] { Kennung });
        }
        catch (System.Exception ex) { Debug.WriteLine(ex); }
#endif
    }

    /// <summary>Notification-Berechtigung anfordern (Android 13+, iOS) – einmal vor der Navigation.</summary>
    public static async System.Threading.Tasks.Task BerechtigungAsync()
    {
#if ANDROID
        try
        {
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
            {
                var act = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (act != null && AndroidX.Core.Content.ContextCompat.CheckSelfPermission(act, "android.permission.POST_NOTIFICATIONS")
                        != Android.Content.PM.Permission.Granted)
                    AndroidX.Core.App.ActivityCompat.RequestPermissions(act, new[] { "android.permission.POST_NOTIFICATIONS" }, 1002);
            }
        }
        catch (System.Exception ex) { Debug.WriteLine(ex); }
        await System.Threading.Tasks.Task.CompletedTask;
#elif IOS || MACCATALYST
        try
        {
            await UserNotifications.UNUserNotificationCenter.Current
                .RequestAuthorizationAsync(UserNotifications.UNAuthorizationOptions.Alert);
        }
        catch (System.Exception ex) { Debug.WriteLine(ex); }
#else
        await System.Threading.Tasks.Task.CompletedTask;
#endif
    }
}
