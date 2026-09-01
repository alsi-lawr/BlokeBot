using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.DatabaseWorkloads;

return await BaselineCli.RunAsync(args);

public static class BaselineCli
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine(Usage());
                return 0;
            }

            var options = Parse(args.Skip(1).ToArray());
            if (args[0] == "verify-inventory")
            {
                var inventory = RawSqlInventory.Load(Required(options, "inventory"));
                RawSqlInventory.Verify(Required(options, "repo-root"), inventory);
                Console.WriteLine(inventory.Statements.Count);
                return 0;
            }
            if (args[0] == "verify-postgresql-authorities")
            {
                var connectionString = File.ReadAllText(Required(options, "connection-string-file"))
                    .Trim();
                var outcomes = await PostgreSqlAuthorityVerifier.VerifyAsync(
                    connectionString,
                    CancellationToken.None
                );
                Console.WriteLine(JsonSerializer.Serialize(outcomes, _jsonOptions));
                return 0;
            }
            var protocolPath = Required(options, "protocol");
            var digestPath = Required(options, "digest");
            var protocol = FrozenProtocol.Load(FrozenProtocolVersion.V1, protocolPath, digestPath);
            if (args[0] == "verify-protocol")
            {
                Console.WriteLine(FrozenProtocol.Digest(protocolPath));
                return 0;
            }
            if (args[0] is not ("run-sqlite" or "run-postgresql"))
            {
                throw new ArgumentException($"Unknown command '{args[0]}'.");
            }

            var outputPath = Required(options, "output");
            WorkloadDatabase database =
                args[0] == "run-sqlite"
                    ? new SqliteWorkloadDatabase(Required(options, "database"))
                    : new PostgreSqlWorkloadDatabase(
                        File.ReadAllText(Required(options, "connection-string-file")).Trim()
                    );
            await using var runner = new DatabaseBaselineRunner(
                protocol,
                FrozenProtocol.Digest(protocolPath),
                database
            );
            var result = await runner.RunAsync(CancellationToken.None);
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (outputDirectory is not null)
            {
                _ = Directory.CreateDirectory(outputDirectory);
            }
            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(result, _jsonOptions) + Environment.NewLine
            );
            return 0;
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or IOException
                        or InvalidDataException
                        or ProtocolDriftException
                        or InventoryDriftException
            )
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        if (args.Length % 2 != 0)
        {
            throw new ArgumentException("Every option requires a value.");
        }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            var key = args[index];
            if (
                !key.StartsWith("--", StringComparison.Ordinal)
                || !result.TryAdd(key[2..], args[index + 1])
            )
            {
                throw new ArgumentException($"Invalid or duplicate option '{key}'.");
            }
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing --{key}.");

    private static string Usage() =>
        """
            BlokeBot main-database workload baseline

            verify-protocol --protocol PATH --digest PATH
            verify-inventory --repo-root PATH --inventory PATH
            verify-postgresql-authorities --connection-string-file PATH
            run-sqlite --protocol PATH --digest PATH --database NEW_PATH --output PATH
            run-postgresql --protocol PATH --digest PATH --connection-string-file PATH --output PATH

            run-sqlite refuses an existing database path. It creates an offline synthetic database and
            writes only redacted aggregate evidence.
            run-postgresql owns only a dedicated blokebot_workload_v1 schema, refuses an existing
            schema, removes its schema after the run, and writes only redacted aggregate evidence.
            """;
}
