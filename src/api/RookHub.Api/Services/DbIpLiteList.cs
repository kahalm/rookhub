using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace RookHub.Api.Services;

/// <summary>
/// IP-Bereiche → Wert, sortiert und per Binärsuche abgefragt (IPv4 und IPv6 getrennt). Grundlage der DB-IP-Listen
/// (<see cref="IpCountryTable"/>, <see cref="IpAsnTable"/>): CSV <c>start,ende,wert[,…]</c>. Die Bereiche einer Liste
/// dürfen sich nicht überlappen — gesucht wird der letzte Bereich, der bei oder vor der Adresse beginnt.
/// </summary>
public sealed class IpRangeTable<T>
{
    /// <summary>Liest den Wert aus dem dritten CSV-Feld; <c>false</c> = Zeile verwerfen.</summary>
    public delegate bool ValueParser(string field, out T value);

    private readonly uint[] _v4Start;
    private readonly uint[] _v4End;
    private readonly T[] _v4Value;
    private readonly UInt128[] _v6Start;
    private readonly UInt128[] _v6End;
    private readonly T[] _v6Value;

    public IpRangeTable(List<(uint Start, uint End, T Value)> v4, List<(UInt128 Start, UInt128 End, T Value)> v6)
    {
        v4.Sort((a, b) => a.Start.CompareTo(b.Start));
        v6.Sort((a, b) => a.Start.CompareTo(b.Start));
        _v4Start = v4.Select(x => x.Start).ToArray();
        _v4End = v4.Select(x => x.End).ToArray();
        _v4Value = v4.Select(x => x.Value).ToArray();
        _v6Start = v6.Select(x => x.Start).ToArray();
        _v6End = v6.Select(x => x.End).ToArray();
        _v6Value = v6.Select(x => x.Value).ToArray();
    }

    public int Count => _v4Start.Length + _v6Start.Length;

    /// <summary>Liest die CSV. Unlesbare Zeilen fallen weg, statt den ganzen Bestand zu verwerfen.</summary>
    public static IpRangeTable<T> Parse(TextReader reader, ValueParser value)
    {
        var v4 = new List<(uint, uint, T)>();
        var v6 = new List<(UInt128, UInt128, T)>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            // Nur die ersten drei Felder zählen; was danach kommt (z. B. ein Firmenname mit Kommas), bleibt liegen.
            var parts = line.Split(',', 4);
            if (parts.Length < 3) continue;
            if (!IPAddress.TryParse(parts[0], out var start) || !IPAddress.TryParse(parts[1], out var end)) continue;
            if (!value(parts[2], out var v)) continue;

            if (start.AddressFamily == AddressFamily.InterNetwork && end.AddressFamily == AddressFamily.InterNetwork)
                v4.Add((ToUInt32(start), ToUInt32(end), v));
            else if (start.AddressFamily == AddressFamily.InterNetworkV6 && end.AddressFamily == AddressFamily.InterNetworkV6)
                v6.Add((ToUInt128(start), ToUInt128(end), v));
        }
        return new IpRangeTable<T>(v4, v6);
    }

    /// <summary>Wert des Bereichs, in dem die Adresse liegt (IPv4 im IPv6-Gewand wird als IPv4 gesucht).</summary>
    public bool TryLookup(IPAddress ip, out T value)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var hit = ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => Find(_v4Start, _v4End, ToUInt32(ip)),
            AddressFamily.InterNetworkV6 => Find(_v6Start, _v6End, ToUInt128(ip)),
            _ => -1,
        };
        value = hit < 0 ? default! : ip.AddressFamily == AddressFamily.InterNetwork ? _v4Value[hit] : _v6Value[hit];
        return hit >= 0;
    }

    private static int Find<TKey>(TKey[] starts, TKey[] ends, TKey key) where TKey : IComparable<TKey>
    {
        // Letzter Bereich, der bei oder vor der Adresse beginnt — liegt sie vor seinem Ende, ist er es.
        int lo = 0, hi = starts.Length - 1, hit = -1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (starts[mid].CompareTo(key) <= 0) { hit = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return hit >= 0 && ends[hit].CompareTo(key) >= 0 ? hit : -1;
    }

    public static uint ToUInt32(IPAddress ip) => BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes());
    public static UInt128 ToUInt128(IPAddress ip) => BinaryPrimitives.ReadUInt128BigEndian(ip.GetAddressBytes());
}

