using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharkConfig.Protocol;
using SharkConfig.Transport;

namespace SharkConfig.UI;

/// <summary>Configuração no formato da interface web (JSON camelCase).</summary>
public sealed class ConfigDto
{
    public int PollingCode { get; set; }
    public int SensorMode { get; set; }
    public int Lod { get; set; }
    public bool MotionSync { get; set; }
    public bool AngleSnap { get; set; }
    public bool Ripple { get; set; }
    public bool Performance { get; set; }
    public int PerformanceTimeout { get; set; }
    public int Debounce { get; set; }
    public int Sleep { get; set; }
    public int StageCount { get; set; }
    public int CurrentStage { get; set; }
    public int[] Dpi { get; set; } = [];
    public string[] Colors { get; set; } = [];
    public int LedEffect { get; set; }
    /// <summary>Nível de brilho 1–10 (como o slider do app oficial), não o valor gravado.</summary>
    public int LedBrightness { get; set; }
    public int LedSpeed { get; set; }
    /// <summary>Id de <see cref="ButtonOption"/> ou "custom:tt-pp-pp" se não mapeado.</summary>
    public string[] Buttons { get; set; } = [];
    /// <summary>Modo do LED de status do receptor (1–3) ou 0 quando não há receptor com esse LED.</summary>
    public int DongleStatus { get; set; }

    public static ConfigDto From(MouseConfig c) => new()
    {
        PollingCode = c.PollingRateCode, SensorMode = c.SensorMode, Lod = c.Lod, MotionSync = c.MotionSync,
        AngleSnap = c.AngleSnap, Ripple = c.Ripple, Performance = c.Performance,
        PerformanceTimeout = c.PerformanceTimeoutUnits, Debounce = c.DebounceMs, Sleep = c.SleepUnits,
        StageCount = c.StageCount, CurrentStage = c.CurrentStage, Dpi = (int[])c.Dpi.Clone(),
        Colors = c.DpiColors.Select(x => $"#{x.R:X2}{x.G:X2}{x.B:X2}").ToArray(),
        LedEffect = c.DpiLedEffect, LedBrightness = MouseConfig.BrightnessLevelOf(c.DpiLedBrightness), LedSpeed = c.DpiLedSpeed,
        Buttons = c.Buttons.Select(b => ButtonOption.Find(b)?.Id ?? $"custom:{(byte)b.Type:X2}-{b.P1:X2}-{b.P2:X2}").ToArray(),
    };

