# Protocolo HID do Attack Shark X8 Ultra

Documentação reconstruída por análise estática do software oficial "ATTACK SHARK MOUSE HUB"
(v1.00), para fins de interoperabilidade. Nenhum código do fabricante faz parte deste projeto.

Legenda de confiança: **[O]** observado no software oficial · **[I]** inferido · **[?]** a confirmar em hardware.

## Dispositivo

| Item | Valor |
|---|---|
| Sensor | PixArt PAW3950 |
| MCU do mouse | nRF52840 |
| Receptor 8K | CX52660 |
| VID | `0x3554` |
| PIDs pelo cabo | `FB43`, `F516`, `F515` |
| PIDs pelo receptor 8K | `FB44`, `FB35`, `F517` |
| Interface de configuração | `mi_01`, coleção `col05` no app oficial |
| DPI | 50–30.000 em passos de 50; 30.100–42.000 em passos de 100 |
| Estágios | até 8 (padrão 6: 1200, 2400, 3200, 5600, 8000, 42000) |

## Transporte [O]

Relatórios de **17 bytes** por `WriteFile`/`ReadFile` no handle HID, Report ID `0x08`:

```
[0]=0x08  [1]=cmd  [2]=0x00  [3]=endereço alto  [4]=endereço baixo  [5]=len
[6..15]=dados (até 10 bytes)  [16]=checksum
checksum = (0x55 − soma dos bytes [0..15]) & 0xFF
```

- Modo mouse: `len` sem o bit `0x80` (o bit só é usado em modo teclado).
- Resposta válida: checksum correto **e** `resp[1] == cmd`. O app oficial tenta 3 vezes, 100 ms cada.
- Escrita (`0x07`): o mouse responde com o **eco idêntico** dos 17 bytes; o app oficial compara o
  pacote inteiro e reenvia se não bater. Não existe comando de "salvar": as escritas já são definitivas.
- O app oficial espera pelo menos 8 ms entre pacotes.
- Leitura e escrita da memória em blocos de até 10 bytes.

## Comandos (`[1]`) [O]

| cmd | Função | Usado pelo SharkConfig |
|---|---|---|
| `0x03` | mouse conectado ao receptor | sim (leitura) |
| `0x04` | bateria | sim (leitura) |
| `0x07` | **gravar memória de configuração** | sim |
| `0x08` | **ler memória de configuração** | sim (leitura) |
| `0x12` | versão do firmware | sim (leitura) |
| `0xF1` | ler CID/MID (identifica o modelo) | sim (leitura) |
| `0x01`, `0x06`, `0x0E`, `0x17`, `0x19`, `0x20`, `0x2B`, `0x2D`, `0xB3` | outras leituras (DPI atual, pareamento, perfil, alcance, LEDs do receptor, serial, RSSI, versão do escravo) | não |
| `0x02`, `0x0F`, `0x14`, `0x16`, `0x18`, `0x2C` | status do driver, perfil, LEDs do receptor, modo longo alcance | não |
| `0x05`, `0x09`, `0x1A`, `0x1F` | pareamento, reset de fábrica, ID do receptor, serial | **bloqueado** |
| `0x0B`, `0x0C`, `0x0D`, `0x11`, `0xF0` | troca de VID/PID, descritor USB, modo de atualização, modo MTK, gravar CID/MID | **bloqueado — risco de inutilizar o mouse** |

Respostas:
- `0x04`: `resp[6]` = %, `resp[7]` = estado [?], `resp[8..9]` = tensão em mV (big-endian). [O]
- `0x12`: `resp[6] << 8 | resp[7]`. [O]
- `0x08`: dados a partir de `resp[6]`, no mesmo formato da memória. [O]
- `0xF1`: pedido com `len = 0x83`; `resp[6]` = CID, `resp[7]` = MID. [I] O MID é comparado em
  **decimal** com a lista do app oficial (01–05 e 10–12 = PAW3950; 13–22 = PAW3955, outra codificação de DPI).

## Memória de configuração [O]

Valores de 1 byte são gravados como **par `[v, 0x55 − v]`**. Registros de N bytes terminam com
`0x55 − soma(N bytes)`. O app oficial grava só os campos alterados, um pacote por campo.

