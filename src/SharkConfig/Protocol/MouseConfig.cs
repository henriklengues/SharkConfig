using System.Drawing;

namespace SharkConfig.Protocol;

public enum ButtonType : byte
{
    Disabled = 0x00, Mouse = 0x01, Dpi = 0x02, HScroll = 0x03, Fire = 0x04, Combo = 0x05,
    Macro = 0x06, PollingSwitch = 0x07, ConfigSwitch = 0x09, Undefined = 0x10, VScroll = 0x0B, DpiLock = 0x0C,
}

public record struct ButtonAction(ButtonType Type, byte P1, byte P2);

/// <summary>
/// Modelo legível da configuração. Parse a partir da <see cref="FlashImage"/> e
/// aplicação de volta (só os campos que mudaram viram escrita — ver <see cref="Diff"/>).
/// </summary>
public sealed class MouseConfig
{
    // Polling rate: código → Hz (moc jump table RVA 0x1b6958)
    public static readonly (byte Code, int Hz)[] PollingRates =
        [(0x08, 125), (0x04, 250), (0x02, 500), (0x01, 1000), (0x10, 2000), (0x20, 4000), (0x40, 8000)];

    // unidades de 10 s (índice da UI → valor gravado) @0x1401516b0
    public static readonly (byte Units, string Label)[] SleepOptions =
        [(1, "10 s"), (3, "30 s"), (6, "1 min"), (12, "2 min"), (18, "3 min"), (30, "5 min"),
         (60, "10 min"), (90, "15 min"), (180, "30 min"), (210, "35 min")];

    public static readonly string[] LodLabels = ["0.7 mm", "1 mm", "2 mm"];
    public static readonly string[] SensorModeLabels = ["LP (normal)", "HP (alto desempenho)", "Corded (ultra)"];
    public static readonly string[] DpiLedEffects = ["Desligado", "Fixo", "Respiração"];

    // Limites dos controles do app oficial (QSlider/QSpinBox setMinimum/setMaximum).
    public const int DebounceMin = 1, DebounceMax = 20;      // @0x14014fe5d
    public const int LedSpeedMin = 1, LedSpeedMax = 5;       // @0x14014b555
    // Brilho: slider de 10 níveis → valor gravado (@0x14014a84a). Nível 5 = 0x80 (padrão).
    public static readonly byte[] BrightnessLevels = [16, 30, 60, 90, 128, 150, 180, 210, 230, 255];
    public static int BrightnessLevelOf(int raw)
    {
        int best = 0;
        for (int i = 1; i < BrightnessLevels.Length; i++)
            if (Math.Abs(BrightnessLevels[i] - raw) < Math.Abs(BrightnessLevels[best] - raw)) best = i;
        return best + 1;
    }

    public const int DpiMin = 50, DpiSeg1Max = 30000, DpiMax = 42000;
    public const byte DpiSeg2Flag = 0x11;
    public const bool SensorHighBitsQuirk = true; // 3950/3395 (@0x140018431)

    public byte PollingRateCode = 0x01;
    public int StageCount = 6;
    public int CurrentStage = 2;
    public int Lod;
    public int DebounceMs = 8;
    public bool MotionSync = true;
    public byte SleepUnits = 6;
    public bool AngleSnap;
    public bool Ripple;
    public bool Performance;
    public byte PerformanceTimeoutUnits = 6;
    public int SensorMode;

    public int[] Dpi = [1200, 2400, 3200, 5600, 8000, 42000, 6000, 8000];
    public Color[] DpiColors =
    [
        Color.FromArgb(255, 0, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 0, 255), Color.FromArgb(255, 255, 0),
        Color.FromArgb(0, 255, 255), Color.FromArgb(255, 0, 255), Color.FromArgb(0, 255, 255), Color.FromArgb(255, 255, 255),
    ];
    public int DpiLedEffect = 2, DpiLedBrightness = 0x80, DpiLedSpeed = 3;
    public ButtonAction[] Buttons = new ButtonAction[6];

