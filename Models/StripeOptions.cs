namespace JadakMusic.ReceiptService.Models;

/// <summary>
/// WHAT: Strongly-typed config for talking to Stripe, bound from the "Stripe" section
/// of appsettings.json / environment variables.
///
/// WHY: Having a typed options class (instead of pulling strings out of
/// IConfiguration everywhere) means a typo in a key name fails fast at startup
/// instead of silently returning null deep inside a service.
///
/// HOW: Railway sets these as real environment variables when deployed. ASP.NET
/// Core automatically maps an env var named "Stripe__SecretKey" (double underscore)
/// to this class's SecretKey property - see Program.cs where this is registered
/// with builder.Services.Configure&lt;StripeOptions&gt;(...).
/// </summary>
public sealed class StripeOptions
{
    /// <summary>
    /// Stripe's secret API key (starts with sk_test_... or sk_live_...). Used to
    /// create a real Checkout Session from POST /create-checkout-session (see
    /// Program.cs) - this is the key that actually talks to Stripe's API, as
    /// opposed to WebhookSecret below, which only verifies incoming requests.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// The signing secret for this specific webhook endpoint (starts with whsec_...).
    /// Stripe generates this when you add the webhook endpoint in the Dashboard
    /// (Developers -> Webhooks). It's how we prove an incoming request really came
    /// from Stripe and wasn't forged by someone else hitting our URL.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Where Stripe sends the customer's browser after a successful payment on
    /// the hosted Checkout page. REPLACE_ME with the real booking page URL
    /// before this goes live - this placeholder will 404.
    /// </summary>
    public string SuccessUrl { get; set; } = "https://REPLACE_ME.example.com/booking?paid=1";

    /// <summary>
    /// Where Stripe sends the customer's browser if they back out of paying.
    /// REPLACE_ME with the real booking page URL before this goes live.
    /// </summary>
    public string CancelUrl { get; set; } = "https://REPLACE_ME.example.com/booking";
}