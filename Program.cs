// WHAT: Entry point and the two HTTP endpoints this service exposes - one that
// creates a Stripe Checkout Session carrying the booking's event details, and
// one that receives Stripe's webhook when that session's payment completes,
// building a branded PDF receipt and emailing it to the customer.
//
// WHY: Decoupled from the Angular booking site on purpose wherever possible.
// The webhook receiver doesn't care how checkout happened - Stripe fires the
// same "checkout.session.completed" event either way. The one place Angular
// *does* need to change is calling POST /create-checkout-session instead of
// linking straight to the old static Payment Link, so the event date/
// location/notes can actually reach this service.
//
// HOW: A minimal API (no MVC controllers - this app is small enough that one
// file wiring things together, plus a few small service classes, is easier to
// follow than a full controller/action setup). Services are registered with
// dependency injection below and injected into each endpoint's handler.

using JadakMusic.ReceiptService.Models;
using JadakMusic.ReceiptService.Services;
using QuestPDF.Infrastructure;
using Stripe;
using Stripe.Checkout;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---------------------------------------------------------
// Binds appsettings.json's "Stripe" / "Brevo" / "Business" sections (or, in
// production on Railway, the equivalent Stripe__SecretKey / Brevo__ApiKey /
// etc. environment variables) into the typed options classes in Models/.
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection("Brevo"));
builder.Services.Configure<BusinessInfo>(builder.Configuration.GetSection("Business"));

// --- Services ----------------------------------------------------------
builder.Services.AddSingleton<IInvoicePdfService, InvoicePdfService>();
builder.Services.AddHttpClient<IReceiptEmailService, BrevoEmailService>();

// Stripe.net's client object, built once from config rather than the older
// "set a static StripeConfiguration.ApiKey somewhere" pattern - keeps the
// secret key out of global mutable state, which matters more once this app
// has more than one endpoint touching Stripe (the webhook receiver already
// doesn't need this key; POST /create-checkout-session below does).
builder.Services.AddSingleton(sp =>
{
    var stripeOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StripeOptions>>().Value;
    return new StripeClient(stripeOptions.SecretKey);
});

// --- CORS ----------------------------------------------------------------
// The Angular app runs on a different origin than this API (its own dev
// server port locally, and its own domain once deployed), so without this,
// the browser blocks POST /create-checkout-session with a CORS error before
// the request even leaves the page - this isn't optional for the new
// endpoint to work from Angular.
// REPLACE_ME: add the real jadakmusic.com domain here once that's live -
// local dev against the Angular dev server port works as-is below.
//
// IMPORTANT: this (and every other builder.Services.___ call) has to happen
// BEFORE builder.Build() below - once Build() runs, the service collection
// is locked, and adding to it after that throws
// "InvalidOperationException: The service collection cannot be modified
// because it is read-only."
const string AngularOriginsPolicy = "AngularOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularOriginsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:6550", "https://REPLACE_ME.example.com")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// QuestPDF needs its license type set once before generating any document.
// Community = free for individuals/small businesses - see questpdf.com/license.
QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// Everything from here down is app.___ (middleware/endpoints), not
// builder.Services.___ - that's the line Build() draws.
app.UseCors(AngularOriginsPolicy);

// A tiny "is this thing alive" endpoint - handy for Railway's health checks and
// for confirming the deployment worked before wiring up the real Stripe webhook.
app.MapGet("/", () => Results.Ok(new { status = "jadak-music-receipt-service is running" }));

