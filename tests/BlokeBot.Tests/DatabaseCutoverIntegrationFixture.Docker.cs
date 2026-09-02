using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace BlokeBot.Tests;

internal sealed partial class DatabaseCutoverIntegrationFixture : IAsyncDisposable
{
    private const string _database = "blokebot";
    private const string _password = "disposable-cutover-password";
    private const string _role = "cutover";
    private const string _otherRole = "cutover_other";
    private readonly string _root;
    private readonly IReadOnlyDictionary<string, string> _localStateHashes;

    private DatabaseCutoverIntegrationFixture(
        string root,
        DisposablePostgreSql primary,
        DisposablePostgreSql other,
        IReadOnlyDictionary<string, string> localStateHashes
    )
    {
        _root = root;
        Primary = primary;
        Other = other;
        _localStateHashes = localStateHashes;
    }

    internal DisposablePostgreSql Primary { get; }
    internal DisposablePostgreSql Other { get; }
    internal Guid OperationId { get; } = Guid.Parse("20a286a6-6530-475e-b320-15ba24d32aed");
    internal string StateDirectory => Path.Combine(_root, "state");
    internal string SqliteDatabasePath => Path.Combine(StateDirectory, "blokebot.db");
    internal string AdministratorConnectionFile => Path.Combine(_root, "target-admin.connection");
    internal string ApplicationConnectionFile => Path.Combine(_root, "target-app.connection");

    internal static async Task<DatabaseCutoverIntegrationFixture> CreateAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-cutover-integration-{Guid.NewGuid():N}"
        );
        _ = Directory.CreateDirectory(Path.Combine(root, "state"));
        DisposablePostgreSql? primary = null;
        DisposablePostgreSql? other = null;
        try
        {
            primary = await DisposablePostgreSql.StartAsync();
            other = await DisposablePostgreSql.StartAsync();
            var fixture = new DatabaseCutoverIntegrationFixture(
                root,
                primary,
                other,
                new Dictionary<string, string>()
            );
            await fixture.InitializeAsync();
            var hashes = fixture.CaptureLocalStateHashes();
            return new DatabaseCutoverIntegrationFixture(root, primary, other, hashes);
        }
        catch
        {
            if (other is not null)
            {
                await other.DisposeAsync();
            }
            if (primary is not null)
            {
                await primary.DisposeAsync();
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            throw;
        }
    }

    internal async Task SelectTargetAsync(DisposablePostgreSql target)
    {
        await WriteProtectedAsync(AdministratorConnectionFile, target.AdminConnectionString);
        await SelectApplicationTargetAsync(target, _database, _role);
    }

    internal Task SelectOtherDatabaseAsync(DisposablePostgreSql target) =>
        SelectApplicationTargetAsync(target, $"{_database}_other", _role);

    internal Task SelectOtherOwnerAsync(DisposablePostgreSql target) =>
        SelectApplicationTargetAsync(target, _database, _otherRole);

    private Task SelectApplicationTargetAsync(
        DisposablePostgreSql target,
        string database,
        string role
    ) =>
        WriteProtectedAsync(
            ApplicationConnectionFile,
            target.ApplicationConnectionString(database, role)
        );

    private static async Task WriteProtectedAsync(string path, string content)
    {
        await File.WriteAllTextAsync(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Other.DisposeAsync();
        await Primary.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    internal sealed class DisposablePostgreSql(string name, int port, string secretDirectory)
        : IAsyncDisposable
    {
        internal string ConnectionString => ApplicationConnectionString(_database, _role);

        internal string AdminConnectionString =>
            ApplicationConnectionString("postgres", "postgres");

        internal string ApplicationConnectionString(string database, string role) =>
            new NpgsqlConnectionStringBuilder
            {
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                Database = database,
                Username = role,
                Password = _password,
                Pooling = false,
                Timeout = 3,
                CommandTimeout = 30,
            }.ConnectionString;

        internal static async Task<DisposablePostgreSql> StartAsync()
        {
            var name = $"blokebot-cutover-test-{Guid.NewGuid():N}";
            var port = AvailablePort();
            var secretDirectory = Path.Combine(
                Path.GetTempPath(),
                $"blokebot-postgresql-secret-{Guid.NewGuid():N}"
            );
            _ = Directory.CreateDirectory(secretDirectory);
            var passwordFile = Path.Combine(secretDirectory, "password");
            await File.WriteAllTextAsync(passwordFile, _password);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(passwordFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            try
            {
                _ = await DockerAsync(
                    "run",
                    "--detach",
                    "--name",
                    name,
                    "--mount",
                    $"type=bind,source={passwordFile},target=/run/secrets/postgres-password,readonly",
                    "--env",
                    "POSTGRES_PASSWORD_FILE=/run/secrets/postgres-password",
                    "--publish",
                    $"127.0.0.1:{port}:5432",
                    "postgres:18-alpine"
                );
            }
            catch
            {
                Directory.Delete(secretDirectory, recursive: true);
                throw;
            }
            var instance = new DisposablePostgreSql(name, port, secretDirectory);
            try
            {
                await instance.PrepareAsync();
                return instance;
            }
            catch
            {
                await instance.DisposeAsync();
                throw;
            }
        }

        private async Task PrepareAsync()
        {
            Exception? lastFailure = null;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(AdminConnectionString);
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT 1;";
                    _ = await command.ExecuteScalarAsync();
                    lastFailure = null;
                    break;
                }
                catch (Exception exception) when (exception is NpgsqlException or SocketException)
                {
                    lastFailure = exception;
                    await Task.Delay(TimeSpan.FromMilliseconds(250));
                }
            }
            if (lastFailure is not null)
            {
                throw new InvalidOperationException(
                    "The disposable PostgreSql target did not start.",
                    lastFailure
                );
            }

            await using var admin = new NpgsqlConnection(AdminConnectionString);
            await admin.OpenAsync();
            await using var setup = admin.CreateCommand();
            setup.CommandText =
                $"CREATE ROLE {_role} LOGIN PASSWORD '{_password}'; CREATE ROLE {_otherRole} LOGIN PASSWORD '{_password}';";
            _ = await setup.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _ = await DockerAsync("rm", "--force", "--volumes", name);
            }
            finally
            {
                if (Directory.Exists(secretDirectory))
                {
                    Directory.Delete(secretDirectory, recursive: true);
                }
            }
        }

        private static int AvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static async Task<string> DockerAsync(params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            if (!process.Start())
            {
                throw new InvalidOperationException("Docker did not start.");
            }
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0
                ? output.Trim()
                : throw new InvalidOperationException(
                    $"Docker exited with code {process.ExitCode}: {error.Trim()}"
                );
        }
    }
}