    /// <summary>Campos que vieram inválidos (checksum) na leitura — avisados na UI.</summary>
    public List<string> Warnings { get; } = [];

    public int PollingHz => PollingRates.FirstOrDefault(p => p.Code == PollingRateCode).Hz;

    // ---------------- DPI (docs/PROTOCOL.md) ----------------
    public static (ushort Raw, byte Flag) EncodeDpi(int dpi)
    {
        dpi = Math.Clamp(dpi, DpiMin, DpiMax);
        int div = dpi > DpiSeg1Max ? 2 : 1;
        byte flag = div == 2 ? DpiSeg2Flag : (byte)0;
        int raw = dpi / div - 1;
        if (SensorHighBitsQuirk)
        {
            int h = raw >> 8;
            flag |= (byte)((((h << 4) | h) << 2) & 0xFF);
        }
        return ((ushort)raw, flag);
    }

    public static int DecodeDpi(ushort raw, byte flag) => (raw + 1) * ((flag & 0x01) != 0 ? 2 : 1);

    /// <summary>
    /// Registro legado em 0x0C + 4i: [X baixo, Y baixo, flag] em unidades de 50 DPI, com os bits altos
    /// no flag (bits 2–3) e o divisor 2 no bit 0, como no formato novo. Firmwares que deixam a tabela
    /// 0x1B00 apagada (toda 0xFF) guardam o DPI só aqui [H: 800, 2400, 3200, 5600, 8000, 42000].
    /// </summary>
    public static int DecodeLegacyDpi(byte lo, byte flag) =>
        ((lo | ((flag >> 2) & 0x03) << 8) + 1) * 50 * ((flag & 0x01) != 0 ? 2 : 1);

    /// <summary>
    /// Inverso de DecodeLegacyDpi, igual ao app oficial para 3950/3395: raw = dpi/(50·div) − 1;
    /// flag = 0x11 se div 2, mais os bits altos repetidos (h·0x44). Ex.: 1200 → 17 17 00, 42000 → A3 A3 55.
    /// </summary>
    public static (byte Lo, byte Flag) EncodeLegacyDpi(int dpi)
    {
        dpi = Math.Clamp(dpi, DpiMin, DpiMax);
        int div = dpi > DpiSeg1Max ? 2 : 1;
        int raw = dpi / (50 * div) - 1;
        byte flag = div == 2 ? DpiSeg2Flag : (byte)0;
        int h = raw >> 8;
        flag |= (byte)((((h << 4) | h) << 2) & 0xFF);
        return ((byte)raw, flag);
    }

    /// <summary>true quando o DPI veio do registro legado (a tabela 0x1B00 está apagada).</summary>
    public bool DpiFromLegacy;

    /// <summary>
    /// true quando todo registro de DPI lido é reproduzido byte a byte pelo codificador do PAW3950
    /// (X = Y, flag igual): prova que o firmware usa essa codificação mesmo sem o modelo identificado.
    /// </summary>
    public bool DpiEncodingVerified = true;

    public static int SnapDpi(int dpi)
    {
        dpi = Math.Clamp(dpi, DpiMin, DpiMax);
        int step = dpi > DpiSeg1Max ? 100 : 50;
        return (int)Math.Round(dpi / (double)step) * step;
    }

