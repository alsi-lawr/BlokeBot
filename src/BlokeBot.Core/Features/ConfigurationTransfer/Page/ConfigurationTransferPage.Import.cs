using System.Text;
using Microsoft.AspNetCore.Components.Forms;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Page;

public partial class ConfigurationTransferPage
{
    private async Task ReadFileAsync(InputFileChangeEventArgs args)
    {
        _busy = true;
        try
        {
            await using var stream = args.File.OpenReadStream(
                ConfigurationDocumentCodec.MaximumBytes
            );
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            _pastedJson = Encoding.UTF8.GetString(memory.ToArray());
            await PreviewBytesAsync(memory.ToArray());
        }
        catch (IOException)
        {
            _parseIssue = new("$", "The configuration file exceeds the 2 MB limit.");
        }
        finally
        {
            _busy = false;
        }
    }

    private Task PreviewPastedAsync() => PreviewBytesAsync(Encoding.UTF8.GetBytes(_pastedJson));

    private async Task PreviewBytesAsync(byte[] bytes)
    {
        _parseIssue = null;
        _applyIssue = null;
        var parsed = _codec.Parse(bytes);
        if (parsed is ConfigurationDocumentParseOutcome.Invalid invalid)
        {
            _parseIssue = invalid.Issue;
            return;
        }
        _document = ((ConfigurationDocumentParseOutcome.Valid)parsed).Document;
        _strategies.Clear();
        _importSections.Clear();
        _resolutions.Clear();
        _renames.Clear();
        _enablementSelections.Clear();
        foreach (var section in PresentSections(_document))
        {
            _ = _importSections.Add(section);
            _strategies[section] =
                section == ConfigurationSectionId.Guessing
                    ? ImportConflictStrategy.ReplaceSection
                    : ImportConflictStrategy.Merge;
        }
        await RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_document is null)
        {
            return;
        }

        var outcome = await _previewService.PreviewAsync(
            _document,
            BuildPreviewSelection(),
            CancellationToken.None
        );
        _preview = outcome is ConfigurationPreviewOutcome.Success success ? success.Preview : null;
    }

    private async Task ApplyAsync()
    {
        if (_document is null || _preview is null)
        {
            return;
        }

        _busy = true;
        _applyIssue = null;
        try
        {
            ConfigurationImportApplyOutcome? outcome = null;
            await RunSelectedHostMutationAsync(
                HostId,
                async () =>
                    outcome = await _coordinator.ApplyAsync(
                        _session,
                        _document,
                        BuildSelection(),
                        new(_session.UserId, _session.Login),
                        CancellationToken.None
                    )
            );
            switch (outcome)
            {
                case ConfigurationImportApplyOutcome.Applied applied:
                    _applied = applied.Result;
                    if (applied.Result.ActivationId is { } activationId)
                    {
                        _activation = await _activations.LoadAsync(
                            HostId,
                            activationId,
                            CancellationToken.None
                        );
                        StartActivationPolling();
                    }
                    break;
                case ConfigurationImportApplyOutcome.Invalid invalid:
                    _applyIssue = string.Join(
                        " ",
                        invalid.Issues.Select(x => $"{x.Location}: {x.Message}")
                    );
                    break;
                case ConfigurationImportApplyOutcome.Rejected rejected:
                    _applyIssue = $"{rejected.Message} Operation ID: {rejected.OperationId}";
                    break;
                case ConfigurationImportApplyOutcome.Failed failed:
                    _applyIssue =
                        $"The import could not be saved. Operation ID: {failed.OperationId}.";
                    break;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RetryActivationAsync()
    {
        if (_activation is null)
        {
            return;
        }

        if (await _activations.RetryAsync(HostId, _activation.Id, CancellationToken.None))
        {
            _activation = await _activations.LoadAsync(
                HostId,
                _activation.Id,
                CancellationToken.None
            );
            StartActivationPolling();
        }
    }
}
