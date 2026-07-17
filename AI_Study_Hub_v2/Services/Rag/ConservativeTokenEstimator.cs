using System.Text;

namespace AI_Study_Hub_v2.Services.Rag;

public interface ITokenEstimator
{
    int Estimate(string text);
}

/// <summary>
/// A deterministic, dependency-free conservative estimate for multilingual embedding budgets.
/// </summary>
public sealed class ConservativeTokenEstimator : ITokenEstimator
{
    public int Estimate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var total = 0;
        var latinRunLength = 0;

        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                total += EstimateLatinRun(latinRunLength);
                latinRunLength = 0;
                continue;
            }

            if (IsCjkLike(rune))
            {
                total += EstimateLatinRun(latinRunLength) + 1;
                latinRunLength = 0;
                continue;
            }

            if (IsAsciiLetterOrDigit(rune))
            {
                latinRunLength++;
                continue;
            }

            total += EstimateLatinRun(latinRunLength) + 1;
            latinRunLength = 0;
        }

        total += EstimateLatinRun(latinRunLength);
        return string.IsNullOrWhiteSpace(normalized) ? 0 : Math.Max(1, total);
    }

    private static int EstimateLatinRun(int scalarLength) => scalarLength == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(scalarLength / 3d));

    private static bool IsCjkLike(Rune rune) => rune.Value is
        >= 0x2E80 and <= 0x9FFF or
        >= 0xAC00 and <= 0xD7AF or
        >= 0xF900 and <= 0xFAFF or
        >= 0x3040 and <= 0x30FF or
        >= 0xFF66 and <= 0xFF9D or
        >= 0x20000 and <= 0x2FA1F;

    private static bool IsAsciiLetterOrDigit(Rune rune) => rune.Value is
        >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
