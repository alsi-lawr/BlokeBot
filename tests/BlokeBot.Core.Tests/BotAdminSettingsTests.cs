using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Identity;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class BotAdminSettingsTests
{
    [Test]
    public void MutableAdminArray_MappingSettings_NormalizesAndCopiesValues()
    {
        string[] admins = [" Alice ", "@BOB", "alice"];
        var options = new BlokeBotOptions { BotAdmins = admins };

        var settings = BotAdminSettings.FromOptions(options);
        admins[0] = "mallory";

        settings.BotAdmins.ShouldBe(
            [LoginName.Parse("alice"), LoginName.Parse("bob")],
            ignoreOrder: true
        );
        new BotAdminService(settings).IsAdmin("@ALICE").ShouldBeTrue();
        new BotAdminService(settings).IsAdmin("mallory").ShouldBeFalse();
    }
}
