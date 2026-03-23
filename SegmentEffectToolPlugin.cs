using System;
using YukkuriMovieMaker.Plugin;

namespace SegmentEffectPlugin
{
    /// <summary>
    /// メニューの「ツール」に表示されるエントリポイント
    /// </summary>
    public class SegmentEffectToolPlugin : IToolPlugin
    {
        public string Name => "テキスト自動分割ツール";

        public Type ViewModelType => typeof(SegmentEffectViewModel);

        public Type ViewType => typeof(SegmentEffectView);
    }
}