    /// <summary>
    /// Aplica a edição da UI sobre a config lida do mouse. Valores só são validados/limitados quando o
    /// usuário os mudou — o que veio do mouse e não foi tocado é preservado byte a byte.
    /// Valores fora das listas/faixas do app oficial são rejeitados.
    /// </summary>
    public void ApplyTo(MouseConfig c)
    {
        static int Pick(int current, int edited, int min, int max) => edited == current ? current : Math.Clamp(edited, min, max);
        static byte OneOf(byte current, int edited, IEnumerable<byte> allowed, string what) =>
            edited == current ? current : allowed.Contains((byte)edited) && edited is >= 0 and <= 255
                ? (byte)edited : throw new ArgumentException($"{what}: valor {edited} não é aceito pelo mouse.");

        c.PollingRateCode = OneOf(c.PollingRateCode, PollingCode, MouseConfig.PollingRates.Select(p => p.Code), "Taxa de atualização");
        c.SensorMode = Pick(c.SensorMode, SensorMode, 0, 2);
        c.Lod = Pick(c.Lod, Lod, 0, 2);
        c.MotionSync = MotionSync; c.AngleSnap = AngleSnap; c.Ripple = Ripple; c.Performance = Performance;
        c.PerformanceTimeoutUnits = OneOf(c.PerformanceTimeoutUnits, PerformanceTimeout, MouseConfig.SleepOptions.Take(8).Select(s => s.Units), "Desempenho máximo");
        c.SleepUnits = OneOf(c.SleepUnits, Sleep, MouseConfig.SleepOptions.Select(s => s.Units), "Dormir após");
        c.DebounceMs = Pick(c.DebounceMs, Debounce, MouseConfig.DebounceMin, MouseConfig.DebounceMax);
        c.StageCount = Pick(c.StageCount, StageCount, 1, Flash.MaxUserStages);
        c.CurrentStage = Math.Clamp(CurrentStage, 0, c.StageCount - 1);
        for (int i = 0; i < Flash.MaxStages && i < Dpi.Length; i++)
            if (Dpi[i] != c.Dpi[i]) c.Dpi[i] = MouseConfig.SnapDpi(Dpi[i]);
        for (int i = 0; i < Flash.MaxStages && i < Colors.Length; i++) c.DpiColors[i] = ColorTranslator.FromHtml(Colors[i]);
        c.DpiLedEffect = Pick(c.DpiLedEffect, LedEffect, 0, 2);
        if (LedBrightness != MouseConfig.BrightnessLevelOf(c.DpiLedBrightness))
            c.DpiLedBrightness = MouseConfig.BrightnessLevels[Math.Clamp(LedBrightness, 1, 10) - 1];
        c.DpiLedSpeed = Pick(c.DpiLedSpeed, LedSpeed, MouseConfig.LedSpeedMin, MouseConfig.LedSpeedMax);
        for (int i = 0; i < c.Buttons.Length && i < Buttons.Length; i++)
            if (ButtonOption.All.FirstOrDefault(o => o.Id == Buttons[i]) is { } opt) c.Buttons[i] = opt.ToAction();
        if (!c.Buttons.Any(b => b.Type == ButtonType.Mouse && b.P1 == 0x01))
            throw new ArgumentException("Pelo menos um botão precisa continuar como clique esquerdo.");
    }
}

