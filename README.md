<p align="center"><img src="assets/icon.svg" width="112" alt="Ícone do SharkConfig"></p>

# SharkConfig

Configurador **não oficial** e leve para o mouse **Attack Shark X8 Ultra/Ultimate** e o teclado
**Attack Shark X68 HE** no Windows. Interface inspirada no macOS e no Logitech G HUB, protocolos
reconstruídos por engenharia reversa (interoperabilidade) dos softwares oficiais.

> Projeto independente, sem vínculo com a Attack Shark. Use por sua conta e risco.

## O que dá para ajustar

| Área | Ajustes |
|---|---|
| Desempenho | taxa de atualização 125–8000 Hz, modo do sensor (LP/HP/Corded), distância de levantamento, Motion Sync, correção de linha reta, controle de ondulação, desempenho máximo, tempo para dormir, debounce |
| Sensibilidade | 1 a 8 estágios de 50 a 42.000 DPI, cor de cada estágio, estágio ativo |
| Botões | cliques, voltar/avançar, DPI (ciclo, +, −, travar), rolagem |
| Iluminação | efeito, brilho e velocidade do indicador de DPI; LED de status do receptor 8K (taxa de atualização, bateria ou só aviso de bateria) |
| Avançado | leitura da memória do mouse, backup e restauração |

### Teclado X68 HE (pelo cabo)

| Área | Ajustes |
|---|---|
| Desempenho | taxa de atualização 125–8000 Hz |
| Teclas | ponto de acionamento, ponto de soltura, Rapid Trigger (liga/desliga e sensibilidade ao pressionar e soltar) e zona morta, por tecla ou em grupo (todas, WASD, letras) |
| Iluminação | 19 efeitos, direção, velocidade, brilho, cor ou arco-íris |
| Avançado | leitura das tabelas do teclado, backup e restauração |

Reconhece as três revisões do X68 HE pelo id que o teclado informa (2270, 2472 e 2902). Teclas com
DKS, Mod-Tap, Toggle ou SnapTap configurados no app oficial aparecem apagadas e não são alteradas.
A tela inicial mostra o mouse e o teclado, com o que está conectado em cada um, e você escolhe qual
configurar; o botão Início na barra lateral volta para ela.

As configurações ficam gravadas **na memória do próprio dispositivo**: não é preciso deixar o app aberto.

### Bandeja e bateria

Fechar a janela deixa o SharkConfig na bandeja do Windows. O ícone mostra a porcentagem da bateria do
X8, em vermelho com 20% ou menos, e a dica diz se o mouse está dormindo. A bateria é lida uma vez por minuto e quando algum
dispositivo é conectado. Pelo receptor, o app confere antes se o mouse está acordado, para não ficar
mandando pedidos a ele. Clique no ícone para abrir; Sair, no menu do ícone, encerra o app.

**Iniciar com o Windows** (na engrenagem ou no menu do ícone) é opcional e vem desligado. Ligado, grava
`SharkConfig.exe --tray` na chave `Run` do usuário: o app abre só na bandeja e a interface (WebView2) só é
criada quando você abre a janela.

## Requisitos

