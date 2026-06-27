using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Maui.Storage;

namespace SpinNaviApp;

/// <summary>Eine Zeile in der Ergebnisliste (live aktualisierbar).</summary>
public class TestRow : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";

    private string _symbol = "–";
    private Color _farbe = Colors.Gray;
    private string _sub = "";
    public string Symbol { get => _symbol; set { _symbol = value; OnP(); } }
    public Color Farbe { get => _farbe; set { _farbe = value; OnP(); } }
    public string Sub { get => _sub; set { _sub = value; OnP(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// MAUI-Selbsttest mit Tabs (Navigation / OSM / Tour). Jede Gruppe listet ihre
/// Einzeltests aus der GETEILTEN testcases.js; jeder Test ist einzeln per Run-Button
/// ausführbar (SPIN.runOne), „Alle ausführen" läuft die ganze Gruppe (SPIN.run).
/// </summary>
public partial class TestPage : ContentPage
{
    public ObservableCollection<TestRow> Rows { get; } = new();
    public ICommand RunOneCommand { get; }

    private TaskCompletionSource<bool>? _navWarten;
    private string _aktiveGruppe = "";
    private string _geladeneGruppe = "";   // welche Gruppe ist im WebView injiziert?
    private bool _busy;

    public TestPage()
    {
        InitializeComponent();
        RunOneCommand = new Command<TestRow>(async r => { if (r != null) await RunOne(r); });
        Liste.ItemsSource = Rows;
        TestWeb.Navigated += (_, __) => _navWarten?.TrySetResult(true);
        TabsHost.Children.Clear();
        foreach (var g in AppConfig.TestGruppen)
            TabsHost.Children.Add(TabButton(g.key, g.titel));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_aktiveGruppe == "")
            await GruppeWaehlen(AppConfig.TestGruppen[0].key);
    }

    private Button TabButton(string key, string titel)
    {
        var b = new Button { Text = titel, FontSize = 13, Padding = new Thickness(14, 6),
                             CornerRadius = 16, BackgroundColor = Color.FromArgb("#e2e8f0"),
                             TextColor = Color.FromArgb("#0f172a") };
        b.Clicked += async (_, __) => await GruppeWaehlen(key);
        b.AutomationId = "tab-" + key;
        return b;
    }

    private void TabsHervorheben()
    {
        int i = 0;
        foreach (var g in AppConfig.TestGruppen)
        {
            if (TabsHost.Children[i] is Button b)
            {
                bool aktiv = g.key == _aktiveGruppe;
                b.BackgroundColor = aktiv ? Color.FromArgb("#0d9488") : Color.FromArgb("#e2e8f0");
                b.TextColor = aktiv ? Colors.White : Color.FromArgb("#0f172a");
            }
            i++;
        }
    }

    // ---- Gruppe (Tab) wählen: Seite laden, Tests auflisten -------------------
    private async Task GruppeWaehlen(string key)
    {
        if (_busy) return;
        _aktiveGruppe = key;
        TabsHervorheben();
        Rows.Clear();
        var url = AppConfig.TestGruppen.First(g => g.key == key).url;
        _busy = true; AlleBtn.IsEnabled = false;
        try
        {
            Status("Lade Seite …");
            if (!await SeiteLaden(key, url)) return;

            // Einzeltests der Gruppe aus testcases.js holen (id + name)
            string b64 = Unwrap(await TestWeb.EvaluateJavaScriptAsync(
                "btoa(unescape(encodeURIComponent(JSON.stringify((window.SPIN.testcases['" + key +
                "']||[]).map(function(c){return {id:c.id,name:c.name};})))))"));
            var liste = JsonSerializer.Deserialize<List<CaseInfo>>(B64(b64)) ?? new();
            foreach (var c in liste)
                Rows.Add(new TestRow { Id = c.id, Group = key, Name = c.name, Symbol = "–", Farbe = Colors.Gray, Sub = "" });
            Status($"{liste.Count} Tests – einzeln oder alle ausführen.");
        }
        catch (Exception ex) { Status("Fehler: " + ex.Message); }
        finally { _busy = false; AlleBtn.IsEnabled = true; }
    }

    // ---- Einzelnen Test ausführen ------------------------------------------
    private async Task RunOne(TestRow row)
    {
        if (_busy) return;
        _busy = true; AlleBtn.IsEnabled = false;
        try
        {
            var url = AppConfig.TestGruppen.First(g => g.key == row.Group).url;
            if (_geladeneGruppe != row.Group && !await SeiteLaden(row.Group, url)) { return; }
            row.Symbol = "⏳"; row.Farbe = Color.FromArgb("#f59e0b"); row.Sub = "läuft …";

            await TestWeb.EvaluateJavaScriptAsync(
                "window.__ONE_DONE__=false;window.__ONE_RES__=null;" +
                "window.SPIN.runOne('" + row.Group + "','" + row.Id + "').then(function(r){window.__ONE_RES__=r;window.__ONE_DONE__=true;});'ok'");
            if (!await PollDone("window.__ONE_DONE__")) { Setze(row, false, "Zeitüberschreitung", 0); return; }
            string b64 = Unwrap(await TestWeb.EvaluateJavaScriptAsync(
                "btoa(unescape(encodeURIComponent(JSON.stringify(window.__ONE_RES__))))"));
            using var doc = JsonDocument.Parse(B64(b64));
            var r = doc.RootElement;
            Setze(row, r.GetProperty("ok").GetBoolean(),
                  r.GetProperty("error").GetString() ?? "",
                  r.TryGetProperty("ms", out var m) ? m.GetInt32() : 0);
        }
        catch (Exception ex) { Setze(row, false, ex.Message, 0); }
        finally { _busy = false; AlleBtn.IsEnabled = true; }
    }

    // ---- Ganze Gruppe ausführen --------------------------------------------
    private async void OnRunAll(object? sender, EventArgs e)
    {
        if (_busy || _aktiveGruppe == "") return;
        _busy = true; AlleBtn.IsEnabled = false;
        try
        {
            var url = AppConfig.TestGruppen.First(g => g.key == _aktiveGruppe).url;
            if (!await SeiteLaden(_aktiveGruppe, url)) return;
            foreach (var r in Rows) { r.Symbol = "⏳"; r.Farbe = Color.FromArgb("#f59e0b"); r.Sub = "läuft …"; }
            Status("Tests laufen …");
            await TestWeb.EvaluateJavaScriptAsync(
                "(function(){window.SPIN.run('" + _aktiveGruppe + "');return 'ok';})()");
            if (!await PollDone("window.__SPIN_DONE__")) { Status("⚠ Zeitüberschreitung."); return; }
            string b64 = Unwrap(await TestWeb.EvaluateJavaScriptAsync(
                "btoa(unescape(encodeURIComponent(JSON.stringify(window.__SPIN_RESULTS__))))"));
            using var doc = JsonDocument.Parse(B64(b64));
            var root = doc.RootElement;
            var nach = new Dictionary<string, JsonElement>();
            foreach (var c in root.GetProperty("cases").EnumerateArray())
                nach[c.GetProperty("id").GetString() ?? ""] = c;
            foreach (var row in Rows)
                if (nach.TryGetValue(row.Id, out var c))
                    Setze(row, c.GetProperty("ok").GetBoolean(), c.GetProperty("error").GetString() ?? "",
                          c.TryGetProperty("ms", out var m) ? m.GetInt32() : 0);
            int passed = root.GetProperty("passed").GetInt32(), total = root.GetProperty("total").GetInt32();
            Status($"{passed}/{total} bestanden" + (passed < total ? $", {total - passed} fehlgeschlagen" : ""));
        }
        catch (Exception ex) { Status("Fehler: " + ex.Message); }
        finally { _busy = false; AlleBtn.IsEnabled = true; }
    }

    // ---- Helfer -------------------------------------------------------------
    private record CaseInfo(string id, string name);

    private void Setze(TestRow row, bool ok, string err, int ms)
    {
        row.Symbol = ok ? "✓" : "✗";
        row.Farbe = ok ? Color.FromArgb("#16a34a") : Color.FromArgb("#e2231a");
        row.Sub = ok ? $"{ms} ms" : err;
    }

    /// <summary>Seite der Gruppe in den WebView laden + Test-Skripte injizieren.</summary>
    private async Task<bool> SeiteLaden(string key, string url)
    {
        if (AppConfig.UseDevLogin && _geladeneGruppe == "")
            await NavigateAndWait(AppConfig.DevLoginUrl);
        await NavigateAndWait(url);
        await Task.Delay(3800);
        string pfad = Unwrap(await TestWeb.EvaluateJavaScriptAsync("location.pathname"));
        if (pfad.Contains("login", StringComparison.OrdinalIgnoreCase))
        { Status("⚠ Nicht angemeldet – bitte erst im Navigations-Tab einloggen."); _geladeneGruppe = ""; return false; }
        await Inject("tests/runner.js");
        await Inject("tests/testcases.js");
        string ok = Unwrap(await TestWeb.EvaluateJavaScriptAsync(
            "(typeof window.SPIN!=='undefined'&&!!SPIN.runOne&&!!SPIN.testcases)?'1':'0'"));
        if (ok != "1") { Status("⚠ Test-Skripte nicht injiziert."); _geladeneGruppe = ""; return false; }
        _geladeneGruppe = key;
        return true;
    }

    private async Task<bool> PollDone(string flag)
    {
        for (int i = 0; i < 140; i++)
        {
            await Task.Delay(300);
            if (Unwrap(await TestWeb.EvaluateJavaScriptAsync(flag + "===true?'1':'0'")) == "1") return true;
        }
        return false;
    }

    private async Task NavigateAndWait(string url)
    {
        _navWarten = new TaskCompletionSource<bool>();
        TestWeb.Source = url;
        await Task.WhenAny(_navWarten.Task, Task.Delay(20000));
    }

    private async Task Inject(string logicalName)
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync(logicalName);
        using var reader = new StreamReader(stream);
        string text = await reader.ReadToEndAsync();
        string literal = JsonSerializer.Serialize(text);
        await TestWeb.EvaluateJavaScriptAsync(
            $"(function(){{var s=document.createElement('script');s.textContent={literal};document.documentElement.appendChild(s);}})();");
    }

    private void Status(string s) => StatusLabel.Text = s;

    private static string B64(string b64) =>
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));

    /// <summary>EvaluateJavaScriptAsync liefert je Plattform teils einen JSON-String
    /// (in Anführungszeichen). Hier einmalig auspacken.</summary>
    private static string Unwrap(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        { try { return JsonSerializer.Deserialize<string>(s) ?? s; } catch { } }
        return s;
    }
}
