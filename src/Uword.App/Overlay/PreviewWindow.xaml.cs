using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Uword.App.Interop;
using Uword.App.Translation;

namespace Uword.App.Overlay;

public partial class PreviewWindow : Window
{
    private static readonly Brush Accent = BrushFrom("#147D61");
    private static readonly Brush Inactive = BrushFrom("#F1F5F3");
    private static readonly Brush TextColor = BrushFrom("#172B27");
    private static readonly Brush Muted = BrushFrom("#52625D");
    private TranslationMode _mode;

    public event Action? Dismissed;
    public event Action<TranslationMode>? ModeChanged;
    public event Action? SpeakRequested;

    public PreviewWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Dismissed?.Invoke();
        SpeakButton.Click += (_, _) => SpeakRequested?.Invoke();
        TranslationButton.Click += (_, _) => ModeChanged?.Invoke(TranslationMode.Translation);
        DictionaryButton.Click += (_, _) => ModeChanged?.Invoke(TranslationMode.Dictionary);
    }

    public void SetContent(string text, string source, bool dictionaryEligible, TranslationMode mode)
    {
        SelectedText.Text = text;
        SourceName.Text = source;
        DictionaryButton.Visibility = dictionaryEligible ? Visibility.Visible : Visibility.Collapsed;
        SetMode(mode);
        SetLoading();
    }

    public void SetMode(TranslationMode mode)
    {
        _mode = mode;
        TranslationButton.Background = mode == TranslationMode.Translation ? Accent : Inactive;
        TranslationButton.Foreground = mode == TranslationMode.Translation ? Brushes.White : TextColor;
        DictionaryButton.Background = mode == TranslationMode.Dictionary ? Accent : Inactive;
        DictionaryButton.Foreground = mode == TranslationMode.Dictionary ? Brushes.White : TextColor;
        SpeakButton.Visibility = mode == TranslationMode.Dictionary ? Visibility.Visible : Visibility.Collapsed;
        SetVoiceStatus("");
    }

    public void SetLoading()
    {
        ResultContainer.Children.Clear();
        ResultContainer.Children.Add(Label("正在查询...", 14, Muted));
    }

    public void SetError(string message)
    {
        ResultContainer.Children.Clear();
        ResultContainer.Children.Add(Label(message, 13, BrushFrom("#A34C25")));
    }

    public void SetResult(TranslationResult result)
    {
        ResultContainer.Children.Clear();
        if (result.Entry is not { } entry)
        {
            if (_mode == TranslationMode.Dictionary)
                ResultContainer.Children.Add(Label("当前模型未返回词典结构，显示普通译文。", 11, Muted));
            ResultContainer.Children.Add(Label(result.Text ?? "", 15, TextColor));
            SpeakButton.Visibility = _mode == TranslationMode.Dictionary ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        ResultContainer.Children.Add(Label(entry.Headword, 19, TextColor, FontWeights.SemiBold));
        if (!string.IsNullOrEmpty(entry.Phonetic))
            ResultContainer.Children.Add(Label(entry.Phonetic, 13, Muted, marginTop: 3));
        ResultContainer.Children.Add(Label(entry.Translation, 15, Accent, FontWeights.SemiBold, 12));
        foreach (var sense in entry.Senses)
        {
            if (sense.PartOfSpeech.Length > 0)
                ResultContainer.Children.Add(Label(sense.PartOfSpeech, 12, Muted, FontWeights.SemiBold, 14));
            foreach (string meaning in sense.Meanings)
                ResultContainer.Children.Add(Label(meaning, 14, TextColor, marginTop: 4));
            foreach (var example in sense.Examples)
            {
                ResultContainer.Children.Add(Label(example.Source, 12, TextColor, marginTop: 10));
                ResultContainer.Children.Add(Label(example.Translation, 12, Muted, marginTop: 3));
            }
        }
        if (!string.IsNullOrEmpty(entry.Note))
        {
            ResultContainer.Children.Add(new Border
            {
                BorderBrush = BrushFrom("#D5DFDB"), BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 16, 0, 8)
            });
            ResultContainer.Children.Add(Label(entry.Note, 12, Muted));
        }
    }

    public void SetVoiceStatus(string message)
    {
        VoiceStatus.Text = message;
        VoiceStatus.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static TextBlock Label(string text, double size, Brush color, FontWeight? weight = null,
        double marginTop = 0) => new()
    {
        Text = text, FontSize = size, Foreground = color, FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, marginTop, 0, 0)
    };

    private static Brush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle,
            style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
    }
}
