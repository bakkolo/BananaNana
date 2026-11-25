namespace BananaNana;

public static class LinkUrl
{
    // Accepts http, https and mailto only (no javascript:, file: etc. in pasted snippets).
    // A bare domain such as "example.com/page" gets https://; an address with @ and no scheme gets mailto:.
    public static Uri? TryNormalize(string input)
    {
        var text = input.Trim();
        if (text.Length == 0 || text.Any(char.IsWhiteSpace)) return null;
        if (!text.Contains(':'))
            text = text.Contains('@') && !text.Contains('/') ? "mailto:" + text : "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme switch
        {
            "http" or "https" when uri.Host.Contains('.') || uri.IsLoopback => uri,
            "mailto" when uri.UserInfo.Length > 0 || text.Contains('@') => uri,
            _ => null,
        };
    }
}
