namespace GrpcClients;

internal static class StringExt
{
    public static string ToPath(this string s, string replacement = "-")
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        string[] temp = s.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(replacement, temp);
    }
}
