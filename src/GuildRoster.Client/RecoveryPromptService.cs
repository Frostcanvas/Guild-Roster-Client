namespace GuildRoster.Client;

internal static class RecoveryPromptService
{
    public static async Task PromptPendingAsync(
        string serverBaseUrl,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RecoveryOffer> offers;
        try
        {
            offers = await GrmArchiveApiClient.GetPendingRecoveryOffersAsync(
                serverBaseUrl,
                bearerToken,
                cancellationToken);
        }
        catch (Exception ex) when (ex is GuildRosterApiException or HttpRequestException or TaskCanceledException)
        {
            return;
        }

        foreach (var offer in offers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var dialog = new RecoveryOfferDialog(offer);
            _ = dialog.ShowDialog();

            var decision = dialog.Choice switch
            {
                RecoveryOfferChoice.ReviewRestore => "review_restore",
                RecoveryOfferChoice.KeepCurrent => "keep_current",
                _ => "ask_later",
            };
            var selectedFields = dialog.Choice == RecoveryOfferChoice.ReviewRestore
                ? dialog.SelectedFields
                : Array.Empty<string>();

            try
            {
                await GrmArchiveApiClient.DecideRecoveryOfferAsync(
                    serverBaseUrl,
                    bearerToken,
                    offer.Id,
                    decision,
                    selectedFields,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is GuildRosterApiException or HttpRequestException or TaskCanceledException)
            {
                MessageBox.Show(
                    "The recovery choice could not be saved to Services01. Nothing was restored or discarded; the offer will be shown again later.",
                    "Returning Member Recovery",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (dialog.Choice == RecoveryOfferChoice.ReviewRestore)
            {
                MessageBox.Show(
                    "The selected archived GRM fields were saved as a protected restore request. No guild rank or other Blizzard-owned state was changed. Field write-back will occur only through the explicit restore step.",
                    "Recovery Review Saved",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
    }
}
