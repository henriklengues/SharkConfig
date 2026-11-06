using System.Runtime.InteropServices;
using SharkConfig.Transport;

namespace SharkConfig;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--list"))
        {
            AttachConsole(-1);
            foreach (var d in WinHid.Enumerate()) Console.WriteLine($"{d}\n    {d.Path}");
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--render-tray-icon")
        {
            // salva o ícone da bandeja de uma porcentagem como PNG (para conferir o desenho)
            using var ico = UI.TrayBattery.Render(int.Parse(args[1]));
            using var bmp = ico.ToBitmap();
            bmp.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
            return 0;
        }
        if (args.Contains("--mouse-probe"))
        {
            // só leitura: abre o X8 e lê a memória com o registro de pacotes
            AttachConsole(-1);
            using var dev = MouseDevice.TryOpenReal(out var msg);
            Console.WriteLine(msg);
            if (dev is null) return 1;
            if (args.Contains("--verbose")) dev.Log += Console.WriteLine;
            Console.WriteLine($"online={dev.ReadOnline()} versão={dev.ReadVersion()} bateria={dev.ReadBattery()} modelo={dev.ReadModelId()}");
            // luzes do receptor (só leitura)
            foreach (var c in new[] { Protocol.Cmd.GetDongleRgbBar, Protocol.Cmd.GetDongleIndicator, Protocol.Cmd.GetDongleRgb4K })
                Console.WriteLine($"{c,-20} {(dev.Transact(c) is { } r ? Protocol.Packet.Hex(r) : "sem resposta")}");
            if (args.Contains("--dongle-only")) return 0;
            if (args.Contains("--dongle-test"))
            {
                // teste reversível do LED de status do receptor: troca o modo, confere e restaura
                var orig = dev.ReadDongleLight();
                if (orig is null) { Console.WriteLine("receptor sem registro de luz (0x15)"); return 1; }
                byte other = orig[0] == 1 ? (byte)2 : (byte)1;
                try { dev.WriteDongleStatus(other); Console.WriteLine($"gravado modo {other}: {Protocol.Packet.Hex(dev.ReadDongleLight()!)}"); }
                finally { dev.WriteDongleStatus(orig[0]); Console.WriteLine($"restaurado modo {orig[0]}: {Protocol.Packet.Hex(dev.ReadDongleLight()!)}"); }
                return 0;
            }
            try
            {
                var img = dev.ReadFlash();
                var cfg = Protocol.MouseConfig.FromImage(img);
                Console.WriteLine($"memória lida; avisos: {cfg.Warnings.Count}");
                foreach (var w in cfg.Warnings) Console.WriteLine("  " + w);
            }
            catch (Exception ex) { Console.WriteLine("ERRO: " + ex.Message); return 1; }
            return 0;
        }
        if (args.Contains("--kb-probe"))
        {
            // só leitura: identifica o teclado e despeja as tabelas (para validar o protocolo)
            AttachConsole(-1);
            using var kb = KeyboardDevice.TryOpenReal(out var msg);
            Console.WriteLine(msg);
            if (kb is null) return 1;
            if (args.Contains("--verbose")) kb.Log += Console.WriteLine;
            if (args.Contains("--dry-run"))
            {
                // lê tudo e mostra os pacotes de uma edição de exemplo (W em 1,2 mm com Rapid Trigger),
                // sem enviar nenhum deles
                var read = kb.ReadAll();
                var m = kb.Model!;
                Console.WriteLine($"leitura: {read.Warnings.Count} aviso(s) {string.Join("; ", read.Warnings)}");
                var dto = UI.KbConfigDto.From(read, m, kb.Limits);
                int w = Array.FindIndex(m.Keys, k => k.Code == "KeyW");
                dto.Travel[w] = 1.2; dto.Rt[w] = true; dto.RtPress[w] = 0.1;
                var next = dto.ApplyTo(read, m, kb.Limits);
                foreach (var op in kb.Plan(next))
                {
                    Console.WriteLine(op.Label);
                    foreach (var p in op.Packets) Console.WriteLine("   " + Protocol.Keyboard.KbPacket.Hex(p));
                }
                return 0;
            }
            if (args.SkipWhile(a => a != "--write-test").Skip(1).FirstOrDefault() is { } step)
            {
                // teste reversível: grava uma alteração, confere (Apply relê) e restaura o original
                var orig = kb.ReadAll().Clone();
                var m = kb.Model!;
                var backups = Path.Combine(AppContext.BaseDirectory, "backups");
                var t = orig.Clone();
                int w = m.SlotOf(m.Keys.First(k => k.Code == "KeyW"))!.Value;
                switch (step)
                {
                    case "rate": t.ReportRateCode = 3; break;                                    // 1000 Hz
                    case "led": t.Led = t.Led with { Brightness = 2 }; break;
                    case "key": t.Travel[w] = kb.Limits.ToRaw(1.5); t.Mode[w] = 0x80; break;      // W 1,5 mm + RT
                    default: Console.WriteLine("passo desconhecido"); return 2;
                }
                try
                {
                    Console.WriteLine($"[{step}] gravando: {kb.Apply(t, backups)} pacote(s), conferido");
                    Console.WriteLine($"[{step}] lido: taxa={kb.Config!.ReportRateCode} brilho={kb.Config.Led.Brightness} W={kb.Limits.ToMm(kb.Config.Travel[w])}mm modo=0x{kb.Config.Mode[w]:X2}");
                }
                finally
                {
                    Console.WriteLine($"[{step}] restaurando: {kb.Apply(orig, backups)} pacote(s), conferido");
                    Console.WriteLine($"[{step}] lido: taxa={kb.Config!.ReportRateCode} brilho={kb.Config.Led.Brightness} W={kb.Limits.ToMm(kb.Config.Travel[w])}mm modo=0x{kb.Config.Mode[w]:X2}");
                }
                return 0;
            }
            kb.Probe(Console.Out);
            return 0;
        }
        // Instância única: o WebView2 de duas janelas disputaria a mesma pasta de dados (e o mesmo mouse).
        // A segunda execução só traz a janela que já existe para a frente e sai.
        using var single = new Mutex(true, @"Local\SharkConfig.InstanciaUnica", out bool first);
        bool tray = args.Contains("--tray");   // Iniciar com o Windows: começa só na bandeja
        if (!first)
        {
            if (tray) return 0;             // já está rodando: a partida automática não abre a janela
            AllowSetForegroundWindow(-1);   // ASFW_ANY: deixa a outra instância vir para a frente
            PostMessage((IntPtr)0xFFFF, UI.HostForm.ShowWindowMessage, 0, 0);   // HWND_BROADCAST
            return 0;
        }
        ApplicationConfiguration.Initialize();
        // --page overview|performance|dpi|buttons|lighting|advanced
        var page = args.SkipWhile(a => a != "--page").Skip(1).FirstOrDefault();
        Application.Run(new UI.HostForm(page, tray));
        return 0;
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, nint w, nint l);
}
