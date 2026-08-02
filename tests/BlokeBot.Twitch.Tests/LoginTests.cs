using Shouldly;

namespace BlokeBot.Twitch.Tests;

public sealed class LoginTests
{
    [Test]
    [Arguments(" @Streamer ")]
    [Arguments(" #Streamer ")]
    public void PrefixedLogin_Normalizing_TrimsPrefixAndLowercases(string login) =>
        Login.Normalize(login).ShouldBe("streamer");
}
