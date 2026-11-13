using System.Text;
using System.Text.RegularExpressions;

namespace steam_game_tool;

public static partial class GameNames
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Normalize(string name) => Whitespace().Replace(
        name.Normalize(NormalizationForm.FormC).Replace('‘', '\'').Replace('’', '\''),
        " ").Trim();
}
