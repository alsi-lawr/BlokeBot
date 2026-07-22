namespace BlokeBot.Persistence;

public sealed class UnsupportedDatabaseBaselineException()
    : Exception(
        "The existing database does not match the supported Hetzner baseline. "
            + "Restore the deployed v0.1.x schema before starting BlokeBot."
    );
