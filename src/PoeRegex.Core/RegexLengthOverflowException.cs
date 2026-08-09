namespace PoeRegex.Core;

public sealed class RegexLengthOverflowException : Exception
{
    public int RequestedLength { get; }
    public int Limit { get; }

    public RegexLengthOverflowException(int requestedLength, int limit)
        : base($"Generated query is {requestedLength} characters, exceeding the {limit} character limit for this game.")
    {
        RequestedLength = requestedLength;
        Limit = limit;
    }
}
