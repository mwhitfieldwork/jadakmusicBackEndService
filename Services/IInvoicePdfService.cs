using JadakMusic.ReceiptService.Models;

namespace JadakMusic.ReceiptService.Services;

/// <summary>Builds the branded deposit-receipt PDF. See InvoicePdfService for the "why an interface" note.</summary>
public interface IInvoicePdfService
{
    /// <summary>Returns the finished PDF as raw bytes, ready to email as an attachment.</summary>
    byte[] BuildDepositReceipt(DepositReceiptData data);
}
