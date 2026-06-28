using Android.Webkit;

namespace WalkHop;

/// <summary>
/// WebChromeClient, der Web-Geolocation (navigator.geolocation) im WebView ZULÄSST.
/// Standardmäßig würde der WebView die Geolocation-Abfrage stumm ablehnen – ohne
/// diese Freigabe bekäme die Navigation kein GPS. Die native Laufzeit-Berechtigung
/// (ACCESS_FINE_LOCATION) wird separat über MAUI-Permissions angefragt.
/// </summary>
public class SpinWebChromeClient : WebChromeClient
{
    public override void OnGeolocationPermissionsShowPrompt(string? origin, GeolocationPermissions.ICallback? callback)
    {
        // origin erlauben, nicht dauerhaft merken (retain=false).
        callback?.Invoke(origin, true, false);
    }
}
