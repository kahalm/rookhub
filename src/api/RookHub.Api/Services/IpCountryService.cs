using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace RookHub.Api.Services;

/// <summary>
/// IP-Bereiche → Land (ISO-3166-Kürzel), sortiert und per Binärsuche abgefragt. Quelle ist die freie
/// Liste „IP to Country Lite" von DB-IP (CSV: <c>start,ende,land</c>, IPv4 und IPv6 gemischt; CC BY 4.0 —
/// die Seite, die sie benutzt, nennt „IP Geolocation by DB-IP").
/// </summary>
public sealed class IpCountryTable
{
    private readonly uint[] _v4Start;
    private readonly uint[] _v4End;
    private readonly string[] _v4Country;
    private readonly UInt128[] _v6Start;
    private readonly UInt128[] _v6End;
    private readonly string[] _v6Country;

    private IpCountryTable(List<(uint S, uint E, string C)> v4, List<(UInt128 S, UInt128 E, string C)> v6)
    {
        v4.Sort((a, b) => a.S.CompareTo(b.S));
        v6.Sort((a, b) => a.S.CompareTo(b.S));
        _v4Start = v4.Select(x => x.S).ToArray();
        _v4End = v4.Select(x => x.E).ToArray();
        _v4Country = v4.Select(x => x.C).ToArray();
        _v6Start = v6.Select(x => x.S).ToArray();
        _v6End = v6.Select(x => x.E).ToArray();
        _v6Country = v6.Select(x => x.C).ToArray();
    }

    public int Count => _v4Start.Length + _v6Start.Length;

    /// <summary>Liest die CSV. Unlesbare Zeilen fallen weg, statt den ganzen Bestand zu verwerfen.</summary>
    public static IpCountryTable Parse(TextReader reader)
    {
        var v4 = new List<(uint, uint, string)>();
        var v6 = new List<(UInt128, UInt128, string)>();
        var countries = new Dictionary<string, string>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            if (!IPAddress.TryParse(parts[0], out var start) || !IPAddress.TryParse(parts[1], out var end)) continue;
            var code = parts[2].Trim().ToUpperInvariant();
            if (code.Length != 2) continue;
            // Zwei Buchstaben je Zeile, ~700 000 Zeilen: dieselbe Instanz je Land statt 700 000 Strings.
            if (!countries.TryGetValue(code, out var shared)) countries[code] = shared = code;

            if (start.AddressFamily == AddressFamily.InterNetwork && end.AddressFamily == AddressFamily.InterNetwork)
                v4.Add((ToUInt32(start), ToUInt32(end), shared));
            else if (start.AddressFamily == AddressFamily.InterNetworkV6 && end.AddressFamily == AddressFamily.InterNetworkV6)
                v6.Add((ToUInt128(start), ToUInt128(end), shared));
        }
        return new IpCountryTable(v4, v6);
    }

    /// <summary>Land der Adresse oder <c>null</c> (unbekannt, reserviert = <c>ZZ</c>).</summary>
    public string? Lookup(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        string? code = ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => Find(_v4Start, _v4End, _v4Country, ToUInt32(ip)),
            AddressFamily.InterNetworkV6 => Find(_v6Start, _v6End, _v6Country, ToUInt128(ip)),
            _ => null,
        };
        return code is null or "ZZ" ? null : code;
    }

    private static string? Find<T>(T[] starts, T[] ends, string[] countries, T value) where T : IComparable<T>
    {
        // Letzter Bereich, der bei oder vor der Adresse beginnt — liegt sie vor seinem Ende, ist er es.
        int lo = 0, hi = starts.Length - 1, hit = -1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (starts[mid].CompareTo(value) <= 0) { hit = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return hit >= 0 && ends[hit].CompareTo(value) >= 0 ? countries[hit] : null;
    }

    private static uint ToUInt32(IPAddress ip) => BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes());
    private static UInt128 ToUInt128(IPAddress ip) => BinaryPrimitives.ReadUInt128BigEndian(ip.GetAddressBytes());
}

