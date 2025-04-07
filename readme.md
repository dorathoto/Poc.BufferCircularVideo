
# Descrição - Portugues (English version in the footer)
Um projeto de buffer circular para armazenamento temporário de vídeos RTSP em C#, ideal para sistemas de vigilância ou gravação acionada por eventos.

- Eficiência de memória: Armazena apenas os últimos X segundos em memória (buffer circular)
- O Buffer Circular é justamente para que na memória fique apenas os últimos 10 segundos (x-segudos), e não o vídeo inteiro, assim não consome tanta memória.
- Interessante é utilizar os NAL Unity ao invés do buffer bruto de bytes, assim não é preciso re-reencoder novamente (muito mais otimizado)
- Possível fazer com o [Microsoft.VisualStudio.Utilities.CircularBuffer<T> by Microsoft](https://learn.microsoft.com/pt-br/dotnet/api/microsoft.visualstudio.utilities.circularbuffer-1?view=visualstudiosdk-2022) Ou fazer o buffer na mão, como nesse exemplo. Cada um tem vantagens e desvantagens.


## BUGS conhecidos
- O projeto funciona perfeitamente com 15 segundos, porém com muito tempo ele quebra !?
- O Vídeo pode estar em kbps diferente, assim o buffer pode ficar menor ou muito grande, fazendo com o que o tempo não seja o correto.

> OBS: Não teria problema se fosse (10s + trigger + 10s e ao invés de dar 20 segundos desse, 16s ou 22s o que não é aceitavel é ter 1 minuto  ou mais de diferença))



## Para começar

### Pré-requisitos
FFmpeg (Windows): [ffmpeg.exe download](https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip)
*→ Coloque ffmpeg.exe na raiz do projeto e configure como Copy to Output Directory: Copy always*

OBS: Caso queira converter o projeto para Linux, remova o pacote nuget  VideoLAN.LibVLC.Windows



## Como simular um servidor RTSP Localmente
Salve um arquivo MP4 qualquer: ex: http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4

Puxe o MediaMTX e execute ele (ele criará um servidor RTSP)
[MediaMTX.exe download](https://github.com/bluenviron/mediamtx/releases/download/v1.11.3/mediamtx_v1.11.3_windows_amd64.zip)

Depois de executa-lo, execute o comando abaixo para o ffmpeg gerar o conteúdo, pode testar no VLC abrindo em *Midia > Abrindo transmissão de REDE > REDE -> "rtsp://localhost:8554/live/stream"*

     .\ffmpeg.exe -re -stream_loop -1 -i BigBuckBunny.mp4 -c copy -f rtsp -rtsp_transport tcp rtsp://localhost:8554/live/stream

 OBS: Não execute no mesmo ffmpeg do projeto, senão o projeto C# não conseguirá abrir ele.

 Execute a aplicação C#, exemplo da tela do programa
 
![Exemplo da tela](https://github.com/dorathoto/Poc.BufferCircularVideo/blob/stage/Captura2025-04-07%20094946.jpg?raw=true)
 -----------

 # English

 📹 Circular Video Buffer for RTSP Streams (C#)
A circular buffer implementation for RTSP video streams in C#.
Ideal for scenarios where you need to save X seconds of pre-trigger buffer + Y seconds of post-trigger video (e.g., 10s pre-trigger + 5s post-trigger = 15s total) without storing the entire video in memory.

🎯 Key Features
🚀 Memory-efficient: Stores only the last X seconds in memory (circular buffer).

⚡ NAL Unit Optimization: Works with NAL units (H.264 packets) to avoid re-encoding.

📦 Plug & Play: Works with FFmpeg and MediaMTX for local RTSP streaming.

⚠️ Known Issues
Timing Inconsistencies:
The buffer duration may vary slightly due to variable bitrate (VBR) streams.
Example: Expected 20s (10s + trigger + 10s) might result in 16s–22s.
(Critical bug: Avoid extreme deviations like 1min+ differences.)

🛠️ Setup Guide
Prerequisites
FFmpeg (Windows):
Download from gyan.dev (essentials build).
→ Place ffmpeg.exe in the project root and set Copy to Output Directory: Copy always.

MediaMTX (RTSP Server):
Download from GitHub.
→ Run mediamtx.exe to start a local RTSP server.

🚀 How to Run
1. Simulate an RTSP Stream
Download a sample video (e.g., BigBuckBunny.mp4).

Stream it to MediaMTX:

    .\ffmpeg.exe -re -stream_loop -1 -i BigBuckBunny.mp4 -c copy -f rtsp -rtsp_transport tcp rtsp://localhost:8554/live/stream
    Test in VLC:

Open rtsp://localhost:8554/live/stream (Media → Open Network Stream).

2. Run the Project
The C# app will connect to the RTSP stream and manage the circular buffer.

Trigger logic (e.g., manual/API) saves X pre-trigger + Y post-trigger seconds.