/// <summary>Woher eine <see cref="DbIpLiteList{TTable}"/> lädt: Konfigurationsabschnitt (<c>&lt;Section&gt;:Enabled</c>,
/// <c>:SourceUrlTemplate</c>, <c>:CachePath</c>, <c>:DownloadTimeoutSeconds</c>, <c>:CallerWaitSeconds</c>), Name des
/// HttpClients, Vorgabe-Adresse (<c>{0:yyyy-MM}</c> = Monat), Dateiname im Temp-Ordner, Bezeichnung fürs Log.</summary>
public sealed record DbIpLiteListOptions(string Section, string ClientName, string DefaultSourceUrlTemplate,
    string DefaultCacheFile, string Label);

/// <summary>
/// Eine der freien Monatslisten von DB-IP („IP to Country Lite“, „IP to ASN Lite“; CC BY 4.0), heruntergeladen und
/// lokal abgefragt — keine Besucher-IP geht an einen fremden Dienst.
///
/// <para>Geladen wird erst beim ersten Bedarf, nicht beim Start — die Integrationstests starten die API,
/// und jeder Lauf zöge sonst mehrere MB. Die Datei liegt im Temp-Ordner des Containers
/// (<c>&lt;Section&gt;:CachePath</c>) und gilt 35 Tage, danach wird die Liste des neuen Monats geholt (Aktualisierung
/// also von selbst, ohne Neustart). Scheitert der Download, gibt es eine Stunde lang keine Auskunft (<c>null</c>), dann
/// den nächsten Versuch. <c>&lt;Section&gt;:Enabled=false</c> schaltet alles ab.</para>
/// </summary>
public abstract class DbIpLiteList<TTable> where TTable : class
{
    internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(35);
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger _logger;
    private readonly DbIpLiteListOptions _options;
    private readonly string _sourceUrlTemplate;
    private readonly string _cachePath;
    private readonly Func<DateTime> _utcNow;
    private readonly TimeSpan _downloadTimeout;
    private readonly TimeSpan _callerWait;
    private readonly object _refreshLock = new();

    private volatile TTable? _table;
    private DateTime _loadedAt;
    private DateTime? _failedAt;
    /// <summary>Die laufende Aktualisierung (höchstens eine) — läuft unabhängig vom Aufrufer zu Ende.</summary>
    private Task? _refresh;

