namespace JadakMusic.ReceiptService.Models;

/// <summary>
/// WHAT: The static, "always the same on every receipt" business details that
/// appear on the PDF - name, location, contact info, and the boilerplate
/// payment-policy text/payment methods for the remaining balance.
///
/// WHY: Keeping these out of the PDF-building code means Jada's phone number,
/// policy wording, Zelle/CashApp/Venmo handles, etc. can be corrected in one
/// place (appsettings.json or a Railway env var) without touching or
/// redeploying code.
///
/// HOW: Bound from the "Business" config section. Values below are the real
/// ones from Jada's existing invoice template.
/// </summary>
public sealed class BusinessInfo
{
    public string OwnerName { get; set; } = "Jada K.";
    public string CityState { get; set; } = "Chicago, Illinois";
    public string Phone { get; set; } = "708-574-8961";
    public string Email { get; set; } = "info@jadakmusic.com";
    public string Instagram { get; set; } = "jadakmusic24";
    public string YouTube { get; set; } = "jadakonthebeat";

    // --- "Payment Notes" boilerplate, matching Jada's existing invoice
    // template. These two lines are the same on every receipt regardless of
    // the specific booking, which is why they live here as plain config
    // rather than coming from Stripe. (A third line, "this deposit is
    // non-refundable," is always true for this document and is written
    // directly into InvoicePdfService instead of duplicated here.)
    public string RemainingBalanceNote { get; set; } =
        "Remaining balance is due no later than 3 days prior to your event.";
    public string RescheduleNote { get; set; } =
        "Your deposit may be transferred one time if the event is rescheduled with advance notice.";

    // --- Payment methods accepted for the REMAINING balance - not this
    // deposit, which was already paid by card through Stripe.
    public string Zelle { get; set; } = "224-422-6029";
    public string CashApp { get; set; } = "$Jadakmusic";
    public string Venmo { get; set; } = "Jadakmusic";
}