// WHAT: Creates a real Stripe Checkout Session carrying the Angular booking
// form's event details, instead of sending the customer to the old static
// Payment Link. WHY: this is the one piece that actually closes the "known
// gap" - the static Payment Link had nowhere to put event date/location/
// setup notes, but a Checkout Session we create ourselves can carry them as
// metadata, which the webhook handler below reads back out. HOW: Angular
// POSTs the form here, gets back { url }, and redirects the browser there -
// same "customer briefly lands on Stripe's hosted checkout page" experience
// as the old Payment Link button, just backed by real booking data now.
app.MapPost("/create-checkout-session", async (
    CreateCheckoutSessionRequest request,
    StripeClient stripeClient,
    Microsoft.Extensions.Options.IOptions<StripeOptions> stripeOptions,
    ILogger<Program> logger) =>
{
    // Catches a form that's missing required fields before we bother calling
    // Stripe at all - this is just our own sanity check, not something Stripe
    // validates for us.
    if (string.IsNullOrWhiteSpace(request.CustomerEmail) ||
        string.IsNullOrWhiteSpace(request.CustomerName) ||
        string.IsNullOrWhiteSpace(request.EventDate) ||
        string.IsNullOrWhiteSpace(request.PerformanceLocation) ||
        request.DepositAmount <= 0)
    {
        return Results.BadRequest(new { error = "Missing required booking details." });
    }

    var sessionService = new SessionService(stripeClient);


    try
    {
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            CustomerEmail = request.CustomerEmail,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = (long)(request.DepositAmount * 100),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Booking Deposit",
                        },
                    },
                },
            ],
            Metadata = new Dictionary<string, string>
            {
                ["customer_name"] = request.CustomerName,
                ["event_date"] = request.EventDate,
                ["performance_location"] = request.PerformanceLocation,
                ["instructions"] = request.Instructions ?? string.Empty,
                ["setup_requirements"] = request.SetupRequirements ?? string.Empty,
            },
            SuccessUrl = stripeOptions.Value.SuccessUrl,
            CancelUrl = stripeOptions.Value.CancelUrl,
        });

        logger.LogInformation("Created Checkout Session {SessionId} for {Email}.", session.Id, request.CustomerEmail);

        return Results.Ok(new { url = session.Url });
    }
    catch (StripeException ex)
    {
        // Covers an invalid/placeholder API key (expected until the client's
        // real Stripe key is set in Railway) or any other Stripe-side rejection.
        // Without this catch, the exception crashes the request before the CORS
        // middleware can attach its headers - the browser then reports a
        // confusing "CORS policy" error instead of the real problem.
        logger.LogError(ex, "Stripe rejected the Checkout Session request.");
        return Results.Problem(
            detail: "Unable to start checkout with Stripe right now.",
            statusCode: StatusCodes.Status502BadGateway);
    }
});
// In-memory set of Stripe event IDs we've already processed. Stripe retries a
// webhook delivery if it doesn't get a prompt 200 response, which could
// otherwise email the same customer the same receipt twice. A real database
// (planned for the admin app phase) would make this durable across restarts;
// for now this in-memory guard is a reasonable first line of defense for a
// low-volume site, and is called out again in the README as a known limitation.
var processedEventIds = new HashSet<string>();
var processedEventIdsLock = new Lock();