    protected DbIpLiteList(IHttpClientFactory httpFactory, IConfiguration config, ILogger logger, Func<DateTime> utcNow,
        DbIpLiteListOptions options)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _utcNow = utcNow;
        _options = options;
        var s = options.Section;
        Enabled = config.GetValue($"{s}:Enabled", true);
        _sourceUrlTemplate = config[$"{s}:SourceUrlTemplate"] ?? options.DefaultSourceUrlTemplate;
        _cachePath = config[$"{s}:CachePath"] ?? Path.Combine(Path.GetTempPath(), options.DefaultCacheFile);
        _downloadTimeout = TimeSpan.FromSeconds(Math.Clamp(config.GetValue($"{s}:DownloadTimeoutSeconds", 60.0), 0.01, 600));
        _callerWait = TimeSpan.FromSeconds(Math.Clamp(config.GetValue($"{s}:CallerWaitSeconds", 2.0), 0, 30));
    }

    /// <summary><c>&lt;Section&gt;:Enabled</c> (Vorgabe an).</summary>
    protected bool Enabled { get; }

    protected abstract TTable Parse(TextReader reader);
    protected abstract int CountOf(TTable table);

    /// <summary>Die Liste, wie sie gerade da ist, ohne zu warten (Anfrage-Weg). Fehlt sie oder ist sie alt, stößt der
    /// Aufruf die Aktualisierung im Hintergrund an.</summary>
    protected TTable? CurrentTable()
    {
        var now = _utcNow();
        var table = _table;
        var stale = table is null || now - _loadedAt >= MaxAge;
        if (stale && !(_failedAt is DateTime failed && now - failed < RetryAfterFailure))
            lock (_refreshLock) _refresh ??= Task.Run(() => RefreshAsync(now));
        return table;
    }

    /// <summary>
    /// Die Liste, wenn sie da ist. Fehlt sie oder ist sie alt, startet EINE Aktualisierung im Hintergrund — mit eigenem
    /// Zeitlimit (<c>:DownloadTimeoutSeconds</c>, 60 s), nicht mit dem Token des Aufrufers (N6-006: KidHub bricht
    /// nach 4 s ab, der Download fing so jedes Mal von vorn an). Der Aufrufer wartet höchstens
    /// <c>:CallerWaitSeconds</c> (2 s) und bekommt sonst, was da ist; sein Abbruch beendet nur sein Warten.
    /// </summary>
    protected async Task<TTable?> EnsureTableAsync(CancellationToken ct)
    {
        var now = _utcNow();
        if (_table is not null && now - _loadedAt < MaxAge) return _table;
        if (_failedAt is DateTime failed && now - failed < RetryAfterFailure) return _table;

        Task refresh;
        lock (_refreshLock) refresh = _refresh ??= Task.Run(() => RefreshAsync(now));
        try { await refresh.WaitAsync(_callerWait, ct); }
        catch (TimeoutException) { /* läuft im Hintergrund weiter — diesmal ohne Liste */ }
        return _table;
    }

    private async Task RefreshAsync(DateTime now)
    {
        try
        {
            var fresh = File.Exists(_cachePath) && now - File.GetLastWriteTimeUtc(_cachePath) < MaxAge;
            var table = fresh ? Load(_cachePath) : await DownloadAsync(now);
            if (table is null)
            {
                _failedAt = now;
                // Eine alte Datei ist besser als keine — die Bereiche wechseln selten.
                if (_table is null && File.Exists(_cachePath)) _table = Load(_cachePath);
                return;
            }

            _table = table;
            _loadedAt = now;
            _failedAt = null;
            _logger.LogInformation("{Label} geladen: {Count} Bereiche.", _options.Label, CountOf(table));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Label} nicht verfügbar.", _options.Label);
            _failedAt = now;
        }
        finally
        {
            lock (_refreshLock) _refresh = null;
        }
    }

    /// <summary>Diesen Monat, sonst den vorigen (am Monatsersten steht die neue Datei eventuell noch nicht da). Die Datei
    /// wird erst übernommen, wenn sie sich als gz-Liste mit mindestens einem Bereich lesen lässt — sonst galt eine
    /// Fehlerseite mit 200 als „frisch“ und blockierte 35 Tage lang jeden neuen Download (N6-006). Das eigene
    /// Zeitlimit zählt als Fehlschlag.</summary>
    private async Task<TTable?> DownloadAsync(DateTime now)
    {
        var http = _httpFactory.CreateClient(_options.ClientName);
        http.Timeout = Timeout.InfiniteTimeSpan;   // es gilt das eigene Zeitlimit unten
        using var limit = new CancellationTokenSource(_downloadTimeout);
        foreach (var month in new[] { now, now.AddMonths(-1) })
        {
            var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, _sourceUrlTemplate, month);
            var temp = _cachePath + ".part";
            try
            {
                using var response = await http.GetAsync(url, limit.Token);
                if (!response.IsSuccessStatusCode) continue;
                await using (var file = File.Create(temp))
                    await response.Content.CopyToAsync(file, limit.Token);
                var table = Load(temp);
                if (CountOf(table) == 0) throw new InvalidDataException($"{_options.Label} ohne einen einzigen Bereich.");
                File.Move(temp, _cachePath, overwrite: true);
                return table;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Label}: Download von {Url} fehlgeschlagen.", _options.Label, url);
                TryDelete(temp);
            }
        }
        return null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* nächster Versuch überschreibt sie */ }
    }

    private TTable Load(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return Parse(reader);
    }
}