| Endereço | Campo | Padrão | Valores |
|---|---|---|---|
| `0x00` | taxa de atualização | 1 | `0x08`=125, `0x04`=250, `0x02`=500, `0x01`=1000, `0x10`=2000, `0x20`=4000, `0x40`=8000 Hz |
| `0x02` | nº de estágios de DPI | 6 | 1–6 neste modelo (a memória tem 8 registros) |
| `0x04` | estágio ativo | 2 | 0 a nº−1 |
| `0x06`, `0x08` | ? | 0 | [?] |
| `0x0A` | distância de levantamento | 0 | 0=0,7 mm, 1=1 mm, 2=2 mm |
| `0x0C + 4i` | DPI legado do estágio i (3 bytes + chk) | | |
| `0x2C + 4i` | cor do estágio i (R, G, B + chk) | | |
| `0x4C` | indicador de DPI: efeito | 2 | 0=desligado, 1=fixo, 2=respiração |
| `0x4E` | indicador de DPI: brilho | 0x80 | 10 níveis: 16, 30, 60, 90, 128, 150, 180, 210, 230, 255 |
| `0x50` | indicador de DPI: velocidade | 3 | 1–5 |
| `0x52` | indicador de DPI: ligado | 1 | 0 se o efeito é "desligado", senão 1 (gravado junto com o efeito) |
| `0x60 + 4i` | função do botão i (tipo, p1, p2 + chk) | | ver Botões |
| `0xA9` | debounce (ms) | 8 | 1–20 |
| `0xAB` | Motion Sync | 1 | 0/1 |
| `0xAD` | dormir após (unidades de 10 s) | 6 | 1, 3, 6, 12, 18, 30, 60, 90, 180, 210 |
| `0xAF` | correção de linha reta | 0 | 0/1 |
| `0xB1` | controle de ondulação | 0 | 0/1 |
| `0xB3` | ? | 0 | [?] |
| `0xB5` | desempenho máximo | 0 | 0/1 |
| `0xB7` | sair do desempenho máximo após (10 s) | 6 | como `0xAD` |
| `0xB9` | modo do sensor | 0 | 0=LP, 1=HP, 2=Corded |
| `0xBD`–`0xE7` | ? | | [?] |
| `0x100 + 0x20i` | atalho/combinação do botão i | | ver Botões |
| `0x300 + 0x180i` | macro do botão i | | não implementado |
| `0x1B00 + 6i` | DPI do estágio i: X baixo, X alto, Y baixo, Y alto, flag + chk | | ver DPI |

### DPI do PAW3950

```
div  = dpi > 30000 ? 2 : 1
flag = div == 2 ? 0x11 : 0x00
raw  = dpi / div − 1                       (u16, X = Y)
h    = raw >> 8
flag |= ((h << 4 | h) << 2) & 0xFF         (o app oficial faz isso para 3950/3395)
grava em 0x1B00 + 6i: raw_lo raw_hi raw_lo raw_hi flag chk
```

Leitura usada pelo SharkConfig: `dpi = (raw + 1) × (flag & 1 ? 2 : 1)` [I] — os bits altos do flag
recebem o valor acima e não são confiáveis para decodificar.

### DPI no formato antigo (`0x0C + 4i`) [H]

Um X8 com firmware 3.00 pelo receptor (MID 0) deixa a tabela `0x1B00` **toda apagada (`FF`)** e guarda o
DPI só no registro legado: `[X baixo, Y baixo, flag, chk]`, em unidades de **50 DPI**, com os bits altos
em `flag` (bits 2–3) e o divisor 2 no bit 0:

```
dpi = ((lo | ((flag >> 2) & 3) << 8) + 1) × 50 × (flag & 1 ? 2 : 1)
lido: 0F 0F 00 → 800 · 2F → 2400 · 3F → 3200 · 6F → 5600 · 9F → 8000 · A3 A3 55 → 42.000
```

Quando a tabela nova está apagada, o SharkConfig lê o DPI daqui e o deixa **só para leitura**. Antes
disso, a leitura acusava "DPI inválido" em todos os estágios e o app recusava qualquer gravação. A
gravação também regravava os 8 registros de `0x1B00` sempre que algum deles fosse inválido; agora o DPI só é
gravado quando o valor muda.

## Luzes do receptor [O][H]

