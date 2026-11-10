using System.Text.RegularExpressions;

namespace NetBooster.Logic;

public static class PowerPlanLogic
{
    private static readonly Regex GuidPattern = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    public static bool TryParseActiveScheme(string output, out Guid scheme)
    {
        var match = GuidPattern.Match(output);
        return match.Success && Guid.TryParse(match.Value, out scheme);
    }
}
