namespace BlokeBot.Features.Guessing.Configuration;

public abstract record GuessingProfileSelection
{
    private GuessingProfileSelection() { }

    public sealed record Default : GuessingProfileSelection;

    public sealed record Selected(int ProfileId) : GuessingProfileSelection;
}

public sealed record GuessingConfigurationSaved;

public sealed record GuessingProfileCreated(int ProfileId, string Message);

public sealed record GuessingProfileDeleted(string Message);

public sealed record GuessingConfigurationLoadFailure
{
    public string Message =>
        "That round type is no longer available. Reloaded the current settings.";
}

public abstract record GuessingConfigurationSaveFailure
{
    private GuessingConfigurationSaveFailure() { }

    public abstract string Message { get; }

    public sealed record ProfileNotFound : GuessingConfigurationSaveFailure
    {
        public override string Message =>
            "That round type is no longer available. Reload the page and try again.";
    }

    public sealed record ConcurrentEdit : GuessingConfigurationSaveFailure
    {
        public override string Message =>
            "That round type changed while you were editing. Reload the page and try again.";
    }

    public sealed record DuplicateProfileName : GuessingConfigurationSaveFailure
    {
        public override string Message => "A round type with that name already exists.";
    }

    public sealed record AliasAlreadyUsed(string Alias) : GuessingConfigurationSaveFailure
    {
        public override string Message => $"!{Alias} is already used by another bot command.";
    }
}

public sealed record GuessingProfileCreateFailure
{
    public string Message => "A round type with that name already exists.";
}

public abstract record GuessingProfileDeleteFailure
{
    private GuessingProfileDeleteFailure() { }

    public abstract string Message { get; }

    public sealed record ProfileNotFound : GuessingProfileDeleteFailure
    {
        public override string Message => "Round type not found.";
    }

    public sealed record ConcurrentEdit : GuessingProfileDeleteFailure
    {
        public override string Message =>
            "That round type changed while you were editing. Reload the page and try again.";
    }

    public sealed record LastProfile : GuessingProfileDeleteFailure
    {
        public override string Message => "Keep at least one round type.";
    }

    public sealed record UsedByPastRound : GuessingProfileDeleteFailure
    {
        public override string Message => "Round types used by past rounds cannot be deleted.";
    }
}
