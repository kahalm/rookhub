namespace RookHub.Api.Services;

/// <summary>
/// Umkreis-Rechnung. Bewusst in C# und nicht in SQL: <c>Math.Acos</c>/<c>Cos</c>/<c>Sin</c> werden
/// vom MySQL-Provider nicht verlaesslich uebersetzt, und die Unit-Tests laufen gegen EF InMemory -
/// dort faellt so etwas nie auf, in Produktion sofort. Die Datenbank liefert deshalb nur den
/// indexgestuetzten Bounding-Box-Vorfilter, die exakte Distanz entsteht danach im Speicher.
/// </summary>
public static class GeoDistance
{
    private const double EarthRadiusKm = 6371.0088;
    /// <summary>Kilometer pro Breitengrad - fuer die Laenge gilt derselbe Wert mal cos(Breite).</summary>
    private const double KmPerDegreeLatitude = 111.32;

    public static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>
    /// Bounding-Box um einen Punkt. Nahe den Polen wird der Laengengrad-Radius unendlich, deshalb
    /// der Deckel auf 180 Grad - sonst kaeme NaN in die SQL-Abfrage. Die Box ist absichtlich etwas
    /// zu gross (sie umschliesst den Kreis); die exakte Distanz sortiert die Ecken danach aus.
    /// </summary>
    public static (double MinLat, double MaxLat, double MinLon, double MaxLon) BoundingBox(
        double lat, double lon, int radiusKm)
    {
        var deltaLat = radiusKm / KmPerDegreeLatitude;
        var cos = Math.Cos(ToRadians(lat));
        var deltaLon = Math.Abs(cos) < 1e-9 ? 180.0 : Math.Min(180.0, radiusKm / (KmPerDegreeLatitude * cos));

        return (
            Math.Max(-90.0, lat - deltaLat),
            Math.Min(90.0, lat + deltaLat),
            Math.Max(-180.0, lon - deltaLon),
            Math.Min(180.0, lon + deltaLon));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
