using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SegmentEffectPlugin.Infrastructure
{
    /// <summary>
    /// テキストアイテムの情報（プロジェクトファイルから抽出）
    /// </summary>
    public class TextItemInfo
    {
        public int TimelineIndex { get; set; }
        public int ItemIndex { get; set; }
        public int Layer { get; set; }
        public int Frame { get; set; }
        public int Length { get; set; }
        public double FontSize { get; set; }
        public double LineSpacing { get; set; }
        public double CharSpacing { get; set; }
        public int HorizontalAlignment { get; set; }
        public int VerticalAlignment { get; set; }
        public string Text { get; set; } = "";

        public string PreviewText => Text.Length > 60 ? Text[..57] + "..." : Text;
        public string Details => $"L:{Layer} / F:{Frame}~{Frame + Length} / {FontSize}pt / {Text.Length}文字";
    }

    /// <summary>
    /// 分割セグメントの定義
    /// </summary>
    public class SplitSegment
    {
        public int StartIndex { get; set; }
        public int CharCount { get; set; }
        public string Text { get; set; } = "";
        /// <summary>trueなら空白穴埋め（選択されなかった残り部分）</summary>
        public bool IsRemainder { get; set; }
        public string Preview => Text.Length > 40 ? Text[..37] + "..." : Text;
    }

    public static class YmmpEditor
    {
        /// <summary>
        /// プロジェクトから全てのTextItemを検索（行間・文字間隔も取得）
        /// </summary>
        public static List<TextItemInfo> GetAllTextItems(string ymmpPath)
        {
            var results = new List<TextItemInfo>();
            if (!File.Exists(ymmpPath)) return results;

            try
            {
                var doc = JsonNode.Parse(File.ReadAllText(ymmpPath));
                var timelines = doc?["Timelines"]?.AsArray();
                if (timelines == null) return results;

                for (int tIdx = 0; tIdx < timelines.Count; tIdx++)
                {
                    var items = timelines[tIdx]?["Items"]?.AsArray();
                    if (items == null) continue;

                    for (int iIdx = 0; iIdx < items.Count; iIdx++)
                    {
                        var item = items[iIdx];
                        if (item == null) continue;

                        var type = item["$type"]?.GetValue<string>() ?? "";
                        if (!type.Contains("TextItem")) continue;

                        var text = item["Text"]?.GetValue<string>() ?? "";
                        if (string.IsNullOrEmpty(text)) continue;

                        results.Add(new TextItemInfo
                        {
                            TimelineIndex = tIdx,
                            ItemIndex = iIdx,
                            Layer = item["Layer"]?.GetValue<int>() ?? 0,
                            Frame = item["Frame"]?.GetValue<int>() ?? 0,
                            Length = item["Length"]?.GetValue<int>() ?? 0,
                            FontSize = GetAnimatableValue(item, "FontSize", 40.0),
                            // YMM4の内部プロパティ名は一般的に LineSpacing
                            LineSpacing = GetAnimatableValue(item, "LineSpacing", 100.0),
                            CharSpacing = GetAnimatableValue(item, "LetterSpacing", 0.0),
                            HorizontalAlignment = GetAlignmentValue(item, "HorizontalAlignment", 1), // Default to Center
                            VerticalAlignment = GetAlignmentValue(item, "VerticalAlignment", 1),   // Default to Middle (1)
                            Text = text
                        });
                    }
                }
            }
            catch { }

            return results;
        }

        /// <summary>
        /// セグメント定義に基づいてテキストアイテムを分割。
        /// 文字選択モード: 選択部分を抽出し、残り部分は同じ文字数の全角空白で穴埋め。
        /// </summary>
        public static bool SplitTextItemBySegments(
            string ymmpPath, TextItemInfo target,
            List<SplitSegment> segments, double spacing,
            bool isLineSplit, double lineSpacing, double charSpacing)
        {
            if (segments.Count <= 1) return true;

            try
            {
                File.Copy(ymmpPath, ymmpPath + ".sb.bak", overwrite: true);

                var doc = JsonNode.Parse(File.ReadAllText(ymmpPath));
                if (doc == null) return false;
                var itemsArray = doc["Timelines"]?.AsArray()?[target.TimelineIndex]?["Items"]?.AsArray();
                if (itemsArray == null) return false;

                var originalItem = itemsArray[target.ItemIndex];
                if (originalItem == null ||
                    (originalItem["Layer"]?.GetValue<int>() ?? -1) != target.Layer ||
                    (originalItem["Frame"]?.GetValue<int>() ?? -1) != target.Frame)
                    return false;

                double originalX = GetAnimatableValue(originalItem, "X", 0.0);
                double originalY = GetAnimatableValue(originalItem, "Y", 0.0);
                double fontSize = GetAnimatableValue(originalItem, "FontSize", 40.0);
                string fontName = originalItem["Font"]?.GetValue<string>() ?? "メイリオ";
                
                // 配置設定を取得（デフォルトは「中央・中」とする）
                int hAlign = GetAlignmentValue(originalItem, "HorizontalAlignment", 1);
                int vAlign = GetAlignmentValue(originalItem, "VerticalAlignment", 1);

                var newItems = new List<JsonNode>();

                if (isLineSplit)
                {
                    // 行間の計算: YMM4の100%はピクセル単位のFontSizeに相当
                    double effectiveLineSpacing = Math.Max(lineSpacing, 1.0); // 0は回避
                    double stepY = fontSize * (effectiveLineSpacing / 100.0) * (spacing / 100.0);
                    int count = segments.Count;

                    for (int i = 0; i < count; i++)
                    {
                        var seg = segments[i];
                        var cloned = JsonNode.Parse(originalItem.ToJsonString())!;
                        cloned["Text"] = seg.Text;
                        SetAnimatableValue(cloned, "LineSpacing", lineSpacing);
                        SetAnimatableValue(cloned, "LetterSpacing", charSpacing);
                        
                        double newY = originalY;
                        // 垂直揃えに基づいた配置（0.5行分のピクセルオフセット補正が必要）
                        if (vAlign == 0) // 上揃え (Top)
                            newY = originalY + (stepY / 2.0) + (i * stepY);
                        else if (vAlign == 2) // 下揃え (Bottom)
                            newY = originalY - (stepY / 2.0) - (count - 1 - i) * stepY;
                        else // 中揃え (Middle) / 不明
                            newY = originalY + (i - (count - 1) / 2.0) * stepY;

                        SetAnimatableValue(cloned, "Y", newY);
                        newItems.Add(cloned);
                    }
                }
                else
                {
                    // 文字分割（X軸方向）: FormattedTextで正確な文字幅を計算
                    var typeFace = new System.Windows.Media.Typeface(fontName);
                    var segmentWidths = new List<double>();
                    
                    foreach (var seg in segments)
                    {
                        string t = seg.Text ?? "";
                        var ft = new System.Windows.Media.FormattedText(
                            t,
                            System.Globalization.CultureInfo.CurrentCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            typeFace,
                            fontSize,
                            System.Windows.Media.Brushes.Black,
                            new System.Windows.Media.NumberSubstitution(),
                            System.Windows.Media.TextFormattingMode.Ideal,
                            1.0);
                        
                        double w = ft.WidthIncludingTrailingWhitespace;
                        if (t.Length > 1) w += (t.Length - 1) * charSpacing;
                        segmentWidths.Add(w);
                    }

                    double totalWidth = segmentWidths.Sum();
                    if (segments.Count > 1) totalWidth += (segments.Count - 1) * charSpacing;
                    
                    // 水平揃えに基づいた始点計算
                    double currentStartX = originalX;
                    if (hAlign == 0) // 左揃え (Left)
                        currentStartX = originalX;
                    else if (hAlign == 2) // 右揃え (Right)
                        currentStartX = originalX - totalWidth;
                    else // 中央揃え (Center) / 不明
                        currentStartX = originalX - totalWidth / 2.0;

                    for (int i = 0; i < segments.Count; i++)
                    {
                        var seg = segments[i];
                        var cloned = JsonNode.Parse(originalItem.ToJsonString())!;
                        cloned["Text"] = seg.Text;
                        SetAnimatableValue(cloned, "LineSpacing", lineSpacing);
                        SetAnimatableValue(cloned, "LetterSpacing", charSpacing);

                        // 単体アイテムは中心が座標になるため、各セグメントの幅の半分を足す
                        double segCenter = currentStartX + (segmentWidths[i] / 2.0);
                        SetAnimatableValue(cloned, "X", segCenter);
                        
                        currentStartX += segmentWidths[i] + charSpacing;
                        newItems.Add(cloned);
                    }
                }

                itemsArray.RemoveAt(target.ItemIndex);
                for (int i = 0; i < newItems.Count; i++)
                    itemsArray.Insert(target.ItemIndex + i, newItems[i]);

                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                File.WriteAllText(ymmpPath, doc.ToJsonString(opts));

                return true;
            }
            catch { return false; }
        }

        // ---- 旧API互換 ----
        public static List<TextItemInfo> GetMultiLineTextItems(string ymmpPath)
            => GetAllTextItems(ymmpPath).Where(t => t.Text.Contains('\n')).ToList();

        public static bool SplitTextItem(string ymmpPath, TextItemInfo target, double lineSpacingOffset)
        {
            var lines = target.Text.Split('\n');
            int idx = 0;
            var segments = new List<SplitSegment>();
            foreach (var line in lines)
            {
                var clean = line.TrimEnd('\r');
                segments.Add(new SplitSegment { StartIndex = idx, CharCount = clean.Length, Text = clean });
                idx += line.Length + 1;
            }
            return SplitTextItemBySegments(ymmpPath, target, segments, lineSpacingOffset,
                true, target.LineSpacing, target.CharSpacing);
        }

        private static int GetAlignmentValue(JsonNode item, string propertyName, int defaultValue)
        {
            try
            {
                var node = item[propertyName];
                if (node == null) return defaultValue;

                // Animatable形式（"Values": [...]）のチェック
                if (node is JsonObject obj && obj.ContainsKey("Values"))
                {
                    var values = obj["Values"]?.AsArray();
                    if (values != null && values.Count > 0)
                        node = values[0]?["Value"];
                }

                if (node is JsonValue val)
                {
                    // 数値型
                    if (val.TryGetValue<int>(out int intVal)) return intVal;
                    // 文字列型
                    if (val.TryGetValue<string>(out string strVal))
                    {
                        switch (strVal.ToLower())
                        {
                            case "left": case "top": return 0;
                            case "center": case "middle": case "middlecenter": return 1;
                            case "right": case "bottom": return 2;
                        }
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        internal static double GetAnimatableValue(JsonNode item, string propertyName, double defaultValue)
        {
            try
            {
                var prop = item[propertyName];
                if (prop == null) return defaultValue;
                
                if (prop is JsonValue val)
                {
                    if (val.TryGetValue<double>(out double dbl)) return dbl;
                    return defaultValue;
                }
                if (prop is JsonObject obj && obj.ContainsKey("Values"))
                {
                    var values = obj["Values"]?.AsArray();
                    if (values != null && values.Count > 0)
                        return values[0]?["Value"]?.GetValue<double>() ?? defaultValue;
                }
            }
            catch { }
            return defaultValue;
        }

        internal static void SetAnimatableValue(JsonNode item, string propertyName, double newValue)
        {
            try
            {
                var prop = item[propertyName];
                if (prop == null) { item[propertyName] = newValue; return; }

                if (prop is JsonValue)
                    item[propertyName] = newValue;
                else if (prop is JsonObject obj && obj.ContainsKey("Values"))
                {
                    var values = obj["Values"]?.AsArray();
                    if (values != null && values.Count > 0)
                        values[0]!["Value"] = Math.Round(newValue, 2);
                }
                else
                {
                    // プロパティが存在するが型が不明な場合は上書き
                    item[propertyName] = newValue;
                }
            }
            catch { }
        }
    }
}
