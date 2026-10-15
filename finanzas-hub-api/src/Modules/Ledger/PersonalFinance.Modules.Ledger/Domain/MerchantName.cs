using System.Globalization;
using System.Text;

namespace PersonalFinance.Modules.Ledger.Domain;

/// <summary>
/// Where an expense was made, as a key that groups the ways of writing it: "Wong", "WONG " and "Wóng" are the
/// same shop, and so are "UBER *TRIP 4821" and "Uber trip". A movement without a merchant goes by its description.
/// </summary>
public static class MerchantName
{
    /// <summary>Lowercase, without accents or punctuation, without codes of three digits or more. Empty when nothing is left.</summary>
    public static string KeyOf(string? merchant, string description)
    {
        var source = string.IsNullOrWhiteSpace(merchant) ? description : merchant;
        var text = new StringBuilder();
        foreach (var character in source.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            text.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        var words = text.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !(word.Length >= 3 && word.All(char.IsDigit)));
        return string.Join(' ', words);
    }
}
