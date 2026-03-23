using System.Windows.Controls;
using Microsoft.Win32;

namespace SegmentEffectPlugin.UI
{
    public partial class SegmentEffectView : UserControl
    {
        public SegmentEffectView()
        {
            InitializeComponent();
            var vm = new SegmentEffectViewModel();
            DataContext = vm;

            // 参照ボタンが押されたらファイルダイアログを開く
            vm.BrowseRequested += () =>
            {
                var dlg = new OpenFileDialog
                {
                    Title = "YMM4プロジェクトファイルを選択",
                    Filter = "YMM4プロジェクト (*.ymmp)|*.ymmp|すべてのファイル (*.*)|*.*",
                    DefaultExt = ".ymmp"
                };
                if (dlg.ShowDialog() == true)
                {
                    vm.SetProjectPath(dlg.FileName);
                }
            };
        }
    }
}
