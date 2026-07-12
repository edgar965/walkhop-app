using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
#if ANDROID && DEBUG
using Android.Webkit;
#endif

namespace WalkHop;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		Protokoll.Registrieren();   // Logging/Crash-Handler sicher initialisieren (ModuleInitializer ist auf Android unzuverlässig)
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseSkiaSharp()          // Mapsui rendert die native Karte über SkiaSharp
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if ANDROID || IOS
		// GPU-Rendering der Karte erzwingen (SKGLView statt CPU-SKCanvasView). Mapsui lässt GPU auf
		// MAUI per Default aus, weil SkiaSharp.Views.Maui KEINEN SKGLView-Handler registriert. Wir
		// liefern das GL-Rendering über den Kompatibilitäts-Renderer nach. Ohne GPU ruckelt der
		// Pinch-Zoom auf schwacher Hardware (CPU-Rendering jedes Frame).
		Mapsui.UI.Maui.MapControl.UseGPU = true;
		builder.ConfigureMauiHandlers(handlers =>
			handlers.AddHandler(typeof(SkiaSharp.Views.Maui.Controls.SKGLView),
				typeof(SkiaSharp.Views.Maui.Controls.Compatibility.SKGLViewRenderer)));
#endif

#if ANDROID && DEBUG
		// Nur Debug: Der einzige WebView ist der Selbsttest (TestPage). Ihn so
		// konfigurieren, dass die spin1more-Navigation darin läuft (JavaScript,
		// DOM-Storage, Web-Geolocation). Im Release gibt es keinen WebView.
		Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("spin1more-webview", (handler, view) =>
		{
			var wv = handler.PlatformView;
			wv.Settings.JavaScriptEnabled = true;
			wv.Settings.DomStorageEnabled = true;
			wv.Settings.SetGeolocationEnabled(true);
			wv.Settings.MediaPlaybackRequiresUserGesture = false;
			wv.SetWebChromeClient(new SpinWebChromeClient());
		});
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
