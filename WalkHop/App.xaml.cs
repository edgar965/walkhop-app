using Microsoft.Extensions.DependencyInjection;

namespace WalkHop;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		// Demo-/Tages-Limit erreicht (Zeit/Routen/…): Hinweis + Weg zur Registrierung.
		Auth.LimitErreicht += async (art) => await LimitHinweisAsync(window, art);
		// Beim Start: Token/anonymes Geräte-Konto sicherstellen + Reste hochladen.
		window.Created += async (_, _) =>
		{
			DauerGps.Starten();   // GPS empfangen + persistent speichern, solange die App an ist (app-weit, iOS auch im Hintergrund)
			await Auth.InitAsync();
			Nutzung.Starten();    // Nutzungs-Heartbeat (Zeit-Budget + Tracking), nur im Vordergrund
			await AufnahmeService.UploadeAusstehendAsync();
			// Cold-Start: ein evtl. beim Start eingegangener Deep-Link (walkhop://g/<code>) wird
			// jetzt angewandt – die Shell ist nun bereit (Beitritt + Navigation zur Karte).
			DeepLink.AusstehendAnwenden();
			// Erststart-Abfrage (einmalig nach Installation): Sprachansagen + Abbiege-Töne abfragen.
			await ErstkonfigAbfragenAsync(window);
		};
		// Beim Wiederkehren aus dem Hintergrund: Deep-Link nachholen + Heartbeat wieder starten.
		window.Activated += (_, _) => { DeepLink.AusstehendAnwenden(); Nutzung.Starten(); };
		// Beim App-Ende/Hintergrund: Heartbeat stoppen (Restzeit melden), Auto-Aufnahme sichern, Tracks hochladen.
		window.Stopped += async (_, _) =>
		{
			Nutzung.Stoppen();
			try { WalkHop.MainPage.AktiveAufnahmeSichern(); await AufnahmeService.UploadeAusstehendAsync(); }
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Meldung.Fehler("Aufnahme sichern", ex); }
		};
		return window;
	}

	// Tages-Limit der aktuellen Stufe (Demo) erreicht: freundlicher Hinweis mit dem Weg
	// zur Registrierung. Robust gegen „Seite noch nicht bereit" (nutzt die aktuelle Seite).
	private static async Task LimitHinweisAsync(Window window, string art)
	{
		try
		{
			var seite = Shell.Current?.CurrentPage ?? window.Page;
			if (seite == null) return;
			string frage = art == "zeit" ? L.T("demo_limit_zeit") : L.T("demo_limit_funktion");
			bool jetzt = await seite.DisplayAlertAsync(L.T("demo_limit_titel"), frage,
				L.T("demo_limit_registrieren"), L.T("demo_limit_spaeter"));
			if (jetzt && Shell.Current != null) await Shell.Current.GoToAsync("//konto");
		}
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
	}

	// Einmalige Erst-Konfiguration nach der Installation: fragt den Nutzer nacheinander, ob
	// Sprachansagen (Einst.Ton) und Abbiege-Töne (Einst.Benachrichtigungstoene) aktiv sein sollen,
	// und merkt sich den Durchlauf via Einst.ErstkonfigErledigt (persistiert → bleibt bei Updates).
	// Bewusst hier in App und nicht in der ersten Seite, weil die Startseite (UebersichtPage) nicht
	// angefasst werden soll. Robust gegen „Shell/Seite noch nicht bereit": wartet kurz auf eine
	// anzeigebereite Seite, bevor die Dialoge erscheinen.
	private static async Task ErstkonfigAbfragenAsync(Window window)
	{
		if (Einst.ErstkonfigErledigt) return;

		// Auf eine anzeigebereite Seite warten (DisplayAlert braucht eine realisierte Page).
		Page? seite = null;
		for (int i = 0; i < 60; i++)
		{
			seite = Shell.Current?.CurrentPage ?? window.Page;
			if (seite is { Handler: not null }) break;
			seite = null;
			await Task.Delay(100);
		}
		if (seite == null) return;   // Seite kam nicht zustande – beim nächsten Start erneut versuchen

		try
		{
			bool sprache = await seite.DisplayAlertAsync(L.T("erst_titel"), L.T("erst_sprache_frage"), L.T("ja"), L.T("nein"));
			Einst.Ton = sprache;

			bool toene = await seite.DisplayAlertAsync(L.T("erst_titel"), L.T("erst_toene_frage"), L.T("ja"), L.T("nein"));
			Einst.Benachrichtigungstoene = toene;

			Einst.ErstkonfigErledigt = true;
		}
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Meldung.Fehler("Ersteinrichtung", ex); }
	}
}