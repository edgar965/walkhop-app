namespace SpinNaviApp;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		// „Start" zeigt jetzt die OSM-Karte (UebersichtPage als Flyout-Inhalt) – keine
		// separate uebersicht-Route mehr nötig.

#if DEBUG
		// Nur Entwickler-/Admin-Builds: zusätzlich den Selbsttest als Flyout-Eintrag.
		// Im Release ist die TestPage gar nicht erst kompiliert (csproj).
		Items.Add(new FlyoutItem
		{
			Title = "Selbsttest",
			Route = "selbsttest",
			Items = { new ShellContent { ContentTemplate = new DataTemplate(typeof(TestPage)), Route = "TestPage" } },
		});
#endif
	}

	private async void OnLogout(object? sender, EventArgs e)
	{
		FlyoutIsPresented = false;
		bool ja = await DisplayAlert("Abmelden", "Du wirst abgemeldet (es wird wieder ein anonymes Testkonto genutzt).", "Abmelden", "Abbrechen");
		if (!ja) return;
		MainPage.AktiveSitzungBeenden();
		await Auth.AbmeldenAsync();
		await GoToAsync("//start");
	}
}