    // ---------------- parse ----------------
    public static MouseConfig FromImage(FlashImage img)
    {
        var c = new MouseConfig();
        byte S(int addr, string name, byte def)
        {
            var v = img.GetScalar(addr);
            if (v is null) c.Warnings.Add($"{name} (0x{addr:X2}) inválido; usando padrão {def}");
            return v ?? def;
        }
        c.PollingRateCode = S(Flash.PollingRate, "Polling rate", 0x01);
        c.StageCount = Math.Clamp((int)S(Flash.StageCount, "Nº estágios", 6), 1, Flash.MaxStages);
        c.CurrentStage = Math.Clamp((int)S(Flash.CurrentStage, "Estágio atual", 2), 0, c.StageCount - 1);
        c.Lod = Math.Clamp((int)S(Flash.Lod, "LOD", 0), 0, 2);
        c.DebounceMs = S(Flash.Debounce, "Debounce", 8);
        c.MotionSync = S(Flash.MotionSync, "Motion Sync", 1) > 0;
        c.SleepUnits = S(Flash.SleepTime, "Sleep", 6);
        c.AngleSnap = S(Flash.AngleSnap, "Angle snap", 0) > 0;
        c.Ripple = S(Flash.Ripple, "Ripple", 0) > 0;
        c.Performance = S(Flash.Performance, "Desempenho", 0) > 0;
        c.PerformanceTimeoutUnits = S(Flash.PerformanceTimeout, "Timeout desempenho", 6);
        c.SensorMode = Math.Clamp((int)S(Flash.SensorMode, "Modo sensor", 0), 0, 2);
        c.DpiLedEffect = S(Flash.DpiLedEffect, "LED efeito", 2);
        c.DpiLedBrightness = S(Flash.DpiLedBrightness, "LED brilho", 0x80);
        c.DpiLedSpeed = S(Flash.DpiLedSpeed, "LED velocidade", 3);

        for (int i = 0; i < Flash.MaxStages; i++)
        {
            var d = img.GetRecord(Flash.Dpi(i), 5);
            if (d is not null)
            {
                c.Dpi[i] = DecodeDpi((ushort)(d[0] | d[1] << 8), d[4]);
                var (raw, flag) = EncodeDpi(c.Dpi[i]);
                if (d[0] != (byte)raw || d[1] != (byte)(raw >> 8) || d[2] != d[0] || d[3] != d[1] || d[4] != flag)
                    c.DpiEncodingVerified = false;
            }
            else if (IsErased(img, Flash.Dpi(i), 6) && img.GetRecord(Flash.DpiLegacy(i), 3) is { } l)
            {
                c.Dpi[i] = DecodeLegacyDpi(l[0], l[2]);
                c.DpiFromLegacy = true;
                var (lo, flag) = EncodeLegacyDpi(c.Dpi[i]);
                if (l[0] != lo || l[1] != lo || l[2] != flag) c.DpiEncodingVerified = false;
            }
            else
            {
                c.DpiEncodingVerified = false;
                if (i < c.StageCount) c.Warnings.Add($"DPI {i + 1} inválido");
            }
            var col = img.GetRecord(Flash.DpiColor(i), 3);
            if (col is not null) c.DpiColors[i] = Color.FromArgb(col[0], col[1], col[2]);
        }
        for (int i = 0; i < c.Buttons.Length; i++)
        {
            var b = img.GetRecord(Flash.Button(i), 3);
            if (b is null) { c.Warnings.Add($"Botão {i + 1} inválido"); continue; }
            c.Buttons[i] = new ButtonAction((ButtonType)b[0], b[1], b[2]);
        }
        return c;
    }

    static bool IsErased(FlashImage img, int addr, int len) => img.Data.AsSpan(addr, len).IndexOfAnyExcept((byte)0xFF) < 0;

