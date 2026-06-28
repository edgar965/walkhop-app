using Microsoft.Maui.Storage;

namespace SpinNaviApp;

/// <summary>Persistente App-Einstellungen (überleben Neustart, via Preferences).</summary>
public static class Einst
{
    /// <summary>Sprachansagen an/aus (Default aus – wie in der Web-Navi).</summary>
    public static bool Ton
    {
        get => Preferences.Get("ton", false);
        set => Preferences.Set("ton", value);
    }

    /// <summary>Anzahl bereits offline geladener Bereiche (lokaler Zähler fürs Offline-Kontingent:
    /// P8 unbegrenzt, P5 3 inklusive, sonst nur gekaufte Karten).</summary>
    public static int OfflineAnzahl
    {
        get => Preferences.Get("offline_anzahl", 0);
        set => Preferences.Set("offline_anzahl", value);
    }

    /// <summary>Karte folgt beim Start automatisch dem Standort.</summary>
    public static bool Folgen
    {
        get => Preferences.Get("folgen", true);
        set => Preferences.Set("folgen", value);
    }

    // Standard-/Default-Punkt für die Entfernungsanzeige im Kontextmenü, wenn kein
    // aktueller GPS-Standort verfügbar ist. Default: Berlin, Brandenburger Tor.
    public const double BrandenburgerTorLat = 52.5163;
    public const double BrandenburgerTorLng = 13.3777;
    public static double StandardLat
    {
        get => Preferences.Get("std_lat", BrandenburgerTorLat);
        set => Preferences.Set("std_lat", value);
    }
    public static double StandardLng
    {
        get => Preferences.Get("std_lng", BrandenburgerTorLng);
        set => Preferences.Set("std_lng", value);
    }
    /// <summary>Anzeigename des Standard-Punkts (z. B. „Berlin Mitte" / „eigener Punkt").</summary>
    public static string StandardName
    {
        get => Preferences.Get("std_name", "Berlin Mitte");
        set => Preferences.Set("std_name", value);
    }

    /// <summary>Kartenmodus (Wandern=Default wie in der Web-Navi).</summary>
    public static Kartenmodus Karte
    {
        get => (Kartenmodus)Preferences.Get("karte", (int)Kartenmodus.Wandern);
        set => Preferences.Set("karte", (int)value);
    }

    /// <summary>Wanderwege-/Radwege-Overlay einblenden.</summary>
    public static bool Wanderwege
    {
        get => Preferences.Get("wanderwege", false);
        set => Preferences.Set("wanderwege", value);
    }

    /// <summary>Fortbewegung: "pedestrian" | "bicycle" | "auto".</summary>
    public static string Profil
    {
        get => Preferences.Get("profil", "pedestrian");
        set => Preferences.Set("profil", value);
    }

    /// <summary>Fuß-Wegtyp: "fest" | "neutral" | "natur".</summary>
    public static string Wegtyp
    {
        get => Preferences.Get("wegtyp", "neutral");
        set => Preferences.Set("wegtyp", value);
    }

    /// <summary>Auto: Autobahn vermeiden (Kompromiss).</summary>
    public static bool VermeideAutobahn
    {
        get => Preferences.Get("v_autobahn", true);
        set => Preferences.Set("v_autobahn", value);
    }

    /// <summary>Auto: unbefestigte Wege vermeiden.</summary>
    public static bool VermeideUnbefestigt
    {
        get => Preferences.Get("v_unbefestigt", true);
        set => Preferences.Set("v_unbefestigt", value);
    }

    /// <summary>Rad: schlechte Oberflächen vermeiden.</summary>
    public static bool VermeideSchlechteOberflaeche
    {
        get => Preferences.Get("v_oberflaeche", true);
        set => Preferences.Set("v_oberflaeche", value);
    }

    /// <summary>App-, Ansage- und Routensprache: "de" | "en". Steuert die gesamte App-Oberfläche
    /// (über <see cref="Lokalisierung"/>) UND den Routing-/TTS-Locale. Default = Englisch; als Admin
    /// (e@edgarm.de bzw. Auth.IstAdmin) Deutsch – solange der Nutzer noch nicht selbst gewählt hat
    /// (<see cref="SpracheGesetzt"/>).</summary>
    public static string Sprache
    {
        get => Preferences.Get("sprache", StandardSprache);
        set => Preferences.Set("sprache", value);   // legt den Schlüssel an → SpracheGesetzt = true
    }

    /// <summary>Hat der Nutzer die App-Sprache schon einmal explizit gewählt? Solange nicht, gilt die
    /// Admin-Default-Regel (<see cref="StandardSprache"/>).</summary>
    public static bool SpracheGesetzt => Preferences.ContainsKey("sprache");

    /// <summary>Default-Sprache ohne explizite Wahl: Admin → Deutsch, sonst Englisch.</summary>
    public static string StandardSprache => Auth.IstAdmin ? "de" : "en";