app.MapPost("/webhooks/stripe", async (
    HttpRequest request,
    Microsoft.Extensions.Options.IOptions<StripeOptions> stripeOptions,
    IInvoicePdfService pdfService,
    IReceiptEmailService emailService,
    ILogger<Program> logger) =>
{
    // Stripe signs the raw request body, so we have to read it as a plain string
    // ourselves (rather than letting ASP.NET Core deserialize it straight to an
    // object) - any re-serialization would change the bytes and break the
    // signature check below.
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    var signatureHeader = request.Headers["Stripe-Signature"];

    Event stripeEvent;
    try
    {
        // This throws if the signature doesn't match, which happens if the
        // request didn't really come from Stripe (or the webhook secret in
        // config doesn't match the one Stripe is signing with) - that check
        // still runs below.
        //
        // throwOnApiVersionMismatch: false turns off a *separate* check that
        // isn't about security - Stripe.net also normally insists the event's
        // "API version" label matches the exact version this NuGet package
        // was built for, and throws if not. Since Stripe ships new API
        // versions constantly and the account's version won't always line up
        // with whatever Stripe.net version we're on, that strict check isn't
        // practical to keep. The handful of fields this service actually
        // reads (customer email/name, amount, currency, payment status) are
        // part of Checkout Session's stable, long-unchanged shape, so this is
        // a safe trade here - but if this service ever starts reading newer
        // or less common fields off the event, it's worth revisiting.
        stripeEvent = EventUtility.ConstructEvent(
            json,
            signatureHeader,
            stripeOptions.Value.WebhookSecret,
            throwOnApiVersionMismatch: false);
    }
    catch (StripeException ex)
    {
        logger.LogWarning(ex, "Rejected a webhook request with an invalid Stripe signature.");
        return Results.BadRequest();
    }

    if (stripeEvent.Type != EventTypes.CheckoutSessionCompleted)
    {
        // We only care about this one event type right now. Returning 200 (not
        // an error) for event types we're not handling is intentional - Stripe
        // sends many event types to the same endpoint, and a non-2xx response
        // tells Stripe something went wrong and to retry, which isn't true here.
        return Results.Ok();
    }

    if (stripeEvent.Data.Object is not Session session || session.PaymentStatus != "paid")
    {
        return Results.Ok();
    }

    lock (processedEventIdsLock)
    {
        if (!processedEventIds.Add(stripeEvent.Id))
        {
            logger.LogInformation("Skipping already-processed Stripe event {EventId}.", stripeEvent.Id);
            return Results.Ok();
        }
    }

    var customerEmail = session.CustomerDetails?.Email ?? session.CustomerEmail;
    if (string.IsNullOrWhiteSpace(customerEmail))
    {
        logger.LogWarning("Checkout session {SessionId} completed with no customer email - can't send a receipt.", session.Id);
        return Results.Ok();
    }

    // Metadata only exists on sessions created through our own
    // POST /create-checkout-session - a session from the old static Payment
    // Link won't have any of this, so every lookup below falls back to null/
    // empty gracefully rather than throwing. GetValueOrDefault, not indexing,
    // is what makes that safe.
    var metadata = session.Metadata ?? new Dictionary<string, string>();
    string? MetadataOrNull(string key) =>
        string.IsNullOrWhiteSpace(metadata.GetValueOrDefault(key)) ? null : metadata[key];

    var receiptData = new DepositReceiptData
    {
        // A short, readable receipt number derived from the Checkout Session ID
        // rather than a random GUID - easy to cross-reference against the Stripe
        // dashboard if Jada ever needs to look a payment up.
        InvoiceNumber = session.Id.Length >= 8 ? session.Id[^8..].ToUpperInvariant() : session.Id.ToUpperInvariant(),
        PaidAtUtc = DateTime.UtcNow,
        // Prefer our own form's name over Stripe's (Checkout doesn't always
        // collect a name field unless specifically configured to).
        CustomerName = MetadataOrNull("customer_name") ?? session.CustomerDetails?.Name ?? customerEmail,
        CustomerEmail = customerEmail,
        AmountPaid = (session.AmountTotal ?? 0) / 100m, // Stripe amounts are in cents
        Currency = session.Currency ?? "usd",
        Description = "Booking Deposit",
        EventDate = MetadataOrNull("event_date"),
        PerformanceLocation = MetadataOrNull("performance_location"),
        Instructions = MetadataOrNull("instructions"),
        SetupRequirements = MetadataOrNull("setup_requirements"),
    };

    var pdfBytes = pdfService.BuildDepositReceipt(receiptData);
    await emailService.SendReceiptAsync(receiptData.CustomerEmail, receiptData.CustomerName, pdfBytes);

    logger.LogInformation("Sent deposit receipt for Checkout Session {SessionId} to {Email}.", session.Id, customerEmail);

    return Results.Ok();
});

app.Run();