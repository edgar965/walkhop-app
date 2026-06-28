using System.Text.Json;

namespace WalkHop;

/// <summary>Ein gespeichertes Offline-Paket (Region oder Tour) – Metadaten für die Verwaltung
/// („Meine Offline-Karten": auflisten, Größe zeigen, löschen).</summary>
public record OfflinePaket(string Id, string Name, string Art, int Kacheln, int Fotos, long Bytes, long Zeit)
{
    public bool IstTour => Art == "tour";
}

/// <summary>Persistenter Index der geladenen Offline-Pakete (JSON-Datei im Karten-Cache).
/// Die eigentlichen Kacheln liegen im geteilten Kachel-Cache, die Fotos unter <see cref="OfflineFotos"/>;
/// dieser Index dient der Anzeige/Verwaltung (Name, geschätzte Größe, Zeitpunkt).</summary>
public static class OfflinePakete
{
    private static string Datei => Path.Combine(OfflineKarte.CacheDir, "pakete.json");

    public static List<OfflinePaket> Laden()
    {
        try
        {
            if (!File.Exists(Datei)) return new();
            var roh = File.ReadAllText(Datei);
            return JsonSerializer.Deserialize<List<OfflinePaket>>(roh) ?? new();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Meldung.Fehler("Offline-Pakete laden", ex); return new(); }
    }

    private static void Speichern(List<OfflinePaket> liste)
    {
        try
        {
            Directory.CreateDirectory(OfflineKarte.CacheDir);
            File.WriteAllText(Datei, JsonSerializer.Serialize(liste));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Meldung.Fehler("Offline-Paket speichern", ex); }
    }

    /// <summary>Paket hinzufügen oder (gleiche Id) ersetzen.</summary>
    public static void Hinzufuegen(OfflinePaket paket)
    {
        var liste = Laden();
        liste.RemoveAll(p => p.Id == paket.Id);
        liste.Insert(0, paket);
        Speichern(liste);
    }

    /// <summary>Paket aus dem Index entfernen (löscht die zugehörigen Offline-Fotos mit, sofern
    /// dem Paket zugeordnet; die geteilten Kacheln bleiben im Cache).</summary>
    public static void Entfernen(string id)
    {
        var liste = Laden();
        liste.RemoveAll(p => p.Id == id);
        Speichern(liste);
    }

    public static bool Enthaelt(string id) => Laden().Any(p => p.Id == id);

    /// <summary>Den gesamten Index leeren (z. B. zusammen mit „alle Offline-Daten löschen").</summary>
    public static void AllesLeeren()
    {
        try { if (File.Exists(Datei)) File.Delete(Datei); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Meldung.Fehler("Offline-Pakete löschen", ex); }
    }
}
