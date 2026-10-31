namespace JadakMusic.ReceiptService.Models;

/// <summary>
/// WHAT: Config for sending the receipt email through Brevo's transactional email API.
///
/// WHY: Kept separate from StripeOptions/BusinessInfo so each concern (payments,
/// email, business facts) has its own small, obviously-named settings class.
///
/// HOW: Bound from the "Brevo" config section - same env var pattern as Stripe
/// (e.g. "Brevo__ApiKey").
/// </summary>
public sealed class BrevoOptions
{
    /// <summary>Brevo API key, from Brevo dashboard -> SMTP &amp; API -> API Keys.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The "from" address the receipt email is sent from. Must be a sender
    /// verified in Brevo (Brevo dashboard -> Senders &amp; IP).</summary>
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>The "from" display name, e.g. "Jada K Music".</summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// Optional address to quietly BCC on every receipt email, so someone on the
    /// business side gets notified a payment came in. Left null/blank, nobody
    /// gets BCC'd. BCC (not CC) on purpose - the customer should only ever see
    /// their own email in the headers, not an internal notification address.
    /// </summary>
    public string? NotifyEmail { get; set; }
}
