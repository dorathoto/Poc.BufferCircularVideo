using LibVLCSharp.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Poc.BufferCircularVideo;

public class VideoRTSPRecorderCircular : IDisposable
{
    private const int BUFFER_SECONDS = 3;
    private const int EXTRA_SECONDS = 3;
    private const int WIDTH = 1280;
    private const int HEIGHT = 720;
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private readonly ConcurrentQueue<H264Packet> _circularBuffer = new();
    private volatile bool _recording;
    private List<H264Packet> _pendingRecording = new();
    private Thread _recordingThread;

    public VideoRTSPRecorderCircular(string rtspUrl)
    {
        Core.Initialize();
        _libVLC = new LibVLC("--network-caching=500",
                            //   "--clock-jitter=0",
                            "--rtsp-tcp",
                            "--avcodec-hw=none",
                            // "--avcodec-hw=dxva2", // Se tiver GPU compatível, estava dando erro: hardware acceleration picture allocation failed
                            "--skip-frames");

        var media = new Media(_libVLC, rtspUrl, FromType.FromLocation);
        //media.AddOption(":sout=#file{dst=recording.mp4}");
        //media.AddOption(":sout-keep");
        media.AddOption(":codec=h264");
        media.AddOption(":rtsp-tcp");
        media.AddOption(":network-caching=300");
        media.AddOption(":no-audio");
        _mediaPlayer = new MediaPlayer(media)
        {
            EnableHardwareDecoding = false // Ativa aceleração por hardware
        };

        ConfigureCallbacks();
        _mediaPlayer.Play();
    }

    private void ConfigureCallbacks()
    {
        _mediaPlayer.SetVideoFormat("RV24", WIDTH, HEIGHT, WIDTH * 3);// Configuração do formato de vídeo

        // Configuração dos callbacks usando a nova API
        _mediaPlayer.SetVideoCallbacks(
            LockCallback,
            UnlockCallback,
            DisplayCallback
        );


    }

    private IntPtr LockCallback(IntPtr opaque, IntPtr planes)
    {
        // Aloca buffer para o frame
        Marshal.WriteIntPtr(planes, Marshal.AllocHGlobal(WIDTH * HEIGHT * 3));
        return IntPtr.Zero;
    }

    private void UnlockCallback(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
        // Processa o frame quando está desbloqueado
        var buffer = new byte[WIDTH * HEIGHT * 3];
        Marshal.Copy(Marshal.ReadIntPtr(planes), buffer, 0, buffer.Length);

        var packet = new H264Packet
        {
            Data = buffer,
            Timestamp = DateTime.UtcNow
        };

        ManageCircularBuffer(packet);

        // Libera a memória
        Marshal.FreeHGlobal(Marshal.ReadIntPtr(planes));
    }

    private void DisplayCallback(IntPtr opaque, IntPtr picture)
    {
        // Não necessário para esta implementação?
    }

    private void ManageCircularBuffer(H264Packet packet)
    {
        lock (_circularBuffer)
        {
            // Mantém apenas os últimos 10 segundos
            while (_circularBuffer.TryPeek(out var first) &&
                  (DateTime.UtcNow - first.Timestamp).TotalSeconds > BUFFER_SECONDS)
            {
                _circularBuffer.TryDequeue(out _);
            }
            _circularBuffer.Enqueue(packet);
        }
    }

    public void TriggerRecording()
    {
        if (_recording) return;

        _recording = true;
        _recordingThread = new Thread(RecordExtended);
        _recordingThread.Start();
    }

    private void RecordExtended()
    {
        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddSeconds(EXTRA_SECONDS);
        var lastIndex = -1;

        while (DateTime.UtcNow < endTime && _recording)
        {
            lock (_circularBuffer)
            {
                var newPackets = _circularBuffer
                    .Skip(lastIndex + 1)
                    .ToList();

                if (newPackets.Any())
                {
                    _pendingRecording.AddRange(newPackets);
                    lastIndex = _circularBuffer.Count - 1;
                }
            }
            Thread.Sleep(50);
        }

        SaveRecordingAsync(_pendingRecording).GetAwaiter().GetResult();
        _recording = false;
    }