    /// <summary>BCP-47-Code für Valhalla/TTS aus <see cref="Sprache"/>.</summary>
    public static string Locale => Sprache == "en" ? "en-US" : "de-DE";

    // ---- Allgemein (Tab „Allgemein") -------------------------------------
    /// <summary>Maßeinheiten: "metrisch" | "imperial".</summary>
    public static string Einheiten
    {
        get => Preferences.Get("einheiten", "metrisch");
        set => Preferences.Set("einheiten", value);
    }

    /// <summary>Bildschirm bei App-Nutzung nicht sperren (wach halten).</summary>
    public static bool BildschirmWach
    {
        get => Preferences.Get("bildschirm_wach", true);
        set => Preferences.Set("bildschirm_wach", value);
    }

    /// <summary>Kompakter Suchmodus: Suchfeld durch Such-Symbol ersetzen.
    /// (Gespeichert, aber noch nicht im UI wirksam – geplant.)</summary>
    public static bool KompakteSuche
    {
        get => Preferences.Get("kompakte_suche", false);
        set => Preferences.Set("kompakte_suche", value);
    }

    /// <summary>Track-Aufnahme automatisch beim App-Start beginnen (Default an).</summary>
    public static bool AutoAufnahme
    {
        get => Preferences.Get("auto_aufnahme", true);
        set => Preferences.Set("auto_aufnahme", value);
    }

    /// <summary>Foto-Ebene der Übersichtskarte direkt beim App-Start aktivieren. Standard: AUS
    /// (sonst kostet das Laden/Zeichnen vieler Foto-Marker beim Start unnötig Leistung).</summary>
    public static bool FotosBeimStart
    {
        get => Preferences.Get("fotos_beim_start", false);
        set => Preferences.Set("fotos_beim_start", value);
    }

    /// <summary>Fotos nur über WLAN hochladen (nicht über Mobilfunk).
    /// (Gespeichert, aber Foto-Upload-Drosselung noch nicht angeschlossen – geplant.)</summary>
    public static bool FotosNurWlan
    {
        get => Preferences.Get("fotos_wlan", true);
        set => Preferences.Set("fotos_wlan", value);
    }

    // ---- Navigation (Tab „Navigation") -----------------------------------
    /// <summary>Farbmodus: "auto" | "tag" | "nacht".
    /// (Gespeichert; eigener Tag/Nacht-Stil noch nicht angeschlossen – der Kartenstil wird
    /// derzeit über den Kartenmodus im Karten-Sheet gewählt, inkl. "Dunkel". Geplant.)</summary>
    public static string Farbmodus
    {
        get => Preferences.Get("farbmodus", "auto");
        set => Preferences.Set("farbmodus", value);
    }

    /// <summary>Lautstärke der Sprachansagen (0..1).</summary>
    public static double Ansagelautstaerke
    {
        get => Preferences.Get("ansage_vol", 1.0);
        set => Preferences.Set("ansage_vol", value);
    }

    /// <summary>Benachrichtigungstöne abspielen (Tempo/Reroute).
    /// (Gespeichert, aber es werden derzeit keine Töne erzeugt – geplant.)</summary>
    public static bool Benachrichtigungstoene
    {
        get => Preferences.Get("benach_toene", true);
        set => Preferences.Set("benach_toene", value);
    }

    /// <summary>Kartenansicht: "3d" (geneigt) | "2d".
    /// (Gespeichert; geneigte 3D-Ansicht bietet die 2D-Kartenengine Mapsui nicht – geplant.)</summary>
    public static string Kartenansicht
    {
        get => Preferences.Get("kartenansicht", "2d");
        set => Preferences.Set("kartenansicht", value);
    }

    // ---- Karte (Tab „Karte") ---------------------------------------------
    /// <summary>Schattiertes Relief (Hillshade, nur online).
    /// (Gespeichert; eigene Hillshade-Ebene noch nicht angeschlossen – die Wander-Karte
    /// OpenTopoMap zeigt bereits Relief. Geplant.)</summary>
    public static bool SchattiertesRelief
    {
        get => Preferences.Get("relief", false);
        set => Preferences.Set("relief", value);
    }

    /// <summary>Hangneigungs-Overlay (&gt;30°/38°/45°, nur online).
    /// (Gespeichert, aber Overlay-Ebene noch nicht angeschlossen – geplant.)</summary>
    public static bool Hangneigung
    {
        get => Preferences.Get("hangneigung", false);
        set => Preferences.Set("hangneigung", value);
    }

    /// <summary>Karte darf manuell gedreht werden (Zwei-Finger-Geste).</summary>
    public static bool ManuelleDrehung
    {
        get => Preferences.Get("manuelle_drehung", true);
        set => Preferences.Set("manuelle_drehung", value);
    }
}
