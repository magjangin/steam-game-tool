using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace steam_game_tool
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<SteamGame> _view = new();
        private ScanResult? _last;

        public MainWindow()
        {
            InitializeComponent();
            GameList.ItemsSource = _view;
        }

        /// <summary>
        /// 선택된 필터. 값은 ComboBoxItem 의 Tag 에서 읽는다.
        /// 항목 순서에 기대지 않으므로 XAML 에서 순서를 바꿔도 안전하다.
        /// </summary>
        private Marker SelectedMarker =>
            (FilterCombo.SelectedItem as ComboBoxItem)?.Tag is Marker m ? m : Marker.Any;

        private async void OnScanClick(object? sender, RoutedEventArgs e)
        {
            // 입력된 폴더가 있으면 그것들(세미콜론·줄 바꿈으로 구분), 비어 있으면 자동 감지.
            // 입력한 폴더가 하나도 없으면 자동 감지로 넘어가지 않는다. 한 폴더만 보려던 사람에게 전체 결과를 주지 않기 위해서다.
            var typed = SteamLibraries.SplitRootList(RootBox.Text);
            List<string>? roots = null;
            var missingRoots = new List<string>();
            if (typed.Count > 0)
            {
                var existing = typed.Where(Directory.Exists).ToList();
                missingRoots = typed.Except(existing).ToList();
                roots = existing;
                if (existing.Count == 0)
                {
                    StatusText.Text = "입력한 폴더가 없습니다: " + string.Join("  |  ", missingRoots) +
                                      " — 자동 감지로 스캔하려면 입력란을 비우거나 「폴더 ▸ 자동 감지」를 누르세요.";
                    return;
                }
            }

            ScanButton.IsEnabled = false;
            Spinner.IsVisible = true;
            SummaryText.Text = "스캔 중...";
            StatusText.Text = "";
            _view.Clear();
            UpdateSelectAll();

            var previous = _last;
            // 진행 메시지가 완료 메시지보다 늦게 처리될 수 있다. 끝난 뒤 도착한 것은 버린다.
            var scanning = true;
            try
            {
                var (result, inventory) = await Task.Run(() =>
                {
                    var scanned = SteamScanner.Scan(roots, msg =>
                        Dispatcher.UIThread.Post(() => { if (scanning) StatusText.Text = msg; }));
                    // 대표 플레이어는 처음 읽을 때 계산된다(동률이면 데이터 폴더 크기를 전부 더함). 여기서 끝내 UI 스레드가 멈추지 않게 한다.
                    GameClassification.Validate(scanned.Games);
                    if (scanned.Roots.Count == 0) return (scanned, ScanInventory.InventoryCheck.None);

                    var check = ScanInventory.CheckPersistent(scanned);
                    if (previous is not null && previous.Roots.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(scanned.Roots))
                        check = check.Merge(ScanInventory.Check(ScanInventory.BaselineOf(previous), ScanInventory.BaselineOf(scanned)));
                    return (scanned, check);
                });
                scanning = false;

                if (_last is not null)
                {
                    // 설치 후 다시 스캔해도 사용자가 풀어 둔 체크는 그대로 둔다.
                    var excluded = _last.Games.Where(g => !g.InstallChecked)
                        .Select(g => g.InstallDir).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var g in result.Games)
                        if (excluded.Contains(g.InstallDir)) g.InstallChecked = false;
                    foreach (var g in _last.Games) g.PropertyChanged -= OnInstallCheckedChanged;
                }
                // 목록에서 하나씩 체크를 바꿔도 「MelonLoader 설치 대상」 체크박스와 개수가 따라가게 한다.
                foreach (var g in result.Games) g.PropertyChanged += OnInstallCheckedChanged;
                _last = result;
                ApplyFilter();

                if (result.Roots.Count == 0)
                {
                    SummaryText.Text = "스캔할 폴더를 찾지 못했습니다.";
                    StatusText.Text = "스캔 폴더에 steamapps\\common 또는 게임 폴더 경로를 지정해 보세요.";
                }
                else
                {
                    SummaryText.Text = BuildSummary(result);
                    StatusText.Text = BuildStatus(result, inventory, missingRoots);
                }
            }
            catch (Exception ex)
            {
                scanning = false;
                SummaryText.Text = "스캔 중 오류가 발생했습니다.";
                StatusText.Text = ex.Message;
            }
            finally
            {
                Spinner.IsVisible = false;
                ScanButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 마커 집계 요약. 개수는 모두 집계 대상(<see cref="ScanResult.CountedGames"/>) 기준이며,
        /// 사운드트랙·소프트웨어·도구는 세지 않는다. 앞의 전체 개수도 같은 기준이라야 뒤의 개수와 분모가 맞는다.
        /// </summary>
        private static string BuildSummary(ScanResult result)
        {
            var scope = result.HasTypeInfo
                ? $"게임 {result.GameCount}개"
                : $"폴더 {result.GameCount}개";

            return $"{scope} — " +
                   $"Unity Mono {result.UnityMonoCount} · " +
                   $"Unity IL2CPP {result.UnityIl2CppCount} · " +
                   $"MonoBleedingEdge {result.MonoBleedingEdgeCount} · " +
                   $"구형 레거시(Data/Mono) {result.MonoLegacyCount} · " +
                   $"중첩(Unity) {result.NestedUnityCount} · " +
                   $"런타임 미확정 {result.CountedGames.Count(g => g.IsRuntimeUnresolved)} · 백엔드 미확정 {result.CountedGames.Count(g => g.IsBackendUnresolved)}";
        }

        /// <summary>
        /// 이전 스캔 대비 누락, 게임이 아닌 항목, 경고를 알리는 상태 줄.
        /// 「제외」의 개수와 요약 줄의 게임 수를 더하면 스캔한 폴더 수가 된다. 데모는 게임으로 세므로 「제외」에 넣지 않는다.
        /// </summary>
        private static string BuildStatus(ScanResult result, ScanInventory.InventoryCheck inventory,
            IReadOnlyCollection<string> missingRoots)
        {
            var parts = new List<string>();

            // 목록에서 사라진 게임을 가장 먼저 알린다. 폴더가 디스크에 남아 있는 동안 스캔할 때마다 다시 나온다.
            if (inventory.LostUnity.Count > 0)
                parts.Add($"⚠ 전에 Unity 로 분류됐는데 지금은 아닌 폴더 {inventory.LostUnity.Count}개: {Abbreviate(inventory.LostUnity)}" +
                          " (게임을 지웠다면 남은 폴더를 지우면 사라집니다)");
            if (inventory.MissingFolders.Count > 0)
                parts.Add($"⚠ 디스크에는 있는데 이번 스캔에서 빠진 폴더 {inventory.MissingFolders.Count}개: {Abbreviate(inventory.MissingFolders)}");
            if (missingRoots.Count > 0)
                parts.Add("⚠ 입력한 폴더 중 없는 것은 건너뜀: " + string.Join("  |  ", missingRoots));

            if (result.HasTypeInfo)
            {
                var others = new List<string>();
                void Add(string label, int n)
                {
                    if (n > 0) others.Add($"{label} {n}");
                }
                Add("사운드트랙", result.CountOf(SteamAppType.Music));
                Add("소프트웨어", result.CountOf(SteamAppType.Application));
                Add("도구", result.CountOf(SteamAppType.Tool));
                Add("영상", result.CountOf(SteamAppType.Video));
                Add("기타", result.CountOf(SteamAppType.Config) + result.CountOf(SteamAppType.Other));
                if (result.OrphanCount > 0) others.Add($"미설치 잔여 폴더 {result.OrphanCount}");

                if (others.Count > 0) parts.Add("제외: " + string.Join(" · ", others));
            }
            else
            {
                parts.Add("Steam 앱 목록을 읽지 못해 종류를 구분하지 못했습니다");
            }

            parts.Add("스캔 폴더: " + string.Join("  |  ", result.Roots));

            if (result.Warnings.Count > 0)
                parts.Add($"⚠ 읽지 못한 항목 {result.Warnings.Count}개 — 결과가 일부 누락될 수 있습니다");

            return string.Join("   ·   ", parts);
        }

        /// <summary>경로를 세 개까지 적고 나머지는 개수로 줄인다.</summary>
        private static string Abbreviate(IReadOnlyList<string> paths) =>
            string.Join(", ", paths.Take(3)) + (paths.Count > 3 ? $" 외 {paths.Count - 3}개" : "");

        private async void OnBrowseClick(object? sender, RoutedEventArgs e)
        {
            // async void 이므로 예외가 새어 나가면 프로세스가 죽는다. 반드시 여기서 잡는다.
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "steamapps\\common 폴더 선택",
                    AllowMultiple = false,
                });
                var path = folders.FirstOrDefault()?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) RootBox.Text = path;
            }
            catch (Exception ex)
            {
                StatusText.Text = "폴더 선택 실패: " + ex.Message;
            }
        }

        private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "추가할 폴더(라이브러리 또는 단일 게임 보존본) 선택",
                    AllowMultiple = true,
                });
                var paths = folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
                if (paths.Count > 0)
                {
                    // 스캔할 때와 같은 규칙으로 나눠야 입력란의 경로가 어긋나지 않는다.
                    var existing = SteamLibraries.SplitRootList(RootBox.Text);
                    existing.AddRange(paths.Where(p => !existing.Contains(p, StringComparer.OrdinalIgnoreCase)));
                    RootBox.Text = string.Join("; ", existing);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "폴더 추가 실패: " + ex.Message;
            }
        }

        /// <summary>
        /// 자동 감지가 무엇을 찾는지 미리 보여 준다. 스캔은 하지 않는다.
        /// 모든 드라이브를 훑으므로 백그라운드에서 돌린다 — 네트워크 드라이브나 잠든 외장 디스크가 있으면 오래 걸릴 수 있다.
        /// </summary>
        private async void OnAutoClick(object? sender, RoutedEventArgs e)
        {
            RootBox.Text = "";
            StatusText.Text = "Steam 라이브러리를 찾는 중...";
            try
            {
                var found = await Task.Run(SteamScanner.FindCommonFolders);
                StatusText.Text = found.Count > 0
                    ? "자동 감지된 폴더: " + string.Join("  |  ", found)
                    : "자동 감지 실패 — 폴더를 직접 지정하세요.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "자동 감지 실패: " + ex.Message;
            }
        }

        private async void OnExportClick(object? sender, RoutedEventArgs e)
        {
            if (_view.Count == 0)
            {
                StatusText.Text = "저장할 목록이 없습니다. 먼저 스캔하세요.";
                return;
            }

            var marker = SelectedMarker;
            var definition = ExportLists.For(marker);
            // async void 이므로 저장 창 호출까지 try 안에 둔다. 예외가 새어 나가면 프로세스가 죽는다.
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "목록을 TXT로 저장",
                    SuggestedFileName = definition.FileName,
                    DefaultExtension = "txt",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("텍스트 파일") { Patterns = new[] { "*.txt" } },
                    },
                });
                if (file is null) return;

                var text = ExportLists.Generate(_last!, marker, GamesOnlyCheck.IsChecked == true);
                await using (var stream = await file.OpenWriteAsync())
                {
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    await writer.WriteAsync(text);
                }
                StatusText.Text = $"저장 완료: {file.TryGetLocalPath()} ({_view.Count}개)";
            }
            catch (Exception ex)
            {
                StatusText.Text = "저장 실패: " + ex.Message;
            }
        }

        private async void OnExportAllClick(object? sender, RoutedEventArgs e)
        {
            var result = _last;
            if (result is null)
            {
                StatusText.Text = "저장할 목록이 없습니다. 먼저 스캔하세요.";
                return;
            }

            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "6개 목록을 저장할 폴더 선택",
                    AllowMultiple = false,
                });
                var folder = folders.FirstOrDefault()?.TryGetLocalPath();
                if (string.IsNullOrEmpty(folder)) return;

                // 보기 콤보박스 항목을 그대로 따른다. 화면 목록과 저장 목록이 어긋나지 않게 한다.
                var markers = FilterCombo.Items.OfType<ComboBoxItem>().Select(i => i.Tag).OfType<Marker>().ToArray();
                var gamesOnly = GamesOnlyCheck.IsChecked == true;
                var written = await Task.Run(() => ExportLists.WriteAll(result, markers, gamesOnly, folder));
                StatusText.Text = $"저장 완료: {folder} (파일 {written.Count}개)";
            }
            catch (Exception ex)
            {
                StatusText.Text = "저장 실패: " + ex.Message;
            }
        }

        /// <summary>zip 선택 창을 지난번 폴더에서 다시 열기 위한 값. 파일로 저장하지 않는다.</summary>
        private IStorageFolder? _melonZipFolder;

        /// <summary>
        /// 자동 설치 대상 범위: 보기와 상관없이 전체 Unity 게임(「게임만」은 적용).
        /// 게임마다 규칙이 MBE·IL2CPP 만 고르고 구형 레거시·직접 지운 게임은 건너뛰므로 보기로 한 번 더 거를 필요가 없다.
        /// 보기에 묶여 있을 때 MonoBleedingEdge 보기에서 눌러 IL2CPP 게임(Gestalt 등 79개)이 빠진 일이 있었다(2026-09-21).
        /// </summary>
        private List<SteamGame> AutoInstallScope() =>
            _last is null ? [] : SteamScanner.Filter(_last, Marker.Any, GamesOnlyCheck.IsChecked == true).ToList();

        /// <summary>범위 안에서 체크된 게임이 곧 대상이다. 「zip 직접 선택…」은 지금 보기 목록(_view)을 넘긴다.</summary>
        private List<SteamGame>? MelonCandidates(IReadOnlyCollection<SteamGame> scope, out int uncheckedCount)
        {
            var games = scope.Where(g => g.InstallChecked).ToList();
            uncheckedCount = scope.Count - games.Count;
            if (scope.Count == 0)
            {
                StatusText.Text = "설치할 목록이 없습니다. 먼저 스캔하세요.";
                return null;
            }
            if (games.Count == 0)
            {
                StatusText.Text = "체크된 게임이 없습니다.";
                return null;
            }
            return games;
        }

        private void SetMelonBusy(bool busy)
        {
            MelonMenuButton.IsEnabled = !busy;
            ScanButton.IsEnabled = !busy;
            Spinner.IsVisible = busy;
        }

        /// <summary>GitHub 에서 게임마다 버전 규칙에 맞는 릴리스를 받아 설치한다.</summary>
        private async void OnMelonAutoClick(object? sender, RoutedEventArgs e)
        {
            var games = MelonCandidates(AutoInstallScope(), out var uncheckedCount);
            if (games is null) return;

            SetMelonBusy(true);
            try
            {
                var replace = MelonReplaceCheck.IsChecked == true;
                StatusText.Text = "설치 대상을 살피는 중...";
                var targets = await Task.Run(() => games.Select(MelonLoaderInstaller.Inspect).ToList());

                StatusText.Text = "GitHub 에서 MelonLoader 릴리스 목록을 받는 중...";
                List<MelonRelease> releases;
                try { releases = await MelonReleaseClient.FetchAsync(); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    StatusText.Text = $"GitHub 에 접속하지 못했습니다 ({ex.Message}). 「MelonLoader 설치 ▸ zip 직접 선택…」으로 설치할 수 있습니다.";
                    return;
                }

                var packages = new List<MelonPackage>();
                foreach (var (release, arch) in MelonLoaderInstaller.RequiredDownloads(targets, releases, replace))
                {
                    var asset = release.Zips[arch];
                    StatusText.Text = $"{release.Tag} {asset.Name} 준비 중 ({asset.Size / 1048576.0:0.0} MB, 받아 둔 것이 있으면 다시 쓰기)...";
                    var zip = await Task.Run(() => MelonReleaseClient.GetZipAsync(release, arch));
                    packages.Add(await Task.Run(() => MelonLoaderInstaller.OpenPackage(zip, release.Version)));
                }

                var plans = await Task.Run(() => MelonLoaderInstaller.PlanAuto(targets, releases, packages, replace));
                var scope = "대상 범위: 보기와 상관없이 전체 Unity 게임" + (GamesOnlyCheck.IsChecked == true ? "(게임만)" : "") + " 중 체크된 게임\n\n";
                await ConfirmAndInstallAsync(plans, scope + MelonLoaderInstaller.Summarize(plans, packages, uncheckedCount, releases), uncheckedCount);
            }
            catch (Exception ex)
            {
                StatusText.Text = "MelonLoader 설치 실패: " + ex.Message;
            }
            finally
            {
                SetMelonBusy(false);
            }
        }

        /// <summary>가지고 있는 zip 을 체크된 게임 모두에 설치한다. 버전 규칙을 쓰지 않고 네트워크도 쓰지 않는다.</summary>
        private async void OnMelonZipClick(object? sender, RoutedEventArgs e)
        {
            var games = MelonCandidates(_view, out var uncheckedCount);
            if (games is null) return;

            SetMelonBusy(true);
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "MelonLoader zip 선택 (x64·x86 을 함께 골라도 됩니다)",
                    AllowMultiple = true,
                    SuggestedStartLocation = _melonZipFolder,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("MelonLoader zip") { Patterns = new[] { "*.zip" } },
                    },
                });
                var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
                if (paths.Count == 0) return;
                _melonZipFolder = await files[0].GetParentAsync();

                var replace = MelonReplaceCheck.IsChecked == true;
                StatusText.Text = "설치 대상을 확인하는 중...";
                var (packages, plans) = await Task.Run(() =>
                {
                    var opened = MelonLoaderInstaller.OpenPackages(paths);
                    return (opened, MelonLoaderInstaller.Plan(games, opened, replace));
                });
                await ConfirmAndInstallAsync(plans, MelonLoaderInstaller.Summarize(plans, packages, uncheckedCount), uncheckedCount);
            }
            catch (Exception ex)
            {
                StatusText.Text = "MelonLoader 설치 실패: " + ex.Message;
            }
            finally
            {
                SetMelonBusy(false);
            }
        }

        private async Task ConfirmAndInstallAsync(List<MelonPlan> plans, string report, int uncheckedCount)
        {
            Spinner.IsVisible = false;
            var ready = plans.Count(p => p.Skip == MelonSkip.None);
            if (ready == 0)
            {
                StatusText.Text = "설치할 게임이 없습니다 — 체크된 게임이 모두 건너뜀 대상입니다.";
                await TextDialog.ShowAsync(this, "MelonLoader 설치", report);
                return;
            }
            if (!await TextDialog.ShowAsync(this, "MelonLoader 설치", report, $"{ready}개 설치", "취소"))
            {
                StatusText.Text = "MelonLoader 설치를 취소했습니다.";
                return;
            }

            Spinner.IsVisible = true;
            // 진행 메시지가 완료 메시지보다 늦게 처리될 수 있다. 끝난 뒤 도착한 것은 버린다.
            var installing = true;
            var outcomes = await Task.Run(() => MelonLoaderInstaller.InstallAll(plans, (i, n, g) =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (installing) StatusText.Text = $"MelonLoader 설치 중 {i}/{n}: {g.DisplayName}";
                })));
            installing = false;

            var failed = outcomes.Where(o => o.Error is not null).ToList();
            var replaced = outcomes.Count(o => o.Error is null && o.Plan.Replaces);
            StatusText.Text = $"MelonLoader 설치 완료 {outcomes.Count - failed.Count}개" + (replaced > 0 ? $"(교체 {replaced})" : "") +
                              $" · 건너뜀 {plans.Count - ready}개 · 실패 {failed.Count}개" +
                              (uncheckedCount > 0 ? $" · 체크 해제 {uncheckedCount}개 제외" : "");
            if (failed.Count > 0)
            {
                Spinner.IsVisible = false;
                await TextDialog.ShowAsync(this, "MelonLoader 설치 실패",
                    $"실패 {failed.Count}개 — 이 게임들은 이번에 만든 파일을 지우고 교체 전 설치를 되돌렸습니다.\n\n" +
                    string.Join("\n", failed.Select(o => $"{o.Plan.Game.DisplayName}\n    {o.Error}")));
            }
        }

        /// <summary>
        /// 하나라도 풀려 있으면 모두 체크하고, 모두 체크돼 있으면 모두 푼다.
        /// 체크박스 자체의 전환 순서(미정 → 해제)는 따르지 않고 목록 상태로 정한다.
        /// </summary>
        private void OnSelectAllClick(object? sender, RoutedEventArgs e)
        {
            SetInstallChecked(_view.Any(g => !g.InstallChecked));
            UpdateSelectAll();
        }

        /// <summary>지금 보기 목록의 게임만 바꾼다. 다른 보기에 있는 게임의 체크는 그대로다.</summary>
        private void SetInstallChecked(bool value)
        {
            foreach (var g in _view) g.InstallChecked = value;
        }

        private void OnInstallCheckedChanged(object? sender, PropertyChangedEventArgs e) => UpdateSelectAll();

        /// <summary>모두 체크면 체크, 모두 해제면 해제, 섞여 있으면 미정(null)으로 보인다.</summary>
        private void UpdateSelectAll()
        {
            var total = _view.Count;
            var checkedCount = _view.Count(g => g.InstallChecked);
            SelectAllCheck.IsEnabled = total > 0;
            SelectAllCheck.IsChecked = checkedCount == 0 ? false : checkedCount == total ? true : null;
            SelectAllCheck.Content = total == 0
                ? "MelonLoader 설치 대상"
                : $"MelonLoader 설치 대상 {checkedCount} / {total}개 체크";
        }

        private void OnFilterChanged(object? sender, RoutedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            if (_last == null) return;
            var marker = SelectedMarker;

            _view.Clear();
            foreach (var g in SteamScanner.Filter(_last, marker, GamesOnlyCheck.IsChecked == true))
                _view.Add(g);
            UpdateSelectAll();
        }
    }
}
