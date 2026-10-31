using System.Net.Http.Headers;
using System.Net.Http.Json;
using JadakMusic.ReceiptService.Models;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

namespace JadakMusic.ReceiptService.Services;

/// <summary>
/// WHAT: Emails the deposit receipt PDF to the customer via Brevo's transactional
/// email API.
///
/// WHY: This is behind the IReceiptEmailService interface (rather than called
/// directly) so the webhook handler doesn't need to know *how* the email gets
/// sent - swapping Brevo for another provider later only means writing a new
/// class that implements this interface, not touching the webhook code at all.
///
/// HOW: Brevo's API is a plain HTTPS POST with the API key in a request header
/// (no SDK needed) - see https://developers.brevo.com/reference/sendtransacemail.
/// The PDF is attached as base64-encoded bytes alongside the JSON body.
/// </summary>
public sealed class BrevoEmailService : IReceiptEmailService
{
    private const string SendEmailUrl = "https://api.brevo.com/v3/smtp/email";

    private readonly HttpClient _httpClient;
    private readonly BrevoOptions _options;
    private readonly ILogger<BrevoEmailService> _logger;

    public BrevoEmailService(HttpClient httpClient, IOptions<BrevoOptions> options, ILogger<BrevoEmailService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendReceiptAsync(string toEmail, string toName, byte[] pdfBytes, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SendEmailUrl);
        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new BrevoSendEmailRequest
        {
            Sender = new BrevoContact { Email = _options.SenderEmail, Name = _options.SenderName },
            To = [new BrevoContact { Email = toEmail, Name = toName }],
            Bcc = string.IsNullOrWhiteSpace(_options.NotifyEmail)
            ? null
            : [new BrevoContact { Email = _options.NotifyEmail, Name = "Internal notification" }],
            Subject = "Your Jada K Music deposit receipt",
            HtmlContent =
                $"<p>Hi {toName},</p>" +
                "<p>Thanks for your deposit payment - it's attached as a PDF receipt for your records.</p>" +
                $"<p>We're looking forward to your event!</p>" +
                $"<p>&mdash; {_options.SenderName}</p>",
            Attachment =
            [
                new BrevoAttachment
                {
                    Name = "Jada-K-Music-Deposit-Receipt.pdf",
                    Content = Convert.ToBase64String(pdfBytes),
                },
            ],
        };

        request.Content = JsonContent.Create(payload);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            // We deliberately don't throw here: the payment already succeeded on
            // Stripe's side, so failing loudly would make Stripe retry the whole
            // webhook (re-triggering PDF generation) for a problem that's purely
            // about email delivery. Logging loudly so it's easy to notice and
            // manually resend is the safer failure mode for a receipt email.
            _logger.LogError(
                "Brevo email send failed with status {StatusCode}: {Body}", response.StatusCode, body);
        }
    }

    // Small private DTOs matching Brevo's request shape - kept here rather than in
    // Models/ since nothing outside this class needs to know Brevo's exact JSON format.
    private sealed class BrevoSendEmailRequest
    {
        public required BrevoContact Sender { get; init; }
        public required List<BrevoContact> To { get; init; }
        public required string Subject { get; init; }
        public required string HtmlContent { get; init; }
        public required List<BrevoAttachment> Attachment { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<BrevoContact>? Bcc { get; init; }
    }

    private sealed class BrevoContact
    {
        public required string Email { get; init; }
        public required string Name { get; init; }
    }

    private sealed class BrevoAttachment
    {
        public required string Name { get; init; }
        public required string Content { get; init; }
    }
}
