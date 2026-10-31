using JadakMusic.ReceiptService.Models;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JadakMusic.ReceiptService.Services;

/// <summary>
/// WHAT: Builds a one-page PDF deposit receipt, styled to closely match Jada's
/// existing manual invoice template (same accent color, "Name / City" header,
/// Description/Amount table, Payment Notes section, and footer contact block)
/// using the QuestPDF library.
///
/// WHY: Stripe's own receipts/invoices can only be customized with a logo and an
/// accent color - the layout itself is fixed. Generating our own PDF is the only
/// way to actually match her template, as the client asked for.
///
/// HOW: QuestPDF uses a "fluent" API - you describe the page as nested
/// containers (Column, Row, Table...) rather than drawing at x/y coordinates.
/// Each private Compose* method below builds one section of the page;
/// BuildDepositReceipt wires them together into page.Header() / page.Content()
/// / page.Footer().
///
/// NOTE ON SCOPE: this covers the fields we actually have from a Stripe
/// Checkout Session (who paid, how much, when) plus static business/policy
/// text from BusinessInfo. It does NOT include the event date/location/setup
/// details from the Angular booking form that appear on Jada's real invoice
/// template, because that data isn't currently sent to Stripe - see the
/// "Known gap" section of the README for how to close that later. The
/// Payment Notes wording and Zelle/CashApp/Venmo details ARE included,
/// since those are the same on every receipt regardless of booking
/// specifics - they don't require any data Stripe would need to send us.
///
/// NOTE ON LAYOUT: Jada's original template runs a rotated "Invoice #" strip
/// down the left margin. That's left out here - it's a fussier bit of
/// QuestPDF layout, and without NuGet access in my own environment I can't
/// render/preview a PDF to confirm it looks right before handing this to
/// you. Everything else below (colors, fonts, the Description/Amount table,
/// Payment Notes, footer) reuses patterns already proven to compile in this
/// project, so it's lower-risk.
/// </summary>
public sealed class InvoicePdfService : IInvoicePdfService
{
    // Matches the deep magenta/purple accent in Jada's existing invoice template.
    private const string AccentColor = "#8E2A7C";
    private const string MutedTextColor = "#6B6B6B";
    private const string RuleColor = "#D9A9CC"; // lighter tint of AccentColor, for thin separator lines

    private readonly BusinessInfo _business;

