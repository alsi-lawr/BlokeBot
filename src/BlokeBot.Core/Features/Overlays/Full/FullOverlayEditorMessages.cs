namespace BlokeBot.Core.Features.Overlays.Full;

internal static class FullOverlayEditorMessages
{
    internal static string Rejection(FullOverlayRejection reason) =>
        reason.Kind switch
        {
            FullOverlayRejectionKind.Conflict =>
                "Another editor saved a newer revision. Your changes are kept here. Reload only when you are ready to discard them.",
            FullOverlayRejectionKind.Invalid =>
                "Check the overlay name and document. Incomplete source can still be saved as a draft.",
            FullOverlayRejectionKind.Unauthorized =>
                "Your channel access changed. Select your channel and sign in again.",
            FullOverlayRejectionKind.FeatureDisabled => "Overlays are disabled for this channel.",
            FullOverlayRejectionKind.NotFound =>
                "This overlay or history version is no longer available.",
            FullOverlayRejectionKind.Archived => "Restore this overlay before publishing.",
            FullOverlayRejectionKind.PublicationUnavailable =>
                "Publication is unavailable. Your working document is kept.",
            FullOverlayRejectionKind.PublicationRejected =>
                "Publication needs attention. Your working document is kept.",
            FullOverlayRejectionKind.SelectedVersion =>
                "The currently live version cannot be forgotten.",
            FullOverlayRejectionKind.ConfirmationRequired => "Confirm permanent deletion first.",
            FullOverlayRejectionKind.AccessUnavailable =>
                "The original live URL is unavailable for this older overlay. No key was changed.",
        };
}