/// <summary>
/// Ponte JS ↔ C#. Mensagens: { id, cmd, args } → { id, ok, result | error }.
/// Eventos empurrados: { event: "log" | "progress", ... }.
/// </summary>
public sealed partial class Bridge
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly Action<string> _post;
    readonly Func<Func<string?>, string?> _onUi; // executa diálogos na thread de UI
    readonly string _backupDir = Path.Combine(AppContext.BaseDirectory, "backups");
    readonly SemaphoreSlim _lock = new(1, 1);
    MouseDevice? _dev;
    BatteryInfo? _battery;
    ushort? _version;
    (byte Cid, byte Mid)? _model;
    byte[]? _dongleLight;   // registro 0x15 do receptor (null pelo cabo ou se o receptor não tiver)

    // MIDs do Config.ini do app oficial na ordem Drive1..18 → foto img/mouse{i}.png.
    // O app oficial lê os MIDs como DECIMAL (QString::toInt base 10). Drive1–8 (índice 0–7) usam o
    // sensor PAW3950 com DPI 50–42.000; Drive9+ usam o 3955, com outra codificação de DPI.
    static readonly string[] ModelMids = ["01","02","03","04","05","10","11","12","13","14","15","16","17","18","19","20","21","22"];
    public static int? ModelIndex(byte mid)
    {
        int i = Array.FindIndex(ModelMids, s => int.Parse(s) == mid);
        return i >= 0 ? i : null;
    }
    public static bool IsPaw3950Model(int? index) => index is >= 0 and <= 7;
    bool DpiSupported => IsPaw3950Model(_model is { } m ? ModelIndex(m.Mid) : null);

    readonly AppSettings _settings;
    readonly Action<AppSettings> _applyAppearance;

    public Bridge(Action<string> post, Func<Func<string?>, string?> onUi, AppSettings settings, Action<AppSettings> applyAppearance)
    {
        _post = post; _onUi = onUi; _settings = settings; _applyAppearance = applyAppearance;
    }

    /// <summary>
    /// Chamado (com debounce) quando o Windows avisa que um dispositivo entrou ou saiu.
    /// Conecta sozinho se o X8 apareceu; avisa a interface se o mouse aberto sumiu.
    /// </summary>
    public async Task OnDevicesChangedAsync()
    {
        await _lock.WaitAsync();
        try
        {
            object? evt;
            try { evt = await Task.Run<object?>(Check); }
            catch (Exception ex) { evt = new { @event = "notice", text = ex.Message }; }
            if (evt is not null) _post(JsonSerializer.Serialize(evt, Json));
        }
        finally { _lock.Release(); }

        object? Check()
        {
            if (_dev is not null)
            {
                bool present = _dev.DevicePath is null ||
                    WinHid.Enumerate(WinHid.AttackSharkVid).Any(d => string.Equals(d.Path, _dev.DevicePath, StringComparison.OrdinalIgnoreCase));
                if (present) return Available();
                Log("Mouse desconectado.");
                _dev.Dispose(); _dev = null; _model = null;
                return new { @event = "device", state = (object?)null };
            }
            if (_kb is not null)
            {
                bool present = _kb.DevicePath is null ||
                    WinHid.Enumerate(WinHid.KeyboardVid).Any(d => string.Equals(d.Path, _kb.DevicePath, StringComparison.OrdinalIgnoreCase));
                if (present) return Available();
                Log("Teclado desconectado.");
                _kb.Dispose(); _kb = null;
                return new { @event = "device", state = (object?)null };
            }
            // nada aberto: só atualiza a tela inicial (o usuário escolhe em qual dispositivo entrar)
            return Available();
        }
    }

    /// <summary>
    /// O que está ligado agora, para a tela inicial. Só enumera o Windows: não abre nem fala com
    /// nenhum dispositivo. mouse: null (ausente), "cable" ou "dongle".
    /// </summary>
    object Available()
    {
        var m = WinHid.FindMouse(WinHid.Enumerate(WinHid.AttackSharkVid));
        return new
        {
            @event = "available",
            mouse = m is null ? null : WinHid.IsWirelessPid(m.Value.Out.Pid) ? "dongle" : "cable",
            keyboard = WinHid.FindKeyboard(WinHid.Enumerate(WinHid.KeyboardVid)) is not null,
            open = _dev is not null ? "mouse" : _kb is not null ? "keyboard" : null,
        };
    }

    /// <summary>Fecha o dispositivo aberto e volta para a tela inicial.</summary>
    object Disconnect()
    {
        _dev?.Dispose(); _dev = null; _model = null;
        _kb?.Dispose(); _kb = null;
        return Available();
    }

    /// <summary>Fecha o que estiver aberto e abre o dispositivo escolhido ("mouse" ou "keyboard").</summary>
    object? SwitchTo(string kind)
    {
        Disconnect();
        return (kind == "keyboard" ? TryAutoConnectKeyboard() : TryAutoConnectMouse())
               ?? throw new InvalidOperationException(kind == "keyboard"
                   ? "Nenhum X68 HE encontrado. Conecte o teclado pelo cabo."
                   : "Nenhum X8 encontrado. Conecte o receptor 8K ou o cabo do mouse.");
    }

    /// <summary>Recarrega o dispositivo que já está aberto (ou null se nenhum está).</summary>
    object? TryAutoConnect() => _kb is not null ? KbRead() : _dev is not null ? Read() : null;

    object? TryAutoConnectMouse()
    {
        if (_dev is not null) return Read();
        if (WinHid.FindMouse(WinHid.Enumerate(WinHid.AttackSharkVid)) is null) return null;
        MouseDevice? dev = null;
        try
        {
            dev = MouseDevice.TryOpenReal(out var message);
            if (dev is null) return null;
            Log(message);
            // Pelo receptor, o mouse pode estar dormindo, desligado ou sem bateria: o receptor responde
            // (0x03, 0xF1), mas os pedidos que vão até o mouse ficam sem resposta.
            // A versão (0x12) desempata caso o byte de "online" não signifique o que parece.
            if (dev.Description.Contains("dongle") && dev.ReadOnline() == false && dev.ReadVersion() is null)
                throw new MouseAsleepException();
            var d = dev; dev = null;
            return Connect(d);
        }
        catch (MouseAsleepException)
        {
            dev?.Dispose();
            throw new InvalidOperationException("O receptor 8K está conectado, mas o mouse não responde. Ligue o mouse ou mexa nele para acordá-lo, " +
                "e confira a bateria. Depois clique no mouse de novo.");
        }
        catch (Exception ex)
        {
            dev?.Dispose();
            _dev?.Dispose(); _dev = null;
            throw new InvalidOperationException("O X8 foi encontrado, mas não respondeu. Feche o app oficial da Attack Shark, mexa no mouse para acordá-lo e tente de novo. (" + ex.Message + ")");
        }
    }

    sealed class MouseAsleepException : Exception;

    /// <summary>Bateria para a bandeja: nome do mouse, %, se está dormindo e se está ligado.</summary>
    public sealed record BatteryStatus(bool Present, bool Asleep, int? Percent, bool Wireless);
    BatteryStatus? _lastTray;

    /// <summary>
    /// Lê a bateria do X8 para a bandeja (a cada minuto). Usa o mouse aberto na interface, se houver; senão
    /// abre e fecha o dispositivo só para essa leitura. Pelo receptor confere antes se o mouse está acordado
    /// (0x03), para não ficar mandando pedidos a um mouse dormindo. Atualiza a barra lateral se a
    /// porcentagem mudou com o mouse aberto.
    /// </summary>
    public async Task<BatteryStatus> PollBatteryAsync()
    {
        await _lock.WaitAsync();
        try { return await Task.Run(Poll); }
        catch { return _lastTray ?? new BatteryStatus(false, false, null, false); }
        finally { _lock.Release(); }

        BatteryStatus Poll()
        {
            var pick = WinHid.FindMouse(WinHid.Enumerate(WinHid.AttackSharkVid));
            if (pick is null) return _lastTray = new BatteryStatus(false, false, null, false);
            bool wireless = WinHid.IsWirelessPid(pick.Value.Out.Pid);
            MouseDevice? temp = null;
            var dev = _dev;
            try
            {
                if (dev is null) { dev = temp = MouseDevice.TryOpenReal(out _); if (dev is null) return _lastTray = new BatteryStatus(false, false, null, false); }
                if (wireless && dev.ReadOnline() == false)
                    return _lastTray = new BatteryStatus(true, true, _lastTray?.Percent, true);
                var b = dev.ReadBattery();
                if (b is not null && _dev is not null && _battery?.Percent != b.Percent)
                {
                    _battery = b;
                    _post(JsonSerializer.Serialize(new { @event = "battery", battery = new { percent = b.Percent, millivolts = b.Millivolts } }, Json));
                }
                return _lastTray = new BatteryStatus(true, false, b?.Percent ?? _lastTray?.Percent, wireless);
            }
            finally { temp?.Dispose(); }
        }
    }

    object SetStartup(JsonNode? args)
    {
        Startup.Set(args?.GetValue<bool>() ?? false);
        return Startup.IsEnabled;
    }

    object SetAppearance(JsonNode? args)
    {
        var s = args.Deserialize<AppSettings>(Json) ?? new AppSettings();
        AppSettings.Normalize(s);
        _settings.Theme = s.Theme; _settings.Transparency = s.Transparency; _settings.ReduceMotion = s.ReduceMotion;
        _applyAppearance(_settings);   // moldura nativa primeiro (acompanha o crossfade), arquivo depois
        _settings.Save();
        return _settings;
    }

    public async Task HandleAsync(string message)
    {
        var msg = JsonNode.Parse(message)!;
        var id = msg["id"]?.GetValue<int>() ?? 0;
        var cmd = msg["cmd"]?.GetValue<string>() ?? "";
        var args = msg["args"];
        object? result; string? error = null;
        await _lock.WaitAsync();
        try { result = await Task.Run(() => Dispatch(cmd, args)); }
        catch (Exception ex) { result = null; error = ex.Message; Log("Erro: " + ex.Message); }
        finally { _lock.Release(); }
        _post(JsonSerializer.Serialize(new { id, ok = error is null, result, error }, Json));
    }

    object? Dispatch(string cmd, JsonNode? args) => cmd switch
    {
        "init" => Init(),
        "connectReal" => ConnectReal(),
        "autoConnect" => TryAutoConnect(),
        "available" => Available(),
        "disconnect" => Disconnect(),
        "setStartup" => SetStartup(args),
        "switchTo" => SwitchTo(args?.GetValue<string>() ?? "mouse"),
        "setAppearance" => SetAppearance(args),
        "read" => _kb is not null ? KbRead() : Read(),
        "preview" => _kb is not null ? KbPreview(KbDto(args)) : Preview(Dto(args)),
        "apply" => _kb is not null ? KbApply(KbDto(args)) : Apply(Dto(args)),
        "exportBackup" => _kb is not null ? _kb.SaveBackup(_backupDir)
            : _dev?.Image is { } img ? MouseDevice.SaveBackup(img, _backupDir) : throw new InvalidOperationException("Leia o mouse antes de exportar."),
        "importBackup" => _kb is not null ? KbImportBackup() : ImportBackup(),
        _ => throw new InvalidOperationException("comando desconhecido: " + cmd),
    };

    static ConfigDto Dto(JsonNode? n) => n.Deserialize<ConfigDto>(Json) ?? throw new ArgumentException("config ausente");
    static KbConfigDto KbDto(JsonNode? n) => n.Deserialize<KbConfigDto>(Json) ?? throw new ArgumentException("config ausente");

    object Init() => new
    {
        pollingRates = MouseConfig.PollingRates.Select(p => new { code = p.Code, hz = p.Hz }),
        sleepOptions = MouseConfig.SleepOptions.Select(s => new { units = s.Units, label = s.Label }),
        lod = MouseConfig.LodLabels,
        sensorModes = new[] { "LP", "HP", "Corded" },
        ledEffects = MouseConfig.DpiLedEffects,
        buttons = ButtonOption.All.Select(b => new { b.Id, b.Group, b.Label }),
        dongleStatusModes = MouseDevice.DongleStatusModes.Select(m => new { mode = m.Mode, label = m.Label, info = m.Info }),
        dpi = new { min = MouseConfig.DpiMin, max = MouseConfig.DpiMax, seg1 = MouseConfig.DpiSeg1Max, maxStages = Flash.MaxUserStages },
        limits = new
        {
            debounce = new { min = MouseConfig.DebounceMin, max = MouseConfig.DebounceMax },
            ledSpeed = new { min = MouseConfig.LedSpeedMin, max = MouseConfig.LedSpeedMax },
            brightnessLevels = MouseConfig.BrightnessLevels.Length,
        },
        version = typeof(Bridge).Assembly.GetName().Version?.ToString(3),
        appearance = _settings,
        startup = Startup.IsEnabled,
        keyboard = KbInit(),
    };

    object? ConnectReal()
    {
        var dev = MouseDevice.TryOpenReal(out var message);
        Log(message);
        if (dev is null) throw new InvalidOperationException(
            "Nenhum X8 Ultra encontrado. Conecte o dongle 8K ou o cabo USB-C e feche o app oficial.");
        return Connect(dev);
    }

    object? Connect(MouseDevice dev)
    {
        _dev?.Dispose();
        _dev = dev;
        _model = null;
        _dev.Log += Log;
        return Read();
    }

    object Read()
    {
        var dev = _dev ?? throw new InvalidOperationException("Nenhum mouse conectado.");
        _version = dev.ReadVersion();
        _battery = dev.ReadBattery();
        _model ??= dev.ReadModelId();
        _dongleLight = dev.Description.Contains("dongle") ? dev.ReadDongleLight() : null;
        int last = -1;
        var img = dev.ReadFlash(new SyncProgress(v => { if (v != last) { last = v; _post(JsonSerializer.Serialize(new { @event = "progress", value = v }, Json)); } }));
        var cfg = MouseConfig.FromImage(img);
        return State(cfg, img);
    }

    object State(MouseConfig cfg, FlashImage img) => new
    {
        kind = "mouse",
        device = new
        {
            description = _dev!.Description,
            battery = _battery is null ? null : new { percent = _battery.Percent, millivolts = _battery.Millivolts },
            firmware = _version is ushort v ? $"{v >> 8}.{v & 0xFF:D2}" : null,
            model = _model is { } md ? ModelIndex(md.Mid) : null,
            dpiSupported = DpiSupported,
            dongleLight = _dongleLight is not null,
        },
        config = WithDongle(ConfigDto.From(cfg)),
        warnings = cfg.Warnings,
        hex = HexDump(img),
    };

    /// <summary>Monta a config a gravar, recusando DPI quando o sensor do modelo não é o 3950.</summary>
    MouseConfig Edited(ConfigDto dto)
    {
        var img = _dev?.Image ?? throw new InvalidOperationException("Leia o mouse antes de aplicar.");
        var before = MouseConfig.FromImage(img);
        var cfg = MouseConfig.FromImage(img); dto.ApplyTo(cfg);
        if (!DpiSupported && !before.Dpi.SequenceEqual(cfg.Dpi))
            throw new InvalidOperationException(
                "O modelo deste mouse não foi identificado como PAW3950; por segurança o DPI não é gravado.");
        return cfg;
    }

    ConfigDto WithDongle(ConfigDto d) { d.DongleStatus = _dongleLight?[0] ?? 0; return d; }

    /// <summary>Novo modo do LED do receptor, se mudou (só entre os modos que o app oficial oferece).</summary>
    byte? DongleChange(ConfigDto dto) =>
        _dongleLight is { } cur && dto.DongleStatus != cur[0] && MouseDevice.DongleStatusModes.Any(m => m.Mode == dto.DongleStatus)
            ? (byte)dto.DongleStatus : null;

    object Preview(ConfigDto dto)
    {
        var img = _dev!.Image!;
        var cfg = Edited(dto);
        var ops = MouseConfig.Diff(img, cfg.ApplyTo(img)).Select(o => new { address = $"0x{o.Address:X4}", bytes = Packet.Hex(o.Bytes) }).ToList();
        if (DongleChange(dto) is byte mode)
        {
            var data = (byte[])_dongleLight!.Clone(); data[0] = mode;
            ops.Add(new { address = "receptor", bytes = Packet.Hex(Packet.Build(Cmd.SetDongleRgb4K, 0, data, 0x0A)) });
        }
        return ops;
    }

    object Apply(ConfigDto dto)
    {
        var dev = _dev ?? throw new InvalidOperationException("Nenhum mouse conectado.");
        var cfg = Edited(dto);
        int n = 0;
        if (DongleChange(dto) is byte mode) { dev.WriteDongleStatus(mode); n++; }
        n += dev.Apply(cfg, _backupDir);
        Log($"{n} escrita(s) confirmadas. Backup em {_backupDir}");
        var state = Read();
        return new { count = n, state };
    }

    object? ImportBackup()
    {
        var path = _onUi(() =>
        {
            using var dlg = new OpenFileDialog { Filter = "Backup SharkConfig|*.json", InitialDirectory = _backupDir };
            return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
        });
        return path is null ? null : ConfigDto.From(MouseConfig.FromImage(MouseDevice.LoadBackup(path)));
    }

    static string HexDump(FlashImage img)
    {
        var sb = new StringBuilder();
        foreach (var (start, len) in Flash.ReadRegions)
            for (int a = start; a < start + len; a += 16)
                sb.AppendLine($"{a:X4}  {Packet.Hex(img.Data.AsSpan(a, Math.Min(16, start + len - a)))}");
        return sb.ToString();
    }

    void Log(string text) => _post(JsonSerializer.Serialize(new { @event = "log", text, time = DateTime.Now.ToString("HH:mm:ss") }, Json));

    sealed class SyncProgress(Action<int> a) : IProgress<int> { public void Report(int v) => a(v); }
}