- Windows 10/11 com o [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (já vem no Windows 11)
- [.NET SDK 10](https://dotnet.microsoft.com/download) para compilar
- Mouse X8 Ultra pelo receptor 8K ou pelo cabo (VID `0x3554`), ou teclado X68 HE pelo cabo
  (VID `0x3151`, PID `0x502D`). Feche o app oficial antes de usar.

## Compilar e rodar

```powershell
cd src\SharkConfig
dotnet build -c Release
.\bin\Release\net10.0-windows\SharkConfig.exe
```

| Opção | Efeito |
|---|---|
| `--page dpi` | abre numa página: `overview`, `performance`, `dpi`, `buttons`, `lighting`, `advanced` |
| `--list` | lista as coleções HID do PC |
| `--mouse-probe` | só leitura: abre o X8, lê as luzes do receptor e a memória e lista os avisos (`--verbose` mostra os pacotes) |
| `--mouse-probe --dongle-test` | troca o modo do LED do receptor, confere e restaura o original |
| `--tray` | abre só na bandeja (é o que Iniciar com o Windows usa) |
| `--render-tray-icon 85 icone.png` | salva o ícone da bandeja de uma porcentagem, para conferir o desenho |
| `--kb-probe` | só leitura: identifica o teclado e despeja as tabelas (`--verbose` mostra os pacotes) |
| `--kb-probe --dry-run` | lê o teclado e mostra os pacotes de uma edição de exemplo, sem enviar nenhum |
| `--kb-probe --write-test rate\|led\|key` | grava uma alteração de teste, confere e restaura o original (com backup) |

### Foto do teclado

`src/SharkConfig/wwwroot/img/keyboard-x68.png` é a foto de produto do X68 HE (© Attack Shark), recortada
para 498×182 sem a alça, e serve de corpo do teclado. As teclas do app são desenhadas por cima, nas
posições medidas na foto: a grade de 65% fica em `KeyboardModels.cs`. Se a foto faltar, o app desenha
um gabinete simples no lugar.

### Fotos do mouse

As fotos de produto em `src/SharkConfig/wwwroot/img/mouse*.png` vêm do app oficial (© Attack Shark),
recortadas para 202×356. O app escolhe a foto pelo modelo lido do mouse ou manualmente em
Avançado → Aparência; se alguma faltar, mostra uma silhueta neutra. Para atualizá-las a partir de
uma instalação do app oficial:

```powershell
.\tools\extract-photos.ps1
```

## Segurança

- Só leituras, a gravação de configuração e a gravação do LED do receptor (`0x14`, só o modo, com eco e
  releitura) podem ser enviadas; atualização de firmware, troca de VID/PID, pareamento, ID do receptor e
  reset de fábrica são bloqueados no código (`Packet.AllowedCommands`).
- Grava do mesmo jeito do app oficial: um pacote por campo alterado, só valores das mesmas listas e
  faixas, com intervalo entre pacotes.
- Antes de gravar: recusa se a leitura do mouse veio com algum campo inválido, confere cada pacote
  contra as áreas permitidas e salva um backup. Durante: exige o eco exato de cada pacote. Depois:
  relê tudo o que gravou e compara byte a byte.
- DPI só é gravado quando o modelo é identificado como PAW3950, e nenhum botão pode ficar sem o
  clique esquerdo.
- O protocolo veio de análise estática do software oficial; alguns detalhes ainda precisam de
  confirmação em hardware — veja [docs/PROTOCOL.md](docs/PROTOCOL.md).
- Teclado: só leituras, taxa de atualização (`0x03`), iluminação (`0x07`) e seis tabelas dos switches
  magnéticos (`0x65`) podem ser enviados; reset de fábrica, calibração, gravação de SKU, apagar a flash
  e atualização de firmware são bloqueados no código (`KbPacket.AllowedCommands`). Mesmo esquema de
  backup, recusa de leitura inválida e conferência após gravar — veja
  [docs/PROTOCOL-KEYBOARD.md](docs/PROTOCOL-KEYBOARD.md).

## Estrutura

```
src/SharkConfig/
  Protocol/        pacotes, checksum, mapa da memória, modelo de configuração
  Protocol/Keyboard/  pacotes, modelos (matrizes e layout) e configuração do X68 HE
  Transport/       HID do Windows (SetupAPI/hid.dll), relatórios de saída e feature reports
  MouseDevice.cs   transações, leitura da memória, aplicação com backup
  KeyboardDevice.cs  o mesmo para o teclado
  UI/              janela nativa + WebView2 e ponte JS ↔ C#
  wwwroot/         interface (HTML/CSS/JS)
  app.ico          ícone do app (16 a 256 px, gerado de assets/icon.svg)
assets/icon.svg    ícone em vetor
tools/             sonda de coleções HID e atualização das fotos
docs/PROTOCOL.md   protocolo do mouse documentado
docs/PROTOCOL-KEYBOARD.md  protocolo do teclado documentado
```

Os dispositivos são detectados sozinhos ao serem conectados (o app reage ao aviso do Windows, sem ficar consultando).
A engrenagem no canto inferior esquerdo abre a aparência: tema escuro ou claro, transparência da
janela (fundo acrílico do Windows 11), a opção de reduzir movimento e Iniciar com o Windows.

Consumo medido (Windows 11): o processo `SharkConfig.exe` usa cerca de 15 MB de RAM privada; somando
os processos do WebView2 que desenham a interface (com a GPU ligada, para as animações e o vidro
ficarem lisos), o total fica entre ~180 MB (sem mouse) e ~225 MB (conectado). CPU parado: ~0%; ao
minimizar ou fechar para a bandeja, a interface é suspensa. Iniciado só na bandeja (`--tray`), sem a
interface, o processo ocupa ~45 MB. O app abre uma única janela: executá-lo de novo só traz a janela
para a frente (inclusive da bandeja).

## Licença

Código sob a [licença MIT](LICENSE). As fotos de produto são © Attack Shark e não estão cobertas
pela licença.
