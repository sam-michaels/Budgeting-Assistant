using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace BudgetAssistant.Web.Services;

public record ParsedRow(DateOnly Date, string Description, decimal Amount);
public record CsvParseResult(List<ParsedRow> Rows, List<string> Errors);

/// <summary>
/// Reads a bank CSV export. Banks disagree about column names and about how to signal a
/// debit, so this accepts the common shapes rather than demanding one exact format:
/// a single signed Amount, or separate Debit/Credit columns.
/// </summary>
public static class CsvTransactionReader
{
    static readonly string[] DateNames   = ["date", "transaction date", "posted date", "post date"];
    static readonly string[] DescNames   = ["description", "merchant", "name", "payee", "memo", "details"];
    static readonly string[] AmountNames = ["amount", "value"];
    static readonly string[] DebitNames  = ["debit", "withdrawal", "money out"];
    static readonly string[] CreditNames = ["credit", "deposit", "money in"];

    public const int MaxRows = 5000;

    public static CsvParseResult Read(TextReader reader)
    {
        var rows = new List<ParsedRow>();
        var errors = new List<string>();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
        };

        using var csv = new CsvReader(reader, config);
        if (!csv.Read() || !csv.ReadHeader())
            return new CsvParseResult(rows, ["The file has no header row."]);

        // Match case-insensitively but address columns by INDEX afterwards: GetField(name)
        // looks up the header as written ("Date"), so passing a lowercased key finds nothing
        // and silently yields null for every field.
        var headers = (csv.HeaderRecord ?? []).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        int? Find(string[] names)
        {
            var i = Array.FindIndex(headers, h => names.Contains(h));
            return i < 0 ? null : i;
        }

        var dateCol = Find(DateNames);
        var descCol = Find(DescNames);
        var amountCol = Find(AmountNames);
        var debitCol = Find(DebitNames);
        var creditCol = Find(CreditNames);

        if (dateCol is null) errors.Add($"No date column. Looked for: {string.Join(", ", DateNames)}.");
        if (descCol is null) errors.Add($"No description column. Looked for: {string.Join(", ", DescNames)}.");
        if (amountCol is null && debitCol is null && creditCol is null)
            errors.Add("No amount column. Expected 'Amount', or a 'Debit'/'Credit' pair.");
        if (errors.Count > 0) return new CsvParseResult(rows, errors);

        var line = 1;
        while (csv.Read())
        {
            line++;
            if (rows.Count >= MaxRows)
            {
                errors.Add($"Stopped at {MaxRows} rows. Split the file and import the rest separately.");
                break;
            }

            var rawDate = csv.GetField(dateCol!.Value);
            var description = csv.GetField(descCol!.Value)?.Trim();

            if (string.IsNullOrWhiteSpace(rawDate) && string.IsNullOrWhiteSpace(description)) continue;

            if (!TryParseDate(rawDate, out var date))
            {
                errors.Add($"Row {line}: could not read the date '{rawDate}'.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(description))
            {
                errors.Add($"Row {line}: the description is empty.");
                continue;
            }
            if (!TryParseAmount(csv, amountCol, debitCol, creditCol, out var amount))
            {
                errors.Add($"Row {line}: could not read an amount.");
                continue;
            }

            rows.Add(new ParsedRow(date, description!, amount));
        }

        return new CsvParseResult(rows, errors);
    }

    static bool TryParseDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        // Try the invariant/ISO reading first, then the user's own locale, so both
        // 2026-03-14 and 14/03/2026 work depending on where the export came from.
        return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
    }

    static bool TryParseAmount(CsvReader csv, int? amountCol, int? debitCol, int? creditCol, out decimal amount)
    {
        amount = 0m;
        if (amountCol is not null && TryMoney(csv.GetField(amountCol.Value), out amount)) return true;

        // Separate columns: a debit is money leaving, so it is stored negative even though
        // the column itself carries a positive number.
        if (debitCol is not null && TryMoney(csv.GetField(debitCol.Value), out var debit) && debit != 0)
        {
            amount = -Math.Abs(debit);
            return true;
        }
        if (creditCol is not null && TryMoney(csv.GetField(creditCol.Value), out var credit) && credit != 0)
        {
            amount = Math.Abs(credit);
            return true;
        }
        return false;
    }

    static bool TryMoney(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim().Replace("$", "").Replace(",", "");
        // Accounting negatives: (12.34) means -12.34
        var parenthesised = raw.StartsWith('(') && raw.EndsWith(')');
        if (parenthesised) raw = raw[1..^1];
        if (!decimal.TryParse(raw, NumberStyles.Currency | NumberStyles.AllowLeadingSign,
                              CultureInfo.InvariantCulture, out value)) return false;
        if (parenthesised) value = -value;
        return true;
    }
}
