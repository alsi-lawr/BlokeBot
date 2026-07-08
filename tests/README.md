# Test Boundaries

Run the active test set with:

```bash
dotnet restore CommandBot.slnx
dotnet test CommandBot.slnx --no-restore -v:minimal
```

Projects:

- `BlokeBot.Commands.Tests` covers the fluent command dispatcher/builder library only.
- `BlokeBot.Eventing.Tests` covers the standalone eventing leaf library only.
- `BlokeBot.Persistence.Tests` covers database model constraints and persistence invariants.
- `BlokeBot.Twitch.Tests` covers Twitch primitives and platform clients only.
- `BlokeBot.Twitch.Auth.Tests` covers Twitch OAuth/token auth only.
- `BlokeBot.Twitch.Runtime.Tests` covers Twitch chat/runtime transports only.
- `BlokeBot.Tests` covers BlokeBot application services, auth/session policy, command behavior, and lifecycle orchestration.
- `BlokeBot.Testing` contains shared test fixtures and is not a test project.
