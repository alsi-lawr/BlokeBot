using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandEditor
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Aliases { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool AllowEveryone { get; set; } = true;

    public bool AllowModerators { get; set; }

    public List<CustomCommandAllowedUserEditor> AllowedUsers { get; set; } = [];

    public string AllowedUserLoginDraft { get; set; } = string.Empty;

    public bool AllowedUserLookupInProgress { get; set; }

    public string? AllowedUserFeedback { get; set; }

    public int CooldownSeconds { get; set; }

    public CustomCommandCooldownScope CooldownScope { get; set; } =
        CustomCommandCooldownScope.Global;

    public CustomCommandInvocationLimit InvocationLimit { get; set; } =
        CustomCommandInvocationLimit.Unlimited;

    public string ResetViewerLogin { get; set; } = string.Empty;

    public ICustomCommandActionEditor Action
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new MessageCustomCommandActionEditor();

    public CustomCommandActionKind ActionKind
    {
        get => Action.Kind;
        set
        {
            if (value == Action.Kind)
            {
                return;
            }

            Action = value switch
            {
                CustomCommandActionKind.Message => new MessageCustomCommandActionEditor
                {
                    ReplyRoutes = Action.ReplyRoutes,
                },
                CustomCommandActionKind.Counter => new CounterCustomCommandActionEditor
                {
                    ReplyRoutes = Action.ReplyRoutes,
                },
                CustomCommandActionKind.OverlayCue => new OverlayCueCustomCommandActionEditor
                {
                    ReplyRoutes = Action.ReplyRoutes,
                },
                CustomCommandActionKind.Automation => new AutomationCustomCommandActionEditor(),
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
        }
    }
}

public sealed record CustomCommandAllowedUserEditor(
    string TwitchUserId,
    string Login,
    string DisplayName
);

public enum CustomCommandActionKind
{
    Message,
    Counter,
    OverlayCue,
    Automation,
}

public interface ICustomCommandActionEditor
{
    CustomCommandActionKind Kind { get; }

    CustomCommandReplyRoutesEditor ReplyRoutes { get; set; }
}

public sealed class CustomCommandReplyRoutesEditor
{
    public int? ZeroArgumentMessageLibraryEntryId { get; set; }

    public int? OneArgumentMessageLibraryEntryId { get; set; }

    public int? TwoArgumentMessageLibraryEntryId { get; set; }
}

public sealed class MessageCustomCommandActionEditor : ICustomCommandActionEditor
{
    public CustomCommandActionKind Kind => CustomCommandActionKind.Message;

    public CustomCommandReplyRoutesEditor ReplyRoutes
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();
}

public sealed class CounterCustomCommandActionEditor : ICustomCommandActionEditor
{
    public CustomCommandActionKind Kind => CustomCommandActionKind.Counter;

    public CustomCommandReplyRoutesEditor ReplyRoutes
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

    public int CounterId { get; set; }
}

public sealed class OverlayCueCustomCommandActionEditor : ICustomCommandActionEditor
{
    public CustomCommandActionKind Kind => CustomCommandActionKind.OverlayCue;

    public CustomCommandReplyRoutesEditor ReplyRoutes
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

    public Guid TargetOverlayPublicId { get; set; }

    public Guid CuePublicId { get; set; }

    public OverlayCueQueuePolicy QueuePolicy { get; set; } = OverlayCueQueuePolicy.Enqueue;

    public OverlayCueReplyOrder ReplyOrder { get; set; } = OverlayCueReplyOrder.After;
}

public sealed class AutomationCustomCommandActionEditor : ICustomCommandActionEditor
{
    public CustomCommandActionKind Kind => CustomCommandActionKind.Automation;

    public CustomCommandReplyRoutesEditor ReplyRoutes
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();
}

public sealed class CustomCounterEditor
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long Value { get; set; }
}
