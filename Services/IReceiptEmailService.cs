namespace JadakMusic.ReceiptService.Services;

/// <summary>Sends the generated receipt PDF to the customer. See BrevoEmailService for the "why an interface" note.</summary>
public interface IReceiptEmailService
{
    Task SendReceiptAsync(string toEmail, string toName, byte[] pdfBytes, CancellationToken cancellationToken = default);
}
