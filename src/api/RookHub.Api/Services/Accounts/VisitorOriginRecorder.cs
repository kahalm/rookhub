using System.Net;
using System.Security.Cryptography;
using System.Text;
using RookHub.Api.Logging;

namespace RookHub.Api.Services.Accounts;

/// <summary>Was der Sammler seit dem letzten Wegschreiben für einen Besucher an einem Tag gezählt hat.</summary>
public sealed record VisitorDayDelta(DateOnly Day, string VisitorHash, string Surface, string Country, int Hits);

/// <summary>
/// Sammelt die Besucher-Herkunft IM SPEICHER (Singleton): je Anfrage ein Wörterbuch-Zugriff, kein Datenbankzugriff —
/// weggeschrieben wird gebündelt vom <see cref="VisitorOriginFlushWorker"/>. Gezählt wird je Tag (UTC), Besucher und
/// Oberfläche; die IP-Adresse dient nur zum Nachschlagen des Landes (lokale DB-IP-Liste,
/// <see cref="IpCountryService"/>) und wird nicht gespeichert. Beschreibung: <c>docs/api/besucher-herkunft.md</c>.
///
/// <para><b>Nicht gezählt</b> werden: Anfragen ohne Besucher-Kennung, System-/Maschinen-Anfragen
/// (<see cref="SystemCallClassifier"/>: Health, Heartbeats, Zähler-Polls, Engine-Provider), erkennbare Bots und
/// Anfragen ohne User-Agent (<see cref="DeviceClassifier"/>), private/interne Adressen
/// (<see cref="IpCountryService.IsPublic"/>), Google-Server (Linkvorschau/Vorabladen, <see cref="VisitorNetworkClassifier"/>),
/// alles außerhalb von <c>/api</c> und abgelehnte Anfragen (Status ≥ 400). Bekannte VPN-Netze zählen mit dem Land
/// <see cref="VisitorNetworkClassifier.VpnCountry"/> („VPN“) statt dem Land des Servers.</para>
///
/// <para><b>Deckel.</b> Die anonyme Kennung ist ein frei wählbares Feld der Anfrage. Darum legt eine Adresse je Tag
/// höchstens <see cref="MaxNewVisitorsPerIpPerDay"/> neue Besucher an (Zähler nur im Speicher, täglich geleert), und
/// je Tag entstehen insgesamt höchstens <see cref="MaxVisitorsPerDay"/>.</para>
/// </summary>
public sealed class VisitorOriginRecorder
{
    public const int MaxNewVisitorsPerIpPerDay = 50;
    public const int MaxVisitorsPerDay = 50_000;

    /// <summary>Die Oberflächen, wie sie in <c>VisitorDays.Surface</c> stehen (Reihenfolge = Anzeige).</summary>
    public static readonly IReadOnlyList<string> Surfaces = ["rookhub", "leaguehub", "kidhub", "clubhub", "turnier"];

    private readonly Func<IPAddress, string?> _countryOf;
    private readonly Func<IPAddress, VisitorNetwork> _networkOf;
    private readonly Func<DateTime> _utcNow;
    private readonly byte[] _key;
    private readonly bool _enabled;
    private readonly object _lock = new();

    private readonly Dictionary<(DateOnly Day, string Hash, string Surface), Counter> _pending = new();
    /// <summary>Heute schon gesehene Besucher (Deckel) und neue Besucher je Adresse — nur im Speicher.</summary>
    private readonly HashSet<(string Hash, string Surface)> _seenToday = new();
    private readonly Dictionary<IPAddress, int> _newPerIp = new();
    private DateOnly _seenDay;

    private sealed class Counter
    {
        public string Country = string.Empty;
        public int Hits;
    }

    public VisitorOriginRecorder(IpCountryService ipCountry, VisitorNetworkClassifier networks, IConfiguration config)
        : this(config, ipCountry.TryCountryOf, () => DateTime.UtcNow, networks.Classify) { }

