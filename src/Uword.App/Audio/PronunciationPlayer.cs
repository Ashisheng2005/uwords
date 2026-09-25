using System.Speech.Synthesis;

namespace Uword.App.Audio;

public sealed class PronunciationPlayer : IDisposable
{
    private SpeechSynthesizer? _speaker;

    public bool TrySpeak(string text, out string error)
    {
        error = "";
        try
        {
            _speaker ??= new SpeechSynthesizer();
            string language = text.Any(c => c is >= '\u4e00' and <= '\u9fff') ? "zh" : "en";
            var voice = _speaker.GetInstalledVoices().FirstOrDefault(v =>
                v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == language);
            if (voice is null)
            {
                error = "未安装匹配的系统语音";
                return false;
            }
            _speaker.SpeakAsyncCancelAll();
            _speaker.SelectVoice(voice.VoiceInfo.Name);
            _speaker.SpeakAsync(text);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or
            System.Runtime.InteropServices.COMException or PlatformNotSupportedException)
        {
            error = "系统语音不可用";
            return false;
        }
    }

    public void Stop()
    {
        try { _speaker?.SpeakAsyncCancelAll(); }
        catch (InvalidOperationException) { }
    }

    public void Dispose() => _speaker?.Dispose();
}
