using System.Diagnostics;

namespace SpinNaviApp;

public partial class StartPage : ContentPage
{
    private const int StartLimit = 100;   // initial nur N Touren in die Liste – die Suche filtert ALLE
    private List<TourInfo> _alleTouren = new();
    private bool _geladen, _laedt;

    public StartPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await TourenLaden();
    }

    private async Task TourenLaden()
    {
        if (_geladen || _laedt) return;
        _laedt = true;
        try
        {
            _alleTouren = await TourService.LadeTourenAsync();
            TourListe.ItemsSource = _alleTouren.Take(StartLimit).ToList();   // initiale Liste leicht halten
            _geladen = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Touren laden: " + ex);
            if (LeerLabel != null) LeerLabel.Text = "Touren konnten nicht geladen werden.";
        }
        finally { _laedt = false; }
    }

    private void OnTourSuche(object? sender, TextChangedEventArgs e)
    {
        var q = (e.NewTextValue ?? "").Trim();
        TourListe.ItemsSource = string.IsNullOrEmpty(q)
            ? _alleTouren.Take(StartLimit).ToList()
            : _alleTouren.Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(StartLimit).ToList();
    }

    private async void OnTourGewaehlt(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0 || e.CurrentSelection[0] is not TourInfo t) return;
        TourListe.SelectedItem = null;
        // Tour an die Navigationsseite übergeben und dorthin wechseln.
        MainPage.GeplanteTour = t;
        await Shell.Current.GoToAsync("//navigation");
    }

    private async void OnNavigation(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//navigation");

    private async void OnUebersicht(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("uebersicht");
}
