public static partial class RecreationUtility
{
    static bool OrganizationRentalIsFree(ItemEntry fee)
        => fee == null || string.IsNullOrEmpty(fee.itemID) || fee.itemCount <= 0;

    public static bool CanAffordOrganizationRental(Manageable organizer, Manageable venue, ItemEntry fee)
    {
        if (organizer == null || venue == null) return false;
        if (organizer == venue || OrganizationRentalIsFree(fee)) return true;
        return fee.BaseItem != null && (!organizer.isPlayerFaction || organizer.Inventory.HasRequiredItems(fee, 1));
    }

    public static bool TryPayOrganizationRental(RecreationGroup g, out string warning)
    {
        warning = "";
        if (g == null || g.source != RecreationBookingSource.Organization || g.isOrganizationAudience || g.rentalPaymentCompleted) return true;
        // Loaded, already-running occurrences must never acquire a retroactive charge.
        if (g.ActivityJob is Job_Activity job && job.Phase == ActivityPhase.Launched) return true;

        var organizer = Organization(g.organizerFactionID);
        var venue = Organization(g.factionID);
        if (organizer != null && venue != null)
        {
            if (organizer == venue || OrganizationRentalIsFree(g.rentalFee))
            {
                g.rentalPaymentCompleted = true;
                return true;
            }
            if (CanAffordOrganizationRental(organizer, venue, g.rentalFee)
                && organizer.TradeManager != null
                && organizer.TradeManager.TryChargeObligation(organizer, venue, g.rentalFee, out warning))
            {
                g.rentalPaymentCompleted = true;
                string label = LocalizeDictionary.QueryThenParse("ui_recreation_rental_label")
                    .Replace("$activity$", g.DisplayName)
                    .Replace("$organization$", organizer.FactionDisplayName)
                    .Replace("$venue$", venue.FactionDisplayName);
                organizer.DailyReport.tradeLogs.Add(LocalizeDictionary.QueryThenParse("obligation_report_due")
                    .Replace("$name$", label).Replace("$item$", g.rentalFee.Print));
                venue.DailyReport.tradeLogs.Add(LocalizeDictionary.QueryThenParse("obligation_report_incoming")
                    .Replace("$name$", label).Replace("$item$", g.rentalFee.Print));
                return true;
            }
        }

        warning = LocalizeDictionary.QueryThenParse("ui_recreation_rental_failed")
            .Replace("$organization$", organizer?.FactionDisplayName ?? g.organizerFactionID)
            .Replace("$venue$", venue?.FactionDisplayName ?? g.factionID)
            .Replace("$price$", (g.rentalFee ?? new ItemEntry()).Print);
        organizer?.DailyReport.AddTradeWarning(warning);
        return false;
    }
}
