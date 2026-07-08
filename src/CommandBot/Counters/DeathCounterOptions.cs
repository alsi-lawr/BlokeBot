using System.ComponentModel.DataAnnotations;

public sealed class DeathCounterOptions
{
    [Required]
    public string DatabasePath { get; init; } = "commandbot.db";
}
