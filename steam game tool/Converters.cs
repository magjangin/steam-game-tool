using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace steam_game_tool
{
    /// <summary>XAML에서 x:Static 으로 참조하는 값 변환기 모음.</summary>
    public static class Converters
    {
        // 배지마다 새 브러시를 만들지 않도록 한 번만 만든다. 불변 브러시라 어느 스레드에서 써도 된다.
        private static readonly IBrush UnityMonoBrush = new ImmutableSolidColorBrush(Color.Parse("#2563EB"));        // 파랑
        private static readonly IBrush UnityIl2CppBrush = new ImmutableSolidColorBrush(Color.Parse("#E65100"));      // 주황
        private static readonly IBrush MonoBleedingEdgeBrush = new ImmutableSolidColorBrush(Color.Parse("#6E4FE0")); // 보라
        private static readonly IBrush ManagedBrush = new ImmutableSolidColorBrush(Color.Parse("#1E88E5"));          // 파랑
        private static readonly IBrush NestedBrush = new ImmutableSolidColorBrush(Color.Parse("#43A047"));           // 초록
        private static readonly IBrush OtherBrush = new ImmutableSolidColorBrush(Color.Parse("#9AA0A6"));            // 회색

        /// <summary>태그 문자열 → 배지 배경색.</summary>
        public static readonly IValueConverter TagBrush =
            new FuncValueConverter<string?, IBrush>(tag => tag switch
            {
                TagNames.UnityMono        => UnityMonoBrush,
                TagNames.UnityIl2Cpp      => UnityIl2CppBrush,
                TagNames.MonoBleedingEdge => MonoBleedingEdgeBrush,
                TagNames.Managed          => ManagedBrush,
                TagNames.Nested           => NestedBrush,
                _                         => OtherBrush,
            });
    }
}
