using Avalonia.Data.Converters;
using Avalonia.Media;

namespace steam_game_tool
{
    /// <summary>XAML에서 x:Static 으로 참조하는 값 변환기 모음.</summary>
    public static class Converters
    {
        /// <summary>태그 문자열 → 배지 배경색.</summary>
        public static readonly IValueConverter TagBrush =
            new FuncValueConverter<string?, IBrush>(tag => tag switch
            {
                TagNames.UnityMono        => new SolidColorBrush(Color.Parse("#2563EB")), // 파랑
                TagNames.UnityIl2Cpp      => new SolidColorBrush(Color.Parse("#E65100")), // 주황
                TagNames.MonoBleedingEdge => new SolidColorBrush(Color.Parse("#6E4FE0")), // 보라
                TagNames.Managed          => new SolidColorBrush(Color.Parse("#1E88E5")), // 파랑
                TagNames.Nested           => new SolidColorBrush(Color.Parse("#43A047")), // 초록
                _                         => new SolidColorBrush(Color.Parse("#9AA0A6")), // 회색
            });
    }
}