    internal VisitorOriginRecorder(IConfiguration config, Func<IPAddress, string?> countryOf, Func<DateTime> utcNow,
        Func<IPAddress, VisitorNetwork>? networkOf = null)
    {
        _countryOf = countryOf;
        _networkOf = networkOf ?? (_ => VisitorNetwork.Normal);
        _utcNow = utcNow;
        _enabled = config.GetValue("VisitorStats:Enabled", true);
        // Eigenes Salz, sonst aus dem JWT-Schlüssel abgeleitet (der ist auf jedem Server gesetzt und geheim) — so braucht
        // die Zählung keine neue Pflicht-Konfiguration, und die Pseudonyme bleiben über Neustarts gleich.
        var salt = config["VisitorStats:Salt"];
        if (string.IsNullOrWhiteSpace(salt)) salt = "rookhub-visitor-origin|" + config["Jwt:Key"];
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(salt));
    }

    /// <summary><c>VisitorStats:Enabled</c> (Vorgabe an) — aus: nichts wird gezählt, der Schreibdienst endet.</summary>
    public bool Enabled => _enabled;

    /// <summary>Pseudonym einer Besucher-Kennung (<c>u:name</c> / <c>a:…</c>): HMAC-SHA256, 32 Hex-Zeichen.</summary>
    internal string Pseudonym(string visitorId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(visitorId)))[..32];

    /// <summary>Oberfläche aus dem Host der Anfrage (das Frontend-nginx jeder Oberfläche reicht ihn durch):
    /// <c>leaguehub-dev.…</c> → <c>leaguehub</c>; alles Unbekannte zählt als RookHub.</summary>
    public static string SurfaceOf(string? host)
    {
        var h = (host ?? string.Empty).Trim().ToLowerInvariant();
        if (h.StartsWith("leaguehub", StringComparison.Ordinal)) return "leaguehub";
        if (h.StartsWith("kidhub", StringComparison.Ordinal)) return "kidhub";
        if (h.StartsWith("clubhub", StringComparison.Ordinal)) return "clubhub";
        if (h.StartsWith("turnier", StringComparison.Ordinal) || h.StartsWith("tournament", StringComparison.Ordinal)) return "turnier";
        return "rookhub";
    }

    /// <summary>Eine beantwortete Anfrage zählen (aus der Middleware, nach dem Rest der Pipeline). Wirft nie.</summary>
    public void Record(HttpContext context, string? visitorId)
    {
        if (!_enabled) return;
        try
        {
            if (context.Response.StatusCode >= 400) return;
            Record(visitorId, context.Connection.RemoteIpAddress, context.Request.Path.Value,
                context.Request.Headers.UserAgent.ToString(), context.Request.Host.Host);
        }
        catch (Exception)
        {
            // Die Zählung darf nie eine Anfrage stören.
        }
    }

    /// <summary>Kern ohne HttpContext. <c>true</c>, wenn die Anfrage gezählt wurde.</summary>
    internal bool Record(string? visitorId, IPAddress? ip, string? path, string? userAgent, string? host)
    {
        if (!_enabled || string.IsNullOrEmpty(visitorId) || ip is null) return false;
        if (path is null || !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)) return false;
        if (SystemCallClassifier.IsSystemCall(path)) return false;
        if (DeviceClassifier.Classify(userAgent) is DeviceClassifier.Bot or DeviceClassifier.Unknown) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (!IpCountryService.IsPublic(ip)) return false;
        var network = _networkOf(ip);
        if (network == VisitorNetwork.Google) return false;

        var day = DateOnly.FromDateTime(_utcNow());
        var hash = Pseudonym(visitorId);
        var surface = SurfaceOf(host);
        lock (_lock)
        {
            if (day != _seenDay)
            {
                _seenDay = day;
                _seenToday.Clear();
                _newPerIp.Clear();
            }
            if (!_seenToday.Contains((hash, surface)))
            {
                var perIp = _newPerIp.GetValueOrDefault(ip);
                if (_seenToday.Count >= MaxVisitorsPerDay || perIp >= MaxNewVisitorsPerIpPerDay) return false;
                _seenToday.Add((hash, surface));
                _newPerIp[ip] = perIp + 1;
            }
            if (!_pending.TryGetValue((day, hash, surface), out var counter))
                _pending[(day, hash, surface)] = counter = new Counter();
            counter.Hits++;
            // VPN steht über dem Land (die Netzbetreiberliste kann nach dem ersten Aufruf des Tages geladen sein); sonst das
            // Land nur nachschlagen, solange es fehlt (Liste evtl. noch nicht geladen).
            if (network == VisitorNetwork.Vpn) counter.Country = VisitorNetworkClassifier.VpnCountry;
            else if (counter.Country.Length == 0) counter.Country = _countryOf(ip) ?? string.Empty;
        }
        return true;
    }

    /// <summary>Nimmt alles Gesammelte heraus (der Sammler ist danach leer).</summary>
    public IReadOnlyList<VisitorDayDelta> Drain()
    {
        lock (_lock)
        {
            var list = _pending.Select(p => new VisitorDayDelta(p.Key.Day, p.Key.Hash, p.Key.Surface, p.Value.Country, p.Value.Hits)).ToList();
            _pending.Clear();
            return list;
        }
    }

    /// <summary>Legt nicht weggeschriebene Zähler zurück (Datenbank nicht erreichbar) — der nächste Takt versucht es wieder.</summary>
    public void Restore(IEnumerable<VisitorDayDelta> deltas)
    {
        lock (_lock)
        {
            foreach (var d in deltas)
            {
                if (!_pending.TryGetValue((d.Day, d.VisitorHash, d.Surface), out var counter))
                {
                    if (_pending.Count >= MaxVisitorsPerDay * 2) continue;   // Speicherschutz bei langem DB-Ausfall
                    _pending[(d.Day, d.VisitorHash, d.Surface)] = counter = new Counter();
                }
                counter.Hits += d.Hits;
                if (counter.Country.Length == 0) counter.Country = d.Country;
            }
        }
    }
}
