using System.Drawing;
using System.Windows;
using Uword.App.Diagnostics;
using Uword.App.Input;
using Uword.App.Overlay;
using Uword.App.Selection;
using Uword.App.Translation;
using Uword.App.Workflow;
using Forms = System.Windows.Forms;

namespace Uword.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _tray;
    private MouseMonitor? _mouse;
    private SelectionClient? _reader;
    private SelectionCoordinator? _coordinator;
    private StatusWindow? _diagnostics;
    private SettingsWindow? _settingsWindow;
    private TranslationClient? _translator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains(SelectionHost.Argument))
        {
            Shutdown(SelectionHost.Run());
            return;
        }

        _diagnostics = new StatusWindow();
        var log = new DiagnosticLog();
        log.WriteEvent("started");
        var store = new SettingsStore();
        TranslationSettings settings;
        bool settingsLoadFailed = false;
        try { settings = store.Load(); }
        catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException or
            System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        {
            settings = new TranslationSettings();
            settingsLoadFailed = true;
            log.WriteEvent("settings_load_failed");
        }
        _translator = new TranslationClient();
        _settingsWindow = new SettingsWindow(store, _translator, settings);
        var overlay = new OverlayController();
        _reader = new SelectionClient(Environment.ProcessPath ?? throw new InvalidOperationException("Host path unavailable."));
        _mouse = new MouseMonitor();
        _coordinator = new SelectionCoordinator(Dispatcher, _mouse, _reader, overlay, _diagnostics, log,
            _translator, settings);
        _settingsWindow.Saved += value => _coordinator.UpdateSettings(value);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("状态", null, (_, _) => _diagnostics.ShowStatus());
        menu.Items.Add("翻译设置", null, (_, _) => _settingsWindow.ShowSettings());
        var enabled = new Forms.ToolStripMenuItem("启用划词") { Checked = true };
        enabled.Click += (_, _) =>
        {
            if (_coordinator is null) return;
            _coordinator.Enabled = !_coordinator.Enabled;
            enabled.Checked = _coordinator.Enabled;
        };
        menu.Items.Add(enabled);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Uword 划词翻译",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => _diagnostics.ShowStatus();

        try
        {
            _mouse.Start();
            _diagnostics.SetStatus(settingsLoadFailed ? "翻译设置读取失败，请重新配置。" :
                "已就绪，在其他软件中选中文字后将鼠标停留在标志上。");
        }
        catch (Exception ex)
        {
            _diagnostics.SetStatus($"Mouse hook failed: {ex.Message}");
            log.WriteEvent("mouse_hook_failed");
            _diagnostics.ShowStatus();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _coordinator?.Dispose();
        _mouse?.Dispose();
        _translator?.Dispose();
        base.OnExit(e);
    }
}