    public InvoicePdfService(IOptions<BusinessInfo> businessOptions)
    {
        _business = businessOptions.Value;

        // QuestPDF requires picking a license type once at startup before any PDF
        // is generated. This is also set in Program.cs - it's safe/idempotent to
        // set it again here as a defensive guard in case this service is ever
        // used from a different entry point (e.g. a unit test).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] BuildDepositReceipt(DepositReceiptData data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeHeader(c, data));
                page.Content().Element(c => ComposeContent(c, data));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private void ComposeHeader(IContainer container, DepositReceiptData data)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(_business.OwnerName).FontSize(26).Bold().FontColor(AccentColor);
                    left.Item().Text(_business.CityState).FontSize(11).FontColor(MutedTextColor);
                });

                row.ConstantItem(170).Column(right =>
                {
                    right.Item().AlignRight().Text($"Receipt #{data.InvoiceNumber}").Bold().FontSize(12);
                    right.Item().AlignRight().Text(data.PaidAtUtc.ToString("MMMM d, yyyy")).FontColor(MutedTextColor);
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(AccentColor);
        });
    }

    private void ComposeContent(IContainer container, DepositReceiptData data)
    {
        container.PaddingTop(20).Column(column =>
        {
            column.Spacing(16);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Billed To").Bold().FontColor(AccentColor);
                    c.Item().Text(data.CustomerName);
                    c.Item().Text(data.CustomerEmail);
                });

                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignRight().Text("Payment Status").Bold().FontColor(AccentColor);
                    c.Item().AlignRight().Text("Paid in full");
                });
                // Event date/location/notes only show up when this Checkout Session
                // came from our own form (see Program.cs's /create-checkout-session) -
                // a session from the old static Payment Link has none of this, so
                // these sections just don't render rather than showing blank fields.
                if (!string.IsNullOrWhiteSpace(data.EventDate) || !string.IsNullOrWhiteSpace(data.PerformanceLocation))
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Event Date").Bold().FontColor(AccentColor);
                            c.Item().Text(data.EventDate ?? "-");
                        });

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Performance Location").Bold().FontColor(AccentColor);
                            c.Item().Text(data.PerformanceLocation ?? "-");
                        });
                    });
                }

                if (!string.IsNullOrWhiteSpace(data.Instructions))
                {
                    column.Item().Column(c =>
                    {
                        c.Item().Text("Instructions").Bold().FontColor(AccentColor);
                        c.Item().Text(data.Instructions!);
                    });
                }

                if (!string.IsNullOrWhiteSpace(data.SetupRequirements))
                {
                    column.Item().Column(c =>
                    {
                        c.Item().Text("Setup Requirements").Bold().FontColor(AccentColor);
                        c.Item().Text(data.SetupRequirements!);
                    });
                }
            });

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(1);
                });

                table.Header(header =>
                {
                    header.Cell().Background(AccentColor).Padding(6)
                        .Text("Description").FontColor(Colors.White).Bold();
                    header.Cell().Background(AccentColor).Padding(6).AlignRight()
                        .Text("Amount").FontColor(Colors.White).Bold();
                });

                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                    .Text(data.Description);
                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight()
                    .Text($"{FormatAmount(data.AmountPaid, data.Currency)}");
            });

            column.Item().AlignRight().Text(text =>
            {
                text.Span("Total Paid: ").SemiBold();
                text.Span(FormatAmount(data.AmountPaid, data.Currency)).Bold().FontColor(AccentColor);
            });

            column.Item().Element(ComposePaymentNotes);

            column.Item().Element(ComposeRemainingBalanceMethods);

            column.Item().PaddingTop(4).Text(
                "This confirms your deposit payment has been received. Thank you for booking with " +
                $"{_business.OwnerName} - we're looking forward to your event!");
        });
    }

    // Mirrors the "PAYMENT NOTES" section of Jada's existing invoice template -
    // same boilerplate policy wording, each line separated by a thin rule, same
    // as her original. Sourced from BusinessInfo since it's identical on every
    // receipt, not something that comes from Stripe.
    private void ComposePaymentNotes(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("PAYMENT NOTES").Bold().FontSize(10).FontColor(AccentColor);
            column.Item().PaddingTop(4).LineHorizontal(1).LineColor(RuleColor);

            foreach (var note in new[]
                     {
                         "This deposit is non-refundable.",
                         _business.RemainingBalanceNote,
                         _business.RescheduleNote,
                     })
            {
                column.Item().PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                    .Text(note).FontSize(9);
            }
        });
    }

    // Mirrors the "Payments can be made through..." list on Jada's original
    // template. Labeled explicitly as being for the remaining balance - this
    // deposit was already paid by card through Stripe, so repeating Zelle/
    // CashApp/Venmo right under "Total Paid" would read as confusing.
    private void ComposeRemainingBalanceMethods(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("Paying your remaining balance?").Bold().FontSize(10).FontColor(AccentColor);
            column.Item().PaddingTop(2).Text($"Zelle: {_business.Zelle}").FontSize(9);
            column.Item().Text($"CashApp: {_business.CashApp}").FontSize(9);
            column.Item().Text($"Venmo: {_business.Venmo}").FontSize(9);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            column.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"Phone: {_business.Phone}").FontColor(AccentColor).FontSize(9);
                    c.Item().Text($"Email: {_business.Email}").FontColor(AccentColor).FontSize(9);
                    c.Item().Text($"Instagram: {_business.Instagram}").FontColor(AccentColor).FontSize(9);
                    c.Item().Text($"YouTube: {_business.YouTube}").FontColor(AccentColor).FontSize(9);
                });

                row.RelativeItem().AlignRight().AlignMiddle()
                    .Text("Thank you for your business!").Italic().FontColor(MutedTextColor);
            });
        });
    }

    private static string FormatAmount(decimal amount, string currency) =>
        currency.Equals("usd", StringComparison.OrdinalIgnoreCase)
            ? amount.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("en-US"))
            : $"{amount:0.00} {currency.ToUpperInvariant()}";
}