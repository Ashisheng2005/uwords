using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Windows;

namespace Uword.App.Translation;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly TranslationClient _client;
    private CancellationTokenSource? _test;
    public event Action<TranslationSettings>? Saved;

    public SettingsWindow(SettingsStore store, TranslationClient client, TranslationSettings initial)
    {
        InitializeComponent();
        _store = store;
        _client = client;
        Populate(initial);
        SaveButton.Click += (_, _) => Save();
        TestButton.Click += async (_, _) => await TestAsync();
    }

    public void ShowSettings()
    {
        Show();
        Activate();
    }

    private void Populate(TranslationSettings value)
    {
        BaseUrlBox.Text = value.BaseUrl;
        ApiKeyBox.Password = value.ApiKey;
        ModelBox.Text = value.Model;
        LanguageBox.Text = value.TargetLanguage;
        TemperatureBox.Text = value.Temperature.ToString(CultureInfo.CurrentCulture);
        RateBox.Text = value.RequestsPerSecond.ToString(CultureInfo.CurrentCulture);
        LengthBox.Text = value.MaxTextLength.ToString(CultureInfo.CurrentCulture);
        ParagraphBox.Text = value.MaxParagraphs.ToString(CultureInfo.CurrentCulture);
        SystemBox.Text = value.SystemPrompt;
        MultiBox.Text = value.MultiParagraphPrompt;
        SingleBox.Text = value.SingleParagraphPrompt;
        ExampleBox.Text = value.ExamplePrompt;
    }

    private TranslationSettings Read()
    {
        if (!double.TryParse(TemperatureBox.Text, CultureInfo.CurrentCulture, out double temperature) ||
            !int.TryParse(RateBox.Text, out int rate) || !int.TryParse(LengthBox.Text, out int length) ||
            !int.TryParse(ParagraphBox.Text, out int paragraphs))
            throw new ArgumentException("请填写有效的温度、请求速率、字数和段数。");
        var settings = new TranslationSettings
        {
            BaseUrl = BaseUrlBox.Text.Trim(), ApiKey = ApiKeyBox.Password.Trim(),
            Model = ModelBox.Text.Trim(), TargetLanguage = LanguageBox.Text.Trim(),
            Temperature = temperature, RequestsPerSecond = rate, MaxTextLength = length,
            MaxParagraphs = paragraphs, SystemPrompt = SystemBox.Text,
            MultiParagraphPrompt = MultiBox.Text, SingleParagraphPrompt = SingleBox.Text,
            ExamplePrompt = ExampleBox.Text
        };
        settings.Validate();
        return settings;
    }

    private void Save()
    {
        try
        {
            var settings = Read();
            _store.Save(settings);
            Saved?.Invoke(settings);
            Feedback.Text = "已保存";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or
            System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        { Feedback.Text = ex.Message; }
    }

    private async Task TestAsync()
    {
        try
        {
            var settings = Read();
            _test?.Cancel();
            _test?.Dispose();
            _test = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            TestButton.IsEnabled = false;
            Feedback.Text = "正在测试...";
            string result = await _client.TestAsync(settings, _test.Token);
            Feedback.Text = $"连接成功：{result[..Math.Min(result.Length, 60)]}";
        }
        catch (OperationCanceledException) { Feedback.Text = "测试已取消或超时。"; }
        catch (Exception ex) when (ex is ArgumentException or TranslationRequestException or
            HttpRequestException or FormatException or System.Text.Json.JsonException)
        { Feedback.Text = ex is HttpRequestException ? "网络连接失败。" : ex.Message; }
        finally { TestButton.IsEnabled = true; }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        _test?.Cancel();
        Hide();
    }
}
