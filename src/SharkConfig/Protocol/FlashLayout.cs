namespace SharkConfig.Protocol;

/// <summary>
/// Endereços da flash de configuração (docs/PROTOCOL.md).
/// Escalares = par [v, 0x55-v]. Registros = N bytes + checksum (0x55 - soma).
/// </summary>
public static class Flash
{
    public const int Size = 0x2000;

    // escalares (par de 2 bytes)
    public const int PollingRate = 0x00;
    public const int StageCount = 0x02;
    public const int CurrentStage = 0x04;
    public const int Unknown06 = 0x06;
    public const int Unknown08 = 0x08;
    public const int Lod = 0x0A;
    public const int Debounce = 0xA9;
    public const int MotionSync = 0xAB;
    public const int SleepTime = 0xAD;
    public const int AngleSnap = 0xAF;
    public const int Ripple = 0xB1;
    public const int UnknownB3 = 0xB3;
    public const int Performance = 0xB5;
    public const int PerformanceTimeout = 0xB7;
    public const int SensorMode = 0xB9;

    // LED do DPI: 4 pares em 0x4C,0x4E,0x50,0x52
    // 0x52 = "LED ligado": o app oficial grava 1 se o efeito ≠ desligado, senão 0 (@0x140140f80).
    public const int DpiLedEffect = 0x4C, DpiLedBrightness = 0x4E, DpiLedSpeed = 0x50, DpiLedOn = 0x52;

    public const int MaxStages = 8;       // registros de DPI que existem na memória
    public const int MaxUserStages = 6;   // limite do modelo (DPIMaxGrade=6 no Config.ini oficial)
    public const int ButtonSlots = 16;

    public static int DpiLegacy(int i) => 0x0C + 4 * i;      // 3 bytes + chk
    public static int DpiColor(int i) => 0x2C + 4 * i;       // R,G,B + chk
    public static int Button(int i) => 0x60 + 4 * i;         // tipo,p1,p2 + chk
    public static int Dpi(int i) => 0x1B00 + 6 * i;          // Xlo,Xhi,Ylo,Yhi,flag + chk
    // Atalhos/combinações (0x100+) e macros (0x300+) existem na memória, mas o app não os usa.

    /// <summary>Regiões lidas do mouse (em blocos de 10 bytes).</summary>
    public static readonly (int Start, int Length)[] ReadRegions =
    [
        (0x000, 0x0F0),             // escalares, DPI legado, cores, LED, botões, sensor
        (0x1B00, 6 * MaxStages),    // tabela de DPI X/Y
    ];

    /// <summary>Faixas onde o app tem permissão de escrever (campos mapeados [O]).</summary>
    public static readonly (int Start, int End)[] WritableRanges =
    [
        (0x00, 0x0B), (0x0C, 0x2B), (0x2C, 0x4B),   // 0x0C–0x2B: DPI legado (firmwares com 0x1B00 apagada)
        (0x4C, 0x53), (0x60, 0x9F),
        (0xA9, 0xB2), (0xB5, 0xBA),   // 0xB3 (desconhecido) fica de fora
        (0x1B00, 0x1B2F),
    ];

    public static bool IsWritable(int start, int length) =>
        WritableRanges.Any(r => start >= r.Start && start + length - 1 <= r.End);
}

/// <summary>Imagem em memória da flash com helpers de par/registro.</summary>
public sealed class FlashImage
{
    public byte[] Data { get; }
    public bool[] Loaded { get; }

    public FlashImage() { Data = new byte[Flash.Size]; Loaded = new bool[Flash.Size]; }
    private FlashImage(byte[] d, bool[] l) { Data = d; Loaded = l; }
    public FlashImage Clone() => new((byte[])Data.Clone(), (bool[])Loaded.Clone());

    public void Load(int address, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(Data.AsSpan(address));
        for (int i = 0; i < bytes.Length; i++) Loaded[address + i] = true;
    }

    public static byte Pair(byte v) => (byte)(0x55 - v);

    public byte? GetScalar(int addr) => Data[addr + 1] == Pair(Data[addr]) ? Data[addr] : null;
    public void SetScalar(int addr, byte v) { Data[addr] = v; Data[addr + 1] = Pair(v); }

    public byte[]? GetRecord(int addr, int n)
    {
        var span = Data.AsSpan(addr, n);
        return Data[addr + n] == Packet.Checksum(span) ? span.ToArray() : null;
    }
    public void SetRecord(int addr, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(Data.AsSpan(addr));
        Data[addr + bytes.Length] = Packet.Checksum(bytes);
    }
}
