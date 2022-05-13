namespace Bot.Extensions;

public static class StringExtensions
{
    public static bool EqualsCI(this string current, string other)
    {
        return current.Equals(other, StringComparison.InvariantCultureIgnoreCase);
    }
}