# Test Boundaries

Run the active test set with:

```bash
dotnet restore CommandBot.slnx
dotnet test CommandBot.slnx --no-restore -v:minimal
```

Projects:

- `Alsi.TwitchBot.Tests` covers the reusable Twitch bot library only.
- `BlokeBot.Eventing.Tests` covers the standalone eventing leaf library only.
- `BlokeBot.Persistence.Tests` covers database model constraints and persistence invariants.
- `BlokeBot.Tests` covers BlokeBot application services, auth/session policy, command behavior, and lifecycle orchestration.
- `BlokeBot.Testing` contains shared test fixtures and is not a test project.
