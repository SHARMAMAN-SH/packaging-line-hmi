using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

public record TagReading(string Tag, object? Value, string Status, DateTime? SourceTime, bool Usable);
public record LineSnapshot(bool Connected, string State, string Message, TagReading[] Tags);

public sealed class LineMonitor(ILogger<LineMonitor> logger, IConfiguration settings) : BackgroundService
{
    private const string NamespaceUri = "urn:packaging-line:training";
    private static readonly string[] TagNames = ["ConveyorRunning", "LineSpeed", "PackCount", "JamDetected"];
    private volatile LineSnapshot current = Offline("Waiting for the OPC UA simulator");
    public LineSnapshot Current => current;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var port = settings.GetValue("OpcPort", 4848);
        var url = $"opc.tcp://127.0.0.1:{port}/packaging/";
        var pki = Path.Combine(Path.GetTempPath(), "packaging-hmi-pki");
        var telemetry = DefaultTelemetry.Create(_ => { });
        var application = new ApplicationInstance(telemetry)
        {
            ApplicationName = "Packaging HMI",
            ApplicationType = ApplicationType.Client
        };
        var config = await application.Build("urn:localhost:packaging-hmi", "urn:packaging-line:training")
            .SetOperationTimeout(2500)
            .AsClient()
            .AddSecurityConfiguration([new CertificateIdentifier
            {
                SubjectName = "CN=Packaging HMI",
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine(pki, "own")
            }], pki)
            .CreateAsync(stop);

        var sessionFactory = new DefaultSessionFactory(telemetry);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var description = await CoreClientUtils.SelectEndpointAsync(config, url, false, telemetry, stop);
                var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(config));
                using var session = await sessionFactory.CreateAsync(
                    config, endpoint, false, config.ApplicationName, 15000,
                    new UserIdentity(), null, stop);

                var index = session.NamespaceUris.GetIndex(NamespaceUri);
                if (index < 0) throw new InvalidOperationException("Simulator namespace is missing.");
                var nodes = new ReadValueIdCollection(TagNames.Select(name => new ReadValueId
                {
                    NodeId = new NodeId(name, (ushort)index),
                    AttributeId = Attributes.Value
                }));

                while (!stop.IsCancellationRequested)
                {
                    // Read all four tag values and their metadata in one OPC UA request.
                    var result = await session.ReadAsync(null, 0, TimestampsToReturn.Source, nodes, stop);
                    var readings = result.Results.Select((value, i) => ReadTag(TagNames[i], value)).ToArray();
                    current = DescribeLine(readings);
                    await Task.Delay(1000, stop);
                }
            }
            catch (Exception error) when (!stop.IsCancellationRequested)
            {
                current = Offline("OPC UA source unavailable. Reconnecting.");
                logger.LogDebug(error, "OPC UA read or connection failed");
                await Task.Delay(1500, stop);
            }
        }
    }

    private static TagReading ReadTag(string name, DataValue data)
    {
        var source = data.SourceTimestamp == DateTime.MinValue ? (DateTime?)null : data.SourceTimestamp;
        var status = Opc.Ua.StatusCodes.GetBrowseName(data.StatusCode.Code);
        if (!StatusCode.IsGood(data.StatusCode)) return new(name, null, status, source, false);

        // An old value can still have a Good OPC UA status code.
        if (source is null || DateTime.UtcNow - source.Value > TimeSpan.FromSeconds(4))
            return new(name, null, $"{status} / stale or missing time", source, false);

        return new(name, data.Value, status, source, data.Value is not null);
    }

    private static LineSnapshot DescribeLine(TagReading[] readings)
    {
        if (readings[3].Value is true)
            return new(true, "Jam", "Discharge jam detected. Conveyor stopped in the simulation.", readings);

        var unavailable = readings.FirstOrDefault(tag => !tag.Usable);
        if (unavailable is not null)
            return new(true, "Data fault", $"{unavailable.Tag} is unavailable. Check its OPC status and source time.", readings);

        return readings[0].Value is true
            ? new(true, "Running", "Conveyor running. Discharge clear.", readings)
            : new(true, "Stopped", "Conveyor stopped. Discharge clear.", readings);
    }

    private static LineSnapshot Offline(string message) => new(false, "Offline", message,
        TagNames.Select(name => new TagReading(name, null, "Unavailable", null, false)).ToArray());
}
