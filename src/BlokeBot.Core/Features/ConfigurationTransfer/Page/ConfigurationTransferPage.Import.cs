using System.Text;
using Microsoft.AspNetCore.Components.Forms;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Page;

public partial class ConfigurationTransferPage
{
    private async Task ReadFileAsync(InputFileChangeEventArgs args)
    {
        _busy = true;
        _automationEffectsApproved = false;
        var request = ++_previewRequest;
        var host = HostId;
        try
        {
            await using var stream = args.File.OpenReadStream(long.MaxValue);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            if (request != _previewRequest || HostId != host)
            {
                return;
            }
            _pastedJson = Encoding.UTF8.GetString(memory.ToArray());
            await PreviewBytesAsync(memory.ToArray());
        }
        catch (IOException)
        {
            _parseIssue = new(
                "$",
                "The file could not be read. Select it again or paste its contents."
            );
        }
        catch (OutOfMemoryException)
        {
            _parseIssue = new(
                "$",
                "There is not enough memory to read this file. Close other work or use a host with more available memory."
            );
        }
        finally
        {
            _busy = false;
        }
    }

    private Task PreviewPastedAsync() => PreviewOutcomeAsync(_codec.Parse(_pastedJson));

    private async Task PreviewBytesAsync(byte[] bytes)
    {
        _parseIssue = null;
        _applyIssue = null;
        await PreviewOutcomeAsync(_codec.Parse(bytes));
    }

    private async Task PreviewOutcomeAsync(ConfigurationDocumentParseOutcome parsed)
    {
        _parseIssue = null;
        _applyIssue = null;
        _automationEffectsApproved = false;
        _previewRequest++;
        _preview = null;
        _document = null;
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
        _guessingProfileTargets.Clear();
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

        _automationEffectsApproved = false;
        var request = ++_previewRequest;
        var document = _document;
        var host = HostId;
        var previous = _preview;
        _previewPending = true;
        try
        {
            _session = (await LoadPageContextAsync()).Session;
            if (HostId != host)
            {
                ResetImport();
                return;
            }
            if (request != _previewRequest || !ReferenceEquals(document, _document))
            {
                return;
            }
            var outcome = await _previewService.PreviewAsync(
                document,
                BuildSelection(),
                CancellationToken.None
            );
            if (
                request != _previewRequest
                || !ReferenceEquals(document, _document)
                || HostId != host
            )
            {
                return;
            }
            _automationEffectsApproved = false;
            _preview = outcome is ConfigurationPreviewOutcome.Success success
                ? success.Preview with
                {
                    Sections = _strategies
                        .Keys.Select(section =>
                            success.Preview.Sections.FirstOrDefault(value =>
                                value.Section == section
                            ) ?? previous!.Sections.Single(value => value.Section == section)
                        )
                        .ToArray(),
                }
                : null;
        }
        finally
        {
            if (request == _previewRequest)
            {
                _previewPending = false;
            }
        }
    }

    private async Task ApplyAsync()
    {
        if (_document is null || _preview is null || _previewPending)
        {
            return;
        }

        _busy = true;
        _applyIssue = null;
        var request = _previewRequest;
        var document = _document;
        var host = HostId;
        var selection = BuildSelection();
        try
        {
            ConfigurationImportApplyOutcome? outcome = null;
            await RunSelectedHostMutationAsync(
                HostId,
                async () =>
                    outcome = await _coordinator.ApplyAsync(
                        PageContext.Session,
                        document,
                        selection,
                        new(PageContext.Session.UserId, PageContext.Session.Login),
                        CancellationToken.None
                    )
            );
            if (HostId != host)
            {
                ResetImport();
                return;
            }
            if (request != _previewRequest || !ReferenceEquals(document, _document))
            {
                return;
            }
            switch (outcome)
            {
                case ConfigurationImportApplyOutcome.StaleAutomationReview:
                    await RefreshPreviewAsync();
                    _applyIssue =
                        "Automation effects changed. Review the current effects and approve them before applying again.";
                    break;
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
