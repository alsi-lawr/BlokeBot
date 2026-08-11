using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Competitions;

public static class CompetitionPresentation
{
    public static string Label(this CompetitionFormat value) =>
        value switch
        {
            CompetitionFormat.Tournament => "Tournament",
            CompetitionFormat.RoundRobin => "Round robin",
            CompetitionFormat.PredictionLeague => "Prediction league",
            _ => throw new UnreachableException(),
        };

    public static string Label(this CompetitionEntryKind value) =>
        value switch
        {
            CompetitionEntryKind.Individual => "Individuals",
            CompetitionEntryKind.Team => "Teams",
            _ => throw new UnreachableException(),
        };

    public static string Label(this CompetitionStatus value) =>
        value switch
        {
            CompetitionStatus.Draft => "Draft",
            CompetitionStatus.Registration => "Registration",
            CompetitionStatus.Running => "Running",
            CompetitionStatus.Completed => "Completed",
            CompetitionStatus.Archived => "Archived",
            _ => throw new UnreachableException(),
        };

    public static string Label(this CompetitionTiebreak value) =>
        value switch
        {
            CompetitionTiebreak.ScoreDifferenceThenScoreFor => "Score difference → scored",
            CompetitionTiebreak.ScoreForThenWins => "Score for → wins",
            _ => throw new UnreachableException(),
        };
}