    private async Task SaveRecordingAsync(List<H264Packet> recordingData)
    {

        var errors = new StringBuilder();
        try
        {
            var outputFile = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.ts";
            var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg", "ffmpeg.exe");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -hwaccel none -f h264 -i pipe: " +
                            $"-bsf:v h264_mp4toannexb " +
                            $"-c:v copy -f mpegts {outputFile}",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            File.WriteAllBytes("debug_input.h264", recordingData.SelectMany(p => p.Data).ToArray());

            process.Start();

            process.ErrorDataReceived += (sender, e) => errors.AppendLine(e.Data);
            process.BeginErrorReadLine();

            // Escreve os headers SPS/PPS primeiro
            //var headers = recordingData
            //              .Where(p => p.Data.Length > 4 && (p.Data[4] & 0x1F) == 7 || (p.Data[0] & 0x1F) == 8)
            //              .Take(2)
            //              .ToList();
            var headers = recordingData
                            .Where(p =>
                            {
                                if (p.Data.Length < 1) return false;
                                int nalType = p.Data[0] & 0x1F; // AVCC: NAL type está na posição 0
                                return nalType == 7 || nalType == 8;
                            })
                            .Take(2)
                            .ToList();

            foreach (var header in headers)
            {
                //await process.StandardInput.BaseStream.WriteAsync(header.Data.AsMemory(0, header.Data.Length)); Quem sabe essa abordagem?
                await process.StandardInput.BaseStream.WriteAsync(header.Data, 0, header.Data.Length);
                await process.StandardInput.BaseStream.FlushAsync();
                //process.StandardInput.BaseStream.Write(header.Data, 0, header.Data.Length);
            }


            // Escreve o restante dos dados ordenados por timestamp
            var headerTimestamps = headers.Select(h => h.Timestamp).ToHashSet();
            var packets = recordingData
                .Where(p => !headerTimestamps.Contains(p.Timestamp))
                .OrderBy(p => p.Timestamp);
            foreach (var packet in packets)
            {
                if (packet.Data?.Length > 0) //verifica se não está vazio ou corrompido
                {
                    try
                    {
                        process.StandardInput.BaseStream.Write(packet.Data, 0, packet.Data.Length);
                    }
                    catch (Exception ex)
                    {

                        errors.AppendLine(ex.Message);
                        break;
                    }
                }
            }

            process.StandardInput.Close();
            process.WaitForExit(10000);

            // LOG DOS PACOTES


            File.WriteAllLines("packet_log.log", recordingData.Select(p =>
                $"{p.Timestamp:HH:mm:ss.fff} - {(p.IsKeyFrame ? "KEY" : "PFR")} - {p.Data.Length} bytes"));
            Console.WriteLine($"Gravação finalizada: {outputFile} (Tamanho: {new FileInfo(outputFile).Length} bytes)");
        }
        catch (Exception ex)
        {
            File.WriteAllText("ffmpeg_crash.log", $"Erro: {ex}\nLogs: {errors}");
        }
        finally
        {
            File.WriteAllText("ffmpeg_errors.log", errors.ToString());
        }
    }

    private bool IsKeyFrame(byte[] data)
    {
        // Verifica se é um NAL unit type 5 (IDR) em formato Annex B
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (data[i] == 0x00 && data[i + 1] == 0x00 && data[i + 2] == 0x00 && data[i + 3] == 0x01)
            {
                int nalType = data[i + 4] & 0x1F;
                return nalType == 5;
            }
        }
        return false;
    }

    public void Dispose()
    {
        _recording = false;
        if (_recordingThread.IsAlive)
        {
            _recordingThread?.Join(3000); // Espera até 3 segundos
        }
        _mediaPlayer?.Stop();
        _libVLC?.Dispose();
    }
}