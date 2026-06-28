namespace SpinNaviApp;

public partial class AppShell : Shell
{
#if DEBUG
	private FlyoutItem? _selbsttestItem;
#endif

	public AppShell()
	{
		InitializeComponent();
		// „Start" zeigt jetzt die OSM-Karte (UebersichtPage als Flyout-Inhalt) – keine
		// separate uebersicht-Route mehr nötig.

		// Flyout-/Menü-Titel mehrsprachig setzen (Shell-Items aktualisieren Bindings auf Indexer
		// nicht zuverlässig live, daher imperativ + bei Sprachwechsel neu setzen).
		TexteSetzen();
		L.Geaendert += TexteSetzen;

#if DEBUG
		// Selbsttest (WebView-Testlauf) ist im Release gar nicht erst kompiliert (csproj).
		// Hier zusätzlich nur für Admin sichtbar und direkt VOR „Beenden" – reaktiv auf
		// Login/Logout (Auth-Status-Event).
		Auth.StatusGeaendert += MenueAktualisieren;
		MenueAktualisieren();
#endif
	}

	private void TexteSetzen()
	{
		FiStart.Title = L.T("nav_start");
		FiNavigation.Title = L.T("nav_navigation");
		FiKonto.Title = L.T("nav_konto");
		FiEinstellungen.Title = L.T("nav_einstellungen");
		MiBeenden.Text = L.T("nav_beenden");
#if DEBUG
		if (_selbsttestItem != null) _selbsttestItem.Title = L.T("nav_selbsttest");
#endif
	}

#if DEBUG
	private void MenueAktualisieren()
	{
		bool admin = Auth.IstAdmin;
		if (admin && _selbsttestItem == null)
		{
			_selbsttestItem = new FlyoutItem
			{
				Title = L.T("nav_selbsttest"),
				Route = "selbsttest",
				Items = { new ShellContent { ContentTemplate = new DataTemplate(typeof(TestPage)), Route = "TestPage" } },
			};
			// vor dem ersten Nicht-FlyoutItem (= „Beenden"-MenuItem) einfügen
			int idx = Items.Count;
			for (int i = 0; i < Items.Count; i++)
				if (Items[i] is not FlyoutItem) { idx = i; break; }
			Items.Insert(idx, _selbsttestItem);
		}
		else if (!admin && _selbsttestItem != null)
		{
			Items.Remove(_selbsttestItem);
			_selbsttestItem = null;
		}
	}
#endif

	private async void OnLogout(object? sender, EventArgs e)
	{
		FlyoutIsPresented = false;
		bool ja = await DisplayAlert(L.T("logout_titel"), L.T("logout_frage"), L.T("logout_titel"), L.T("abbrechen"));
		if (!ja) return;
		MainPage.AktiveSitzungBeenden();
		await Auth.AbmeldenAsync();
		await GoToAsync("//start");
	}
}
