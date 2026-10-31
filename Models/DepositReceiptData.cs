namespace JadakMusic.ReceiptService.Models;

/// <summary>
/// WHAT: The handful of facts about one successful deposit payment that the PDF
/// and email actually need.
///
/// WHY: The webhook handler pulls this out of Stripe's Checkout Session object and
/// hands it to the PDF/email services as this simple record instead of passing the
/// raw Stripe Session type around. That keeps InvoicePdfService and
/// BrevoEmailService from needing to know anything about Stripe at all - useful if
/// you ever swap payment providers, and makes them easy to test on their own.
///
/// GAP NOW CLOSABLE: when a Checkout Session was created through our own
/// POST /create-checkout-session endpoint (see CreateCheckoutSessionRequest),
/// the four fields below are populated from the session's metadata. A
/// Checkout Session that still came from the old static Payment Link won't
/// have this metadata, so these stay null in that case - the PDF just omits
/// those sections rather than erroring, so both paths keep working side by
/// side during the transition.
/// </summary>
public sealed record DepositReceiptData
{
    public required string InvoiceNumber { get; init; }
    public required DateTime PaidAtUtc { get; init; }
    public required string CustomerName { get; init; }
    public required string CustomerEmail { get; init; }
    public required decimal AmountPaid { get; init; }
    public required string Currency { get; init; }

    /// <summary>Line-item description from the Checkout Session, e.g. "Booking Deposit".</summary>
    public required string Description { get; init; }

    // --- Event-specific details, from Checkout Session metadata (see "GAP NOW
    // CLOSABLE" above). Null when the session didn't carry this metadata.
    public string? EventDate { get; init; }
    public string? PerformanceLocation { get; init; }
    public string? Instructions { get; init; }
    public string? SetupRequirements { get; init; }
}