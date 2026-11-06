using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SharkConfig.UI;

/// <summary>
/// Janela nativa que hospeda a interface web (wwwroot) via WebView2.
/// Tema escuro/claro na barra de título e detecção automática dos dispositivos por WM_DEVICECHANGE.
/// </summary>
public sealed class HostForm : Form
{
    static readonly Color DarkBase = Color.FromArgb(0x1E, 0x20, 0x23), LightBase = Color.FromArgb(0xF5, 0xF5, 0xF7);
    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer _deviceDebounce = new() { Interval = 500 };
    readonly string _query;
    readonly AppSettings _settings = AppSettings.Load();
    readonly Bridge _bridge;

    // bandeja: fechar a janela só a esconde; o ícone mostra a bateria do mouse (lida a cada minuto)
    readonly NotifyIcon _tray = new() { Text = "SharkConfig" };
    readonly ToolStripMenuItem _startupItem = new("Iniciar com o Windows") { CheckOnClick = true };
    readonly System.Windows.Forms.Timer _batteryTimer = new() { Interval = 60_000 };
    readonly Icon _appIcon;
    Icon? _batteryIcon;
    bool _allowShow, _exiting, _trayHintShown;

    /// <summary>Mensagem que a segunda execução do app manda (broadcast) para esta janela vir para a frente.</summary>
    public static readonly int ShowWindowMessage = RegisterWindowMessage("SharkConfig.MostrarJanela");

    // 1 processo de renderização e sem serviços de fundo do Edge. A GPU fica ligada: é ela que
    // compõe as animações e o vidro a 60 fps sem custar CPU. O serviço de rede roda dentro do processo
    // principal (a página é só arquivo local): −1 processo, ~9 MB. Medido e NÃO adotado, porque engasga:
    // --in-process-gpu (−70 MB, mas ~110 fps com quadros de 25–37 ms), --disable-gpu-rasterization
    // (−15 MB, quadro de 71 ms ao abrir Botões) e memória Low ao perder o foco (−40 MB, 50–75 ms ao voltar).
    const string BrowserArgs =
        "--renderer-process-limit=1 --enable-features=NetworkServiceInProcess2 " +
        "--disable-background-networking --disable-component-update " +
        "--disable-sync --disable-extensions --disable-default-apps --no-first-run --disable-breakpad " +
        "--disable-features=msSmartScreenProtection,msWebOOUI,msPdfOOUI,SpareRendererForSitePerProcess," +
        "AutofillServerCommunication,OptimizationHints,Translate,MediaRouter,BackForwardCache " +
        "--js-flags=--max-old-space-size=48";

