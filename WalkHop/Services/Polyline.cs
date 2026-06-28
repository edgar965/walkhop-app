namespace WalkHop;

/// <summary>Dekodiert Valhalla-/Google-Polyline-Strings. Valhalla nutzt Präzision 6 (1e6).</summary>
public static class Polyline
{
    public static List<(double lat, double lon)> Decode(string? encoded, double precision = 1e6)
    {
        var pts = new List<(double, double)>();
        if (string.IsNullOrEmpty(encoded)) return pts;
        int index = 0, lat = 0, lon = 0, len = encoded.Length;

        // Liest ein zickzack-kodiertes Delta. Gibt false zurück, wenn der String
        // mitten im Wert endet (abgeschnitten) – dann wird das Paar verworfen,
        // statt einen Phantom-Punkt zu erzeugen.
        bool LeseDelta(ref int wert)
        {
            int b, shift = 0, result = 0;
            do
            {
                if (index >= len) return false;
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            wert += (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
            return true;
        }

        while (index < len)
        {
            if (!LeseDelta(ref lat)) break;
            if (!LeseDelta(ref lon)) break;
            pts.Add((lat / precision, lon / precision));
        }
        return pts;
    }
}
