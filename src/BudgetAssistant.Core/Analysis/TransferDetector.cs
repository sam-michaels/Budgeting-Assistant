using System.Text.RegularExpressions;

namespace BudgetAssistant.Core.Analysis;

/// <summary>
/// Recognizes money moving between the owner's own accounts.
///
/// A transfer is neither income nor spending. A $600 sweep into savings booked as income
/// overstates what the month earned, and its matching outflow booked as spending
/// overstates what the month cost — the pair nets to zero while inflating both sides of
/// every KPI on the dashboard.
///
/// Deliberately a rule, unlike everything else in this namespace. A transfer description
/// is bank boilerplate rather than a merchant name, so there is no merchant identity for
/// an embedding to resolve; and a misread transfer moves a headline number rather than one
/// row, which is the case for a decision you can read off the screen instead of a vote.
/// </summary>
public static partial class TransferDetector
{
    // Word-boundaried: TRANSFERWISE is a merchant, not a transfer.
    // "PAYMENT THANK YOU" and its variants are how issuers write a credit-card payment,
    // which moves money between the owner's own accounts like any other transfer.
    [GeneratedRegex(@"\b(TRANSFERS?|XFER|PAYMENT THANK YOU|CARDMEMBER PAYMENT)\b")]
    private static partial Regex TransferPhrase();

    /// <param name="normalizedMerchant">Output of <see cref="MerchantNormalizer.Normalize"/>:
    /// uppercase, with digits and punctuation already stripped.</param>
    public static bool IsTransfer(string normalizedMerchant) =>
        !string.IsNullOrWhiteSpace(normalizedMerchant) && TransferPhrase().IsMatch(normalizedMerchant);
}
