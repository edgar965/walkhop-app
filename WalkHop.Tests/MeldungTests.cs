using System;
using Xunit;

namespace WalkHop.Tests;

/// <summary>Unit-Tests für die zentrale Melde-/Logging-Verteilung (<see cref="Meldung"/>). Prüft, dass
/// gemeldete Fehler an BEIDE Senken gehen (Protokoll-Datei + Dialog), Notizen nur ins Protokoll, und
/// dass Meldung robust ist (nie werfend, auch wenn eine Senke wirft oder keine registriert ist).
/// Die Delegaten filtern auf den test-eigenen Kontext → keine Störung durch parallel laufende Tests,
/// die (mit null-Delegaten) ebenfalls Meldung.Fehler aufrufen.</summary>
public class MeldungTests
{
    [Fact]
    public void Fehler_geht_an_Protokoll_UND_Anzeiger()
    {
        string? proto = null, anzeige = null;
        Exception? protoEx = null;
        Meldung.Protokollierer = (k, ex) => { if (k == "Testfehler-A") { proto = k; protoEx = ex; } };
        Meldung.Anzeiger = (k, ex) => { if (k == "Testfehler-A") anzeige = k; };
        try
        {
            var boom = new InvalidOperationException("kaputt");
            Meldung.Fehler("Testfehler-A", boom);
            Assert.Equal("Testfehler-A", proto);      // ins Protokoll geschrieben
            Assert.Same(boom, protoEx);               // mit der Exception
            Assert.Equal("Testfehler-A", anzeige);    // und als Dialog angezeigt
        }
        finally { Meldung.Protokollierer = null; Meldung.Anzeiger = null; }
    }

    [Fact]
    public void Notiz_geht_nur_an_Notierer_nicht_an_Anzeiger()
    {
        string? kat = null, txt = null; bool anzeigeGerufen = false;
        Meldung.Notierer = (k, t) => { if (k == "NAV-TEST") { kat = k; txt = t; } };
        Meldung.Anzeiger = (_, _) => anzeigeGerufen = true;
        try
        {
            Meldung.Notiz("NAV-TEST", "Navigation gestartet");
            Assert.Equal("NAV-TEST", kat);
            Assert.Equal("Navigation gestartet", txt);
            Assert.False(anzeigeGerufen, "Notiz darf keinen Dialog auslösen");
        }
        finally { Meldung.Notierer = null; Meldung.Anzeiger = null; }
    }

    [Fact]
    public void Ohne_registrierte_Senken_wirft_nichts()
    {
        Meldung.Protokollierer = null; Meldung.Anzeiger = null; Meldung.Notierer = null;
        // Darf ohne registrierte Delegaten NICHT werfen (Tests laufen genau in diesem Zustand).
        Meldung.Fehler("kontext ohne senke", new Exception("x"));
        Meldung.Fehler("nur text");
        Meldung.Notiz("KAT", "text");
    }

    [Fact]
    public void Werfende_Senke_reisst_Meldung_nicht_mit()
    {
        Meldung.Protokollierer = (k, _) => { if (k == "wirf-B") throw new Exception("Protokoll kaputt"); };
        Meldung.Anzeiger = (k, _) => { if (k == "wirf-B") throw new Exception("Anzeige kaputt"); };
        Meldung.Notierer = (k, _) => { if (k == "wirf-B") throw new Exception("Notiz kaputt"); };
        try
        {
            // Beide/alle Senken werfen – Meldung muss das schlucken (nie werfend).
            Meldung.Fehler("wirf-B", new Exception("ursprung"));
            Meldung.Notiz("wirf-B", "egal");
        }
        finally { Meldung.Protokollierer = null; Meldung.Anzeiger = null; Meldung.Notierer = null; }
    }

    [Fact]
    public void IgnorierteAnzahl_ohne_Delegat_ist_null()
    {
        Meldung.AnzahlIgnoriert = null;
        Assert.Equal(0, Meldung.IgnorierteAnzahl);
        Meldung.AnzahlIgnoriert = () => 7;
        try { Assert.Equal(7, Meldung.IgnorierteAnzahl); }
        finally { Meldung.AnzahlIgnoriert = null; }
    }
}