Analisado no "ATTACK SHARK MOUSE HUB" de 14/08/2026 (`download.attackshark.pro/ATTACKSHARK/X8Ultra/`),
`hidusb.dll`. Os comandos vão para o próprio receptor e respondem com o mouse dormindo.

| Gravar | Ler | Função na DLL | Dados |
|---|---|---|---|
| `0x14` | `0x15` | `Set4KDongleRGB` / `Get4KDongleRGBValue` | 10 bytes em `[6..15]`, `len = 0x0A` |
| `0x18` | `0x19` | `SetDongleRGBBarMode` / `GetDongleRGBBarMode` | `[6]` = modo (0 se desligado), `[7..11]` = 5 parâmetros |
| `0x2C` | `0x2D` | `SetDongleIndicatorLed` / `GetDongleIndicatorLed` | 3 bytes em `[6..8]` |

- A gravação `0x14` é confirmada pelo eco: `resp[2] = 0` e `resp[6..15]` iguais ao enviado.
- O app oficial só troca o **byte 0** do registro `0x14` (o modo do LED de status) e regrava o resto como leu:
  `1` = taxa de atualização (vermelho 125 Hz, azul 250, amarelo 500, laranja 1000, roxo 2000, verde 4000,
  ciano 8000), `2` = nível de bateria (verde ≥ 75%, laranja < 75%, vermelho ≤ 15%), `3` = só aviso de
  bateria (pisca em vermelho com a bateria fraca). Os outros 9 bytes não são interpretados.
- No receptor 8K do X8 Ultra (PID `FB44`) [H]: `0x15` → `02 00 FF 00 FF FF 00 FF 00 00` (bateria);
  gravado 1 e de volta 2, com eco e releitura idênticos. `0x19` e `0x2D` respondem `resp[2] = 01` sem
  dados (com o mouse dormindo), então a luz decorativa (`0x18`) e o outro LED (`0x2C`) não estão no SharkConfig.

### Mouse dormindo

Pelo receptor, o comando `0x03` responde `resp[6] = 01` com o mouse ligado e `00` com ele dormindo ou
desligado [H]. Nesse estado o receptor ainda responde `0x03` e `0xF1`, mas `0x04`, `0x08` e `0x12` ficam
sem resposta. O SharkConfig confere isso antes de ler a memória e pede para mexer no mouse.

### Botões

Registro `[tipo, p1, p2, chk]`:

| Tipo | Função | p1 |
|---|---|---|
| `0x00` | desativado | |
| `0x01` | botão do mouse | `01` esquerdo, `02` direito, `04` meio, `08` voltar, `10` avançar |
| `0x02` | DPI | `01` ciclo, `02` aumentar, `03` diminuir |
| `0x03` | rolagem horizontal | `01` esquerda, `02` direita |
| `0x04` | disparo repetido | [?] |
| `0x05` | combinação/mídia (dados no bloco de atalho) | |
| `0x06` | macro | |
| `0x07` | alternar taxa de atualização | |
| `0x09` | trocar configuração | |
| `0x0B` | rolagem vertical | `01` cima, `02` baixo |
| `0x0C` | travar DPI | |
| `0x10`–`0x16` | indefinido / iluminação | |

Bloco de atalho (0x20 bytes): `[n (2–6)]`, depois n eventos `[estado | tipo, b1, b2]` e o checksum
de `1 + 3n` bytes. Estado `0x80` = pressiona, `0x40` = solta. Tipo 2 = mídia, com o código HID
consumer em `b1` (baixo) e `b2` (alto), ex. `0x00CD` reproduzir/pausar [I].

## A configuração fica no mouse

Tudo é gravado na memória do mouse e funciona sem software aberto:
- a escrita é o comando `0x07` em endereços de memória, no formato autoverificável acima;
- o app oficial lê a configuração de volta do mouse a cada conexão;
- o mouse tem valores-padrão para campos inválidos (memória apagada).

[?] A confirmar em hardware: mudar uma configuração, fechar o app, reconectar e verificar.

## Pendências de validação

- Decodificação do DPI do 3950 (bits altos do flag).
- Ordem física dos 5 botões do corpo e o que o 6º slot de botão (`0x60 + 4·5`) controla — o
  X8 Ultra não tem botão nem LED no centro do corpo (talvez um botão na base) [?].
- Campos marcados com ?.