    // ---------------- aplicar ----------------
    public FlashImage ApplyTo(FlashImage baseImg)
    {
        var img = baseImg.Clone();
        img.SetScalar(Flash.PollingRate, PollingRateCode);
        img.SetScalar(Flash.StageCount, (byte)StageCount);
        img.SetScalar(Flash.CurrentStage, (byte)Math.Min(CurrentStage, StageCount - 1));
        img.SetScalar(Flash.Lod, (byte)Lod);
        img.SetScalar(Flash.Debounce, (byte)DebounceMs);
        img.SetScalar(Flash.MotionSync, (byte)(MotionSync ? 1 : 0));
        img.SetScalar(Flash.SleepTime, SleepUnits);
        img.SetScalar(Flash.AngleSnap, (byte)(AngleSnap ? 1 : 0));
        img.SetScalar(Flash.Ripple, (byte)(Ripple ? 1 : 0));
        img.SetScalar(Flash.Performance, (byte)(Performance ? 1 : 0));
        img.SetScalar(Flash.PerformanceTimeout, PerformanceTimeoutUnits);
        img.SetScalar(Flash.SensorMode, (byte)SensorMode);
        img.SetScalar(Flash.DpiLedEffect, (byte)DpiLedEffect);
        img.SetScalar(Flash.DpiLedBrightness, (byte)DpiLedBrightness);
        img.SetScalar(Flash.DpiLedSpeed, (byte)DpiLedSpeed);
        img.SetScalar(Flash.DpiLedOn, (byte)(DpiLedEffect == 0 ? 0 : 1));

        var old = FromImage(baseImg);
        for (int i = 0; i < Flash.MaxStages; i++)
        {
            // Só reescreve DPI se o valor mudou (preserva bytes originais do firmware). Antes, um
            // registro inválido também era reescrito — com a tabela 0x1B00 apagada isso gravava os 8
            // registros em toda aplicação, mesmo sem mudar o DPI.
            if (old.Dpi[i] != Dpi[i])
            {
                if (old.DpiFromLegacy)
                {
                    // firmware com a tabela 0x1B00 apagada: o DPI vive só no registro legado 0x0C+4i
                    var (lo, lflag) = EncodeLegacyDpi(Dpi[i]);
                    img.SetRecord(Flash.DpiLegacy(i), [lo, lo, lflag]);
                }
                else
                {
                    var (raw, flag) = EncodeDpi(Dpi[i]);
                    img.SetRecord(Flash.Dpi(i), [(byte)raw, (byte)(raw >> 8), (byte)raw, (byte)(raw >> 8), flag]);
                }
            }
            var c = DpiColors[i];
            img.SetRecord(Flash.DpiColor(i), [c.R, c.G, c.B]);
        }
        for (int i = 0; i < Buttons.Length; i++)
        {
            var b = Buttons[i];
            if (old.Buttons[i] == b) continue;
            img.SetRecord(Flash.Button(i), [(byte)b.Type, b.P1, b.P2]);
        }
        return img;
    }

    /// <summary>Lista de escritas (endereço, bytes) por campo alterado; ≤10 bytes cada.</summary>
    public static List<(int Address, byte[] Bytes)> Diff(FlashImage before, FlashImage after)
    {
        var fields = new List<(int, int)>();
        foreach (var a in new[] { Flash.PollingRate, Flash.StageCount, Flash.CurrentStage, Flash.Lod,
                     Flash.DpiLedEffect, Flash.DpiLedBrightness, Flash.DpiLedSpeed, Flash.DpiLedOn, Flash.Debounce, Flash.MotionSync,
                     Flash.SleepTime, Flash.AngleSnap, Flash.Ripple, Flash.Performance, Flash.PerformanceTimeout,
                     Flash.SensorMode })
            fields.Add((a, 2));
        for (int i = 0; i < Flash.MaxStages; i++)
        {
            fields.Add((Flash.DpiLegacy(i), 4));   // DPI em firmwares com a tabela 0x1B00 apagada
            fields.Add((Flash.DpiColor(i), 4));
            fields.Add((Flash.Dpi(i), 6));
        }
        for (int i = 0; i < 6; i++) fields.Add((Flash.Button(i), 4));

        var ops = new List<(int, byte[])>();
        foreach (var (addr, len) in fields)
        {
            if (before.Data.AsSpan(addr, len).SequenceEqual(after.Data.AsSpan(addr, len))) continue;
            for (int off = 0; off < len; off += Packet.MaxData)
            {
                int n = Math.Min(Packet.MaxData, len - off);
                ops.Add((addr + off, after.Data.AsSpan(addr + off, n).ToArray()));
            }
        }
        return ops;
    }
}
