using System.Text;
using System.Text.RegularExpressions;

namespace steam_game_tool;

public static class GameNames
{
    public static string Normalize(string name) => Regex.Replace(
        name.Normalize(NormalizationForm.FormC).Replace('\u2018', '\'').Replace('\u2019', '\''),
        @"\s+", " ").Trim();
}
