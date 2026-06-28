using Foundation;
using UIKit;

namespace WalkHop;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	// Custom-Scheme Deep-Link: walkhop://g/<code> öffnet die App (registriert via CFBundleURLTypes
	// in Info.plist). Cold- UND Warm-Start gehen unter iOS über diesen OpenUrl-Callback. Die URL
	// wird an den gemeinsamen Handler weitergereicht.
	public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
	{
		var s = url?.AbsoluteString;
		if (!string.IsNullOrEmpty(s) && s.StartsWith("walkhop:", StringComparison.OrdinalIgnoreCase))
		{
			DeepLink.Behandeln(s);
			return true;
		}
		return base.OpenUrl(app, url, options);
	}
}
