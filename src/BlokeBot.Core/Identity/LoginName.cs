namespace BlokeBot.Core.Identity;

public readonly record struct LoginName
{
    public LoginName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool IsEmpty => Value.Length == 0;

    public static LoginName Parse(string? value)
    {
        return new(Login.Normalize(value));
    }

    public override string ToString()
    {
        return Value;
    }

    public static implicit operator string(LoginName login)
    {
        return login.Value;
    }
}
