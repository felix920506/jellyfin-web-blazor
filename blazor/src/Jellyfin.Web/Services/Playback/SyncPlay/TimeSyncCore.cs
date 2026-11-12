namespace Jellyfin.Web.Services;

/// <summary>SyncPlay's server clock plus the manually configured clock adjustment.</summary>
public sealed class TimeSyncCore(TimeSyncServer server, AppSettings appSettings)
{
    public TimeSyncServer Server { get; } = server;

    public double ExtraTimeOffset
    {
        get => double.TryParse(appSettings.Get("extraTimeOffset", "syncPlay"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        set => appSettings.Set("extraTimeOffset", value.ToString(System.Globalization.CultureInfo.InvariantCulture), "syncPlay");
    }

    public double OffsetMilliseconds => Server.OffsetMilliseconds + ExtraTimeOffset;

    public int PingMilliseconds => Server.PingMilliseconds;

    public DateTimeOffset RemoteToLocal(DateTimeOffset remote) => remote.AddMilliseconds(-OffsetMilliseconds);

    public DateTimeOffset LocalToRemote(DateTimeOffset local) => local.AddMilliseconds(OffsetMilliseconds);
}
