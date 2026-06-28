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
		// Beim Start: Token/anonymes Geräte-Konto sicherstellen + Reste hochladen.
		window.Created += async (_, _) =>
		{
			await Auth.InitAsync();
			await AufnahmeService.UploadeAusstehendAsync();
			// Cold-Start: ein evtl. beim Start eingegangener Deep-Link (walkhop://g/<code>) wird
			// jetzt angewandt – die Shell ist nun bereit (Beitritt + Navigation zur Karte).
			DeepLink.AusstehendAnwenden();
		};
		// Beim Wiederkehren aus dem Hintergrund einen noch ausstehenden Deep-Link nachholen
		// (Absicherung gegen Timing zwischen Plattform-Intent und bereiter Shell).
		window.Activated += (_, _) => DeepLink.AusstehendAnwenden();
		// Beim App-Ende/Hintergrund: laufende Auto-Aufnahme abschließen, dann Tracks hochladen.
		window.Stopped += async (_, _) =>
		{
			try { WalkHop.MainPage.AktiveAufnahmeSichern(); await AufnahmeService.UploadeAusstehendAsync(); }
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
		};
		return window;
	}
}