    /// <param name="startInTray">--tray (Iniciar com o Windows): começa só na bandeja; a interface
    /// (WebView2) só é criada quando a janela é aberta pela primeira vez.</param>
    public HostForm(string? startPage, bool startInTray = false)
    {
        Text = "SharkConfig";
        _allowShow = !startInTray;
        using (var ico = typeof(HostForm).Assembly.GetManifestResourceStream("SharkConfig.app.ico"))
            _appIcon = ico is not null ? new Icon(ico) : SystemIcons.Application;
        Icon = _appIcon;   // multi-tamanho: o Windows escolhe 16/20/24 (título) e 32/40/48 (barra)
        MinimumSize = new Size(900, 600);   // abaixo disso a UI escalaria demais
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);
        _query = $"?page={Uri.EscapeDataString(startPage ?? "overview")}";
        Load += async (_, _) => await InitAsync();
        Resize += async (_, _) => await OnMinimizeChangedAsync();
        // a ponte existe desde o início (a bandeja lê a bateria mesmo sem a interface aberta)
        _bridge = new Bridge(
            json => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => _web.CoreWebView2?.PostWebMessageAsJson(json)); },
            f => (string?)Invoke(f),
            _settings,
            s => BeginInvoke(() => ApplyAppearance(s)));
        _deviceDebounce.Tick += async (_, _) => { _deviceDebounce.Stop(); await _bridge.OnDevicesChangedAsync(); await UpdateBatteryAsync(); };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir o SharkConfig", null, (_, _) => ShowFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
        menu.Opening += (_, _) => _startupItem.Checked = Startup.IsEnabled;
        _startupItem.CheckedChanged += (_, _) => { if (_startupItem.Checked != Startup.IsEnabled) Startup.Set(_startupItem.Checked); };
        _tray.ContextMenuStrip = menu;
        _tray.Icon = _appIcon;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        _tray.Visible = true;
        _batteryTimer.Tick += async (_, _) => await UpdateBatteryAsync();
        _batteryTimer.Start();
        Shown += async (_, _) => await UpdateBatteryAsync();
        Startup.Refresh();
        if (startInTray) { CreateHandle(); _ = StartTrayAsync(); }
    }

    async Task StartTrayAsync() { await Task.Delay(1500); await UpdateBatteryAsync(); }

    /// <summary>Na partida pela bandeja a janela fica escondida até o usuário abri-la.</summary>
    protected override void SetVisibleCore(bool value)
    {
        if (!_allowShow) { value = false; if (!IsHandleCreated) CreateHandle(); }
        base.SetVisibleCore(value);
    }

    public void ShowFromTray()
    {
        _allowShow = true;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Resume();
        Activate();
        SetForegroundWindow(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // o X esconde na bandeja; Sair (menu da bandeja) e o desligamento do Windows encerram
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _ = SuspendAsync();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(3000, "SharkConfig continua na bandeja",
                    "A bateria do mouse aparece no ícone. Clique nele para abrir, ou use Sair no menu para fechar.", ToolTipIcon.None);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    void ExitApp()
    {
        _exiting = true;
        _batteryTimer.Stop();
        _tray.Visible = false;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tray.Dispose(); _batteryIcon?.Dispose(); _batteryTimer.Dispose(); }
        base.Dispose(disposing);
    }

    /// <summary>Lê a bateria e atualiza o ícone e a dica da bandeja.</summary>
    async Task UpdateBatteryAsync()
    {
        if (_exiting) return;
        Bridge.BatteryStatus st;
        try { st = await _bridge.PollBatteryAsync(); } catch { return; }
        string tip;
        Icon icon;
        if (!st.Present) { tip = "SharkConfig: nenhum mouse conectado"; icon = _appIcon; }
        else if (st.Percent is int p)
        {
            tip = st.Asleep ? $"X8 Ultra: {p}% (dormindo, última leitura)" : $"X8 Ultra: {p}%{(st.Wireless ? "" : " (cabo)")}";
            icon = TrayBattery.Render(p);
        }
        else { tip = st.Asleep ? "X8 Ultra dormindo: mexa no mouse para ler a bateria" : "X8 Ultra: bateria indisponível"; icon = _appIcon; }
        var old = _batteryIcon;
        _tray.Icon = icon;
        _batteryIcon = ReferenceEquals(icon, _appIcon) ? null : icon;
        if (old is not null && !ReferenceEquals(old, icon)) old.Dispose();
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;
        DwmSetWindowAttribute(Handle, 33, ref round, 4);    // DWMWA_WINDOW_CORNER_PREFERENCE = arredondado
        ApplyAppearance(_settings);
    }

    /// <summary>Tema na moldura nativa (barra de título e borda) e no fundo atrás da página.</summary>
    public void ApplyAppearance(AppSettings s)
    {
        if (!IsHandleCreated) return;
        bool dark = s.Theme != "light";
        var baseColor = dark ? DarkBase : LightBase;
        int darkMode = dark ? 1 : 0;
        BackColor = baseColor;
        _web.DefaultBackgroundColor = baseColor;            // sem clarão antes de a página desenhar
        int caption = ToBgr(baseColor);
        int text = ToBgr(dark ? Color.FromArgb(0xF2, 0xF2, 0xF0) : Color.FromArgb(0x1D, 0x1D, 0x1F));
        DwmSetWindowAttribute(Handle, 20, ref darkMode, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(Handle, 35, ref caption, 4);  // DWMWA_CAPTION_COLOR
        DwmSetWindowAttribute(Handle, 34, ref caption, 4);  // DWMWA_BORDER_COLOR
        DwmSetWindowAttribute(Handle, 36, ref text, 4);     // DWMWA_TEXT_COLOR
    }

    static int ToBgr(Color c) => c.B << 16 | c.G << 8 | c.R;

    protected override void WndProc(ref Message m)
    {
        // WM_DEVICECHANGE: chega a toda janela de topo quando algo USB é ligado/desligado.
        // Sem consulta periódica: só reagimos ao aviso do Windows (com 500 ms de espera p/ estabilizar).
        if (m.Msg == 0x0219) { _deviceDebounce.Stop(); _deviceDebounce.Start(); }
        if (m.Msg == ShowWindowMessage && ShowWindowMessage != 0)
        {
            // outra execução do app pediu a janela (inclusive com ela escondida na bandeja)
            if (!Visible) ShowFromTray();
            else { ShowWindow(Handle, IsIconic(Handle) ? 9 : 5); SetForegroundWindow(Handle); }   // SW_RESTORE | SW_SHOW
            return;
        }
        base.WndProc(ref m);
    }

    async Task InitAsync()
    {
        try { await StartWebViewAsync(); }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("O SharkConfig precisa do Microsoft Edge WebView2 Runtime, que não foi encontrado neste PC.\n\n" +
                 "Ele já vem no Windows 11. No Windows 10, instale o \"WebView2 Runtime\" pelo site da Microsoft " +
                 "(developer.microsoft.com/microsoft-edge/webview2) e abra o app de novo.");
        }
        catch (Exception ex)
        {
            Fail("Não foi possível iniciar a interface do SharkConfig (WebView2).\n\n" +
                 "Feche o app, espere alguns segundos e abra de novo. Se continuar, reinstale o WebView2 Runtime.\n\n" +
                 "Detalhe técnico: " + ex.Message);
        }
    }

    void Fail(string text)
    {
        MessageBox.Show(this, text, "SharkConfig", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    async Task StartWebViewAsync()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharkConfig", "WebView2");
        var args = BrowserArgs + " " + Environment.GetEnvironmentVariable("SHARKCONFIG_WV2_ARGS"); // para experimentos
        var options = new CoreWebView2EnvironmentOptions(args.Trim()) { AreBrowserExtensionsEnabled = false };
        var env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        var st = core.Settings;
        st.AreDefaultContextMenusEnabled = false;
        st.IsStatusBarEnabled = false;
        st.IsZoomControlEnabled = false;
        st.IsPinchZoomEnabled = false;
        st.IsSwipeNavigationEnabled = false;
        st.IsPasswordAutosaveEnabled = false;
        st.IsGeneralAutofillEnabled = false;
        st.AreBrowserAcceleratorKeysEnabled = false;
        st.IsBuiltInErrorPageEnabled = false;
        core.SetVirtualHostNameToFolderMapping("sharkconfig.local",
            Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);

        core.WebMessageReceived += (_, e) => _ = _bridge.HandleAsync(e.WebMessageAsJson);
        core.Navigate("https://sharkconfig.local/index.html" + _query);
    }

    bool _suspended;
    async Task OnMinimizeChangedAsync()
    {
        if (WindowState == FormWindowState.Minimized) await SuspendAsync();
        else if (Visible) Resume();
    }

    /// <summary>Minimizado ou escondido na bandeja: congela scripts/render e devolve memória ao sistema.</summary>
    async Task SuspendAsync()
    {
        var core = _web.CoreWebView2;
        if (core is null || _suspended) return;
        _suspended = true;
        core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        _web.Visible = false;
        try { await core.TrySuspendAsync(); } catch { /* ocupado: tenta na próxima vez */ }
        EmptyWorkingSet(Environment.ProcessId);
    }

    void Resume()
    {
        var core = _web.CoreWebView2;
        if (core is null || !_suspended) return;
        _suspended = false;
        _web.Visible = true;   // retomar é automático ao ficar visível
        core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
        _ = _bridge.OnDevicesChangedAsync();   // verifica de novo ao reabrir: algo pode ter mudado enquanto estava escondido
    }

    static void EmptyWorkingSet(int pid)
    {
        using var p = System.Diagnostics.Process.GetProcessById(pid);
        SetProcessWorkingSetSize(p.Handle, -1, -1);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, nint min, nint max);
}
