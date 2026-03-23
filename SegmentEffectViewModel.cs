using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.Generic;

namespace SegmentEffectPlugin
{
    public class SegmentEffectViewModel : INotifyPropertyChanged
    {
        // ===== プロジェクト =====
        private string _projectPath = "プロジェクトが見つかりません";
        public string ProjectPath
        {
            get => _projectPath;
            set { _projectPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanScan)); }
        }

        private string _statusText = "待機中";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // ===== テキストアイテム一覧 =====
        public ObservableCollection<TextItemInfo> TextItems { get; } = new();

        private TextItemInfo? _selectedTextItem;
        public TextItemInfo? SelectedTextItem
        {
            get => _selectedTextItem;
            set
            {
                _selectedTextItem = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSplit));
                OnPropertyChanged(nameof(HasSelection));
                // 元テキストの間隔を反映
                if (value != null)
                {
                    LineSpacing = value.LineSpacing;
                    CharSpacing = value.CharSpacing;
                    SelectionStart = 0;
                    SelectionLength = value.Text.Replace("\r", "").Replace("\n", "").Length;
                }
                BuildSegmentsFromSelection();
            }
        }

        public bool HasSelection => SelectedTextItem != null;

        // ===== 分割モード =====
        // 0=行分割, 1=文字数分割, 2=文字選択分割
        private int _splitMode = 0;
        public int SplitMode
        {
            get => _splitMode;
            set { _splitMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLineMode)); OnPropertyChanged(nameof(IsCharCountMode)); OnPropertyChanged(nameof(IsCharSelectMode)); OnPropertyChanged(nameof(SpacingLabel)); BuildSegmentsFromSelection(); }
        }

        public bool IsLineMode => SplitMode == 0;
        public bool IsCharCountMode => SplitMode == 1;
        public bool IsCharSelectMode => SplitMode == 2;
        public string SpacingLabel => SplitMode == 0 ? "行間 (px)" : "文字間隔 (px)";

        // ===== 文字数分割パラメータ =====
        private int _charCount = 1;
        public int CharCount
        {
            get => _charCount;
            set { _charCount = Math.Max(1, value); OnPropertyChanged(); BuildSegmentsFromSelection(); }
        }

        // ===== 文字選択分割パラメータ =====
        private int _selectionStart = 0;
        public int SelectionStart
        {
            get => _selectionStart;
            set { _selectionStart = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(SelectionInfo)); BuildSegmentsFromSelection(); }
        }

        private int _selectionLength = 1;
        public int SelectionLength
        {
            get => _selectionLength;
            set { _selectionLength = Math.Max(1, value); OnPropertyChanged(); OnPropertyChanged(nameof(SelectionInfo)); BuildSegmentsFromSelection(); }
        }

        public string SelectionInfo
        {
            get
            {
                if (SelectedTextItem == null) return "";
                var flat = SelectedTextItem.Text.Replace("\r", "").Replace("\n", "");
                int start = Math.Min(SelectionStart, flat.Length);
                int len = Math.Min(SelectionLength, flat.Length - start);
                if (len <= 0) return $"位置 {start} （範囲外）";
                var selected = flat.Substring(start, len);
                return $"「{selected}」({len}文字)";
            }
        }

        // ===== 間隔パラメータ =====
        private double _lineSpacing = 100.0;
        public double LineSpacing
        {
            get => _lineSpacing;
            set { _lineSpacing = value; OnPropertyChanged(); }
        }

        private double _charSpacing = 0;
        public double CharSpacing
        {
            get => _charSpacing;
            set { _charSpacing = value; OnPropertyChanged(); }
        }

        private double _spacingPercent = 100.0;
        public double SpacingPercent
        {
            get => _spacingPercent;
            set { _spacingPercent = value; OnPropertyChanged(); }
        }

        // ===== セグメントプレビュー =====
        public ObservableCollection<SplitSegment> Segments { get; } = new();

        // ===== コマンド =====
        public bool CanScan => File.Exists(ProjectPath);
        public bool CanSplit => SelectedTextItem != null && Segments.Count > 1;

        public ICommand AutoDetectCommand { get; }
        public ICommand BrowseCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand SplitCommand { get; }

        /// <summary>参照ダイアログを開くためのイベント（Viewが購読）</summary>
        public event Action? BrowseRequested;

        public SegmentEffectViewModel()
        {
            AutoDetectCommand = new RelayCommand(_ => AutoDetect());
            BrowseCommand = new RelayCommand(_ => BrowseRequested?.Invoke());
            ScanCommand = new RelayCommand(_ => Scan());
            SplitCommand = new RelayCommand(async _ => await SplitAsync());
            AutoDetect();
        }

        private void AutoDetect()
        {
            var p = ProjectDetector.GetActiveProjectPath();
            if (!string.IsNullOrEmpty(p))
            {
                ProjectPath = p;
                StatusText = "プロジェクトを自動検出しました";
                Scan();
            }
            else
            {
                StatusText = "プロジェクトが見つかりません。参照ボタンで指定してください";
            }
        }

        public void SetProjectPath(string path)
        {
            ProjectPath = path;
            Scan();
        }

        private void Scan()
        {
            if (!File.Exists(ProjectPath)) return;
            TextItems.Clear();
            SelectedTextItem = null;
            Segments.Clear();
            StatusText = "検索中...";

            var items = YmmpEditor.GetAllTextItems(ProjectPath);
            foreach (var item in items)
                TextItems.Add(item);

            StatusText = $"テキストアイテムを {items.Count} 件検出";
        }

        /// <summary>
        /// セグメントプレビューを生成
        /// </summary>
        private void BuildSegmentsFromSelection()
        {
            Segments.Clear();
            if (SelectedTextItem == null) { OnPropertyChanged(nameof(CanSplit)); return; }

            var text = SelectedTextItem.Text;

            if (SplitMode == 0)
            {
                // === 行分割 ===
                var lines = text.Split('\n');
                int idx = 0;
                foreach (var line in lines)
                {
                    var clean = line.TrimEnd('\r');
                    Segments.Add(new SplitSegment { StartIndex = idx, CharCount = clean.Length, Text = clean });
                    idx += line.Length + 1;
                }
            }
            else if (SplitMode == 1)
            {
                // === 文字数分割 ===
                var flat = text.Replace("\r", "").Replace("\n", "");
                int count = Math.Max(1, CharCount);
                int idx = 0;
                while (idx < flat.Length)
                {
                    int take = Math.Min(count, flat.Length - idx);
                    Segments.Add(new SplitSegment { StartIndex = idx, CharCount = take, Text = flat.Substring(idx, take) });
                    idx += take;
                }
            }
            else if (SplitMode == 2)
            {
                // === 文字選択分割（3パーツに分割）===
                var flat = text.Replace("\r", "").Replace("\n", "");
                int start = Math.Min(SelectionStart, flat.Length);
                int len = Math.Min(SelectionLength, flat.Length - start);
                if (start >= flat.Length || len <= 0)
                {
                    Segments.Add(new SplitSegment { StartIndex = 0, CharCount = flat.Length, Text = flat });
                }
                else
                {
                    var pre = flat.Substring(0, start);
                    var sel = flat.Substring(start, len);
                    var post = flat.Substring(start + len);

                    if (pre.Length > 0)
                        Segments.Add(new SplitSegment { StartIndex = 0, CharCount = pre.Length, Text = pre, IsRemainder = true });
                    
                    if (sel.Length > 0)
                        Segments.Add(new SplitSegment { StartIndex = start, CharCount = sel.Length, Text = sel, IsRemainder = false });
                        
                    if (post.Length > 0)
                        Segments.Add(new SplitSegment { StartIndex = start + len, CharCount = post.Length, Text = post, IsRemainder = true });
                }
            }

            OnPropertyChanged(nameof(CanSplit));
        }

        private async Task SplitAsync()
        {
            if (SelectedTextItem == null || Segments.Count <= 1) return;
            var target = SelectedTextItem;
            var path = ProjectPath;
            var segs = Segments.ToList();
            bool lineMode = SplitMode == 0;

            StatusText = "分割を処理中...";

            bool success = await Task.Run(() =>
                YmmpEditor.SplitTextItemBySegments(path, target, segs, SpacingPercent, lineMode, LineSpacing, CharSpacing));

            if (success)
            {
                TextItems.Remove(target);
                Segments.Clear();
                SelectedTextItem = null;

                StatusText = "再読み込み中...";
                bool reloaded = false;
                try { reloaded = ProjectDetector.ReloadCurrentProject(path); } catch { }

                StatusText = reloaded
                    ? "✅ 分割完了！自動で再読み込みしました"
                    : "✅ 分割完了！手動でプロジェクトを再読み込みしてください";
            }
            else
            {
                StatusText = "❌ 分割失敗。ファイルがロックされている可能性があります";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}
