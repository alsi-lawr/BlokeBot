using Microsoft.AspNetCore.Components;

namespace BlokeBot.Site.Components;

public partial class DeviceShowcase
{
    [Parameter, EditorRequired]
    public required string DarkLaptopSource { get; set; }

    [Parameter, EditorRequired]
    public required string LightLaptopSource { get; set; }

    [Parameter, EditorRequired]
    public required string LaptopAlt { get; set; }

    [Parameter, EditorRequired]
    public required string DarkPhoneSource { get; set; }

    [Parameter, EditorRequired]
    public required string LightPhoneSource { get; set; }

    [Parameter, EditorRequired]
    public required string PhoneAlt { get; set; }

    [Parameter, EditorRequired]
    public required string Caption { get; set; }
}
