namespace JadakMusic.ReceiptService.Models;

/// <summary>
/// WHAT: The booking details the Angular form sends right before a customer
/// pays their deposit - who they are, how much the deposit is, and the event
/// specifics that used to only live in a separate email/text thread with Jada.
///
/// WHY: A Stripe Checkout Session can carry a small amount of custom
/// "metadata" alongside the payment itself. By collecting these fields on our
/// own form first (instead of using the old static Payment Link button), we
/// can attach them to the session in POST /create-checkout-session, and the
/// webhook handler reads them back out later when building the PDF receipt -
/// this is what closes the "known gap" documented on DepositReceiptData.
///
/// HOW: Posted as JSON from Angular to POST /create-checkout-session.
/// </summary>
public sealed class CreateCheckoutSessionRequest
{
    public required string CustomerName { get; init; }
    public required string CustomerEmail { get; init; }

    /// <summary>
    /// Deposit amount in whole dollars, e.g. 50.00 - converted to cents before
    /// being sent to Stripe, since Stripe always wants the smallest currency
    /// unit. Not hardcoded, since Jada quotes different deposit amounts per
    /// client (same "customer chooses price" flexibility the old Payment Link
    /// had).
    /// </summary>
    public required decimal DepositAmount { get; init; }

    public required string EventDate { get; init; }
    public required string PerformanceLocation { get; init; }

    /// <summary>Free-text notes, e.g. "Primary location is an outdoor patio..."</summary>
    public string? Instructions { get; init; }

    /// <summary>Free-text notes, e.g. space/surface requirements for the instrument.</summary>
    public string? SetupRequirements { get; init; }
}
