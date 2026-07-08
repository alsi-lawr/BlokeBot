using System.ComponentModel.DataAnnotations;

public sealed class AllowedLoginOptions
{
    [MinLength(1)]
    public string[] AllowedLogins { get; init; } = [];
}