/// <summary>
/// Land einer Besucher-IP — für die Startsprache von KidHub, wenn die Browsersprache keine der
/// Kindersprachen ist. Alles bleibt auf dem eigenen Server: die Länderliste wird hier heruntergeladen
/// (einmal im Monat, <c>IpCountry:SourceUrlTemplate</c>) und lokal abgefragt; keine Besucher-IP geht an einen
/// fremden Dienst, und keine wird gespeichert.
///
/// <para>Geladen wird erst beim ersten Bedarf, nicht beim Start — die Integrationstests starten die API,
/// und jeder Lauf zöge sonst 4,5 MB. Die Datei liegt im Temp-Ordner des Containers
/// (<c>IpCountry:CachePath</c>) und gilt 35 Tage. Scheitert der Download, gibt es eine Stunde lang keine
/// Auskunft (<c>null</c> → KidHub nimmt Deutsch), dann den nächsten Versuch. <c>IpCountry:Enabled=false</c>
/// schaltet alles ab.</para>
/// </summary>
public class IpCountryService
{
    internal const string DefaultSourceUrlTemplate = "https://download.db-ip.com/free/dbip-country-lite-{0:yyyy-MM}.csv.gz";
    internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(35);
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<IpCountryService> _logger;
    private readonly bool _enabled;
    private readonly string _sourceUrlTemplate;
    private readonly string _cachePath;
    private readonly Func<DateTime> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IpCountryTable? _table;
    private DateTime _loadedAt;
    private DateTime? _failedAt;

    public IpCountryService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<IpCountryService> logger)
        : this(httpFactory, config, logger, () => DateTime.UtcNow) { }

    internal IpCountryService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<IpCountryService> logger,
        Func<DateTime> utcNow)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _utcNow = utcNow;
        _enabled = config.GetValue("IpCountry:Enabled", true);
        _sourceUrlTemplate = config["IpCountry:SourceUrlTemplate"] ?? DefaultSourceUrlTemplate;
        _cachePath = config["IpCountry:CachePath"] ?? Path.Combine(Path.GetTempPath(), "dbip-country-lite.csv.gz");
    }

    /// <summary>ISO-Land der Adresse, oder <c>null</c> (private/lokale Adresse, unbekannt, Liste nicht da).</summary>
    public async Task<string?> CountryOfAsync(IPAddress? ip, CancellationToken ct = default)
    {
        if (!_enabled || ip is null || !IsPublic(ip)) return null;
        var table = await EnsureTableAsync(ct);
        return table?.Lookup(ip);
    }

    /// <summary>Nur öffentliche Adressen haben ein Land — LAN, Loopback, CGNAT und Link-Local nicht.</summary>
    internal static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10 || b[0] == 0
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6None));
        return false;
    }

    private async Task<IpCountryTable?> EnsureTableAsync(CancellationToken ct)
    {
        var now = _utcNow();
        if (_table is not null && now - _loadedAt < MaxAge) return _table;
        if (_failedAt is DateTime failed && now - failed < RetryAfterFailure) return _table;

        await _gate.WaitAsync(ct);
        try
        {
            if (_table is not null && now - _loadedAt < MaxAge) return _table;
            if (_failedAt is DateTime again && now - again < RetryAfterFailure) return _table;

            var fresh = File.Exists(_cachePath) && now - File.GetLastWriteTimeUtc(_cachePath) < MaxAge;
            if (!fresh && !await DownloadAsync(now, ct))
            {
                _failedAt = now;
                // Eine alte Datei ist besser als keine — Länder wechseln ihre Adressen selten.
                if (_table is null && File.Exists(_cachePath)) _table = Load();
                return _table;
            }

            _table = Load();
            _loadedAt = now;
            _failedAt = null;
            _logger.LogInformation("IP-Länderliste geladen: {Count} Bereiche.", _table.Count);
            return _table;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "IP-Länderliste nicht verfügbar.");
            _failedAt = now;
            return _table;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Diesen Monat, sonst den vorigen (am Monatsersten steht die neue Datei eventuell noch nicht da).</summary>
    private async Task<bool> DownloadAsync(DateTime now, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(nameof(IpCountryService));
        http.Timeout = TimeSpan.FromSeconds(60);
        foreach (var month in new[] { now, now.AddMonths(-1) })
        {
            var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, _sourceUrlTemplate, month);
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;
                var temp = _cachePath + ".part";
                await using (var file = File.Create(temp))
                    await response.Content.CopyToAsync(file, ct);
                File.Move(temp, _cachePath, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "IP-Länderliste: Download von {Url} fehlgeschlagen.", url);
            }
        }
        return false;
    }

    private IpCountryTable Load()
    {
        using var file = File.OpenRead(_cachePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return IpCountryTable.Parse(reader);
    }
}
