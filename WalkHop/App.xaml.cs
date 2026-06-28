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
		};
		// Beim App-Ende/Hintergrund: laufende Auto-Aufnahme abschließen, dann Tracks hochladen.
		window.Stopped += async (_, _) =>
		{
			try { WalkHop.MainPage.AktiveAufnahmeSichern(); await AufnahmeService.UploadeAusstehendAsync(); }
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
		};
		return window;
	}
}