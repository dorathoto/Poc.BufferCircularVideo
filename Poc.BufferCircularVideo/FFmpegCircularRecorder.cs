using System.Diagnostics;
using System.Text;

namespace Poc.BufferCircularVideo;

public class FFmpegCircularRecorder : IDisposable
{
    private readonly string _rtspUrl;
    private Process _ffmpegProcess;
    private readonly CircularBuffer _buffer;
    private Thread _readerThread;
    private bool _isRunning;

    private const int BufferDurationMs = 10000;
    private readonly int _bufferSize;

    // Monitoramento de bitrate em tempo real
    private int _currentBitrateKbps;
    private int _frameCount;
    private long _totalBytesReceived;
    private Stopwatch _bitrateTimer = Stopwatch.StartNew();
    private Stopwatch _fpsTimer = Stopwatch.StartNew();

    private ManualResetEvent _ffmpegReadyEvent = new ManualResetEvent(false);
    private bool _isBufferInitialized;
    private const int MaxBitrateKbps = 4096; // 4 Mbps, se a câmera estiver muito alto vai dar estouro de buffer


    public FFmpegCircularRecorder(string rtspUrl)
    {
        _rtspUrl = rtspUrl;
        _bufferSize = CalculateBufferSize();// (Bitrate * BufferSeconds) * (100 + SafetyMarginPercent) / 100;
        _buffer = new CircularBuffer(_bufferSize);
        _bitrateTimer = Stopwatch.StartNew();
    }


    /// <summary>
    /// Calcula o tamanho do buffer em bytes necessário para armazenar o vídeo circular
    /// </summary>
    /// <returns></returns>
    private int CalculateBufferSize()
    {
        var bps = MaxBitrateKbps * 1024 / 8; // dividir por 8 bits para bytes
        var segundos = BufferDurationMs / 500; //dividindo por 500 e não 1000 porque quero o dobro de tempo de buffer (ex 10000ms/1000 = 10s * 2)
        return bps * segundos;
    }

    public void Start()
    {
        _isRunning = true;
        _ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-rtsp_transport tcp -i {_rtspUrl} -c copy -f mp4 -movflags frag_keyframe+empty_moov pipe:1",
                UseShellExecute = false,    // se true=Não usar shell do windows
                RedirectStandardOutput = true,  //saida do FFmpeg vai pro shell do windows
                RedirectStandardInput = true,   //com true=é possível enviar q (kill) para o FFmpeg.
                CreateNoWindow = true   //sempre true, evita que janelas do console apareçam
            }
        };
        _ffmpegProcess.Start();
        _readerThread = new Thread(() =>
        {
            // Aguarda o primeiro pacote de vídeo para considerar inicializado
            ReadFFmpegOutput();
            _ffmpegReadyEvent.Set();
        });
        _readerThread.Start();
    }

    private void ReadFFmpegOutput()
    {
        byte[] buffer = new byte[4096];
        while (_isRunning)
        {
            int bytesRead = _ffmpegProcess.StandardOutput.BaseStream.Read(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                _buffer.Write(buffer, 0, bytesRead);
                UpdateBitrate(bytesRead);
                //  UpdateFps();
            }
        }
    }
    private void UpdateBitrate(int bytesRead)
    {
        _totalBytesReceived += bytesRead;
        if (_bitrateTimer.Elapsed.TotalSeconds >= 1.0)
        {
            _currentBitrateKbps = (int)((_totalBytesReceived * 8) / _bitrateTimer.Elapsed.TotalMilliseconds);
            _totalBytesReceived = 0;
            _bitrateTimer.Restart();
            Console.WriteLine($"[DEBUG] Bitrate: {_currentBitrateKbps} Kbps ");
        }
    }

    public void SaveClip(string outputPath, int secondsBefore, int secondsAfter)
    {

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Caminho de saída inválido.");

        byte[] clipData = _buffer.ReadLastApproxSeconds(secondsBefore + secondsAfter);//Lê ~10s de buffer + secondAfter
                                                                                      //            byte[] clipData = _buffer.ReadLastByTime((secondsBefore + secondsAfter) * 1000, _currentBitrateKbps);

        if (clipData == null || clipData.Length == 0 || !IsValidVideoData(clipData))
            throw new InvalidDataException("Dados inválidos ou corrompidos.");

        if (clipData == null || clipData.Length == 0)
            throw new InvalidOperationException("Nenhum dado disponível no buffer.");


        Console.WriteLine($"[DEBUG] Bitrate atual: {_currentBitrateKbps} Kbps");
        Console.WriteLine($"[DEBUG] Tamanho do clip: {clipData.Length} bytes");
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, clipData);
            RunFFmpeg($"-y -f mp4 -i {tempFile} -c copy  -movflags +faststart {outputPath}");
            Console.WriteLine($"Arquivo salvo: {outputPath} (~{(clipData.Length / (1024 * 1024)):0.0}MB)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO] Falha ao salvar o arquivo: {ex.Message}");
            throw;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); }
                catch { /* Log  se falhar */ }
            }
        }
    }

    private bool IsValidVideoData(byte[] data)
    {
        // Verificação simples do cabeçalho MP4 
        return data.Length >= 8 && data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p';
    }


    private void RunFFmpeg(string args)
    {
        string logPath = Path.Combine(Path.GetTempPath(), "ffmpeg.log");
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true // Para capturar mensagens de erro
                }
            };

            // Captura a saída de erro
            StringBuilder errorOutput = new StringBuilder();
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutput.AppendLine(e.Data);
                    File.AppendAllText(logPath, e.Data + Environment.NewLine);
                }
            };
            process.Start();
            process.BeginErrorReadLine(); // Inicia leitura assíncrona do stderr

            bool exited = process.WaitForExit(15000); // Timeout de 10s
            if (!exited)
            {
                throw new Exception("FFmpeg não respondeu após 10 segundos.");
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"FFmpeg retornou código {process.ExitCode}: {errorOutput} no log {logPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO FFMPEG] {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        try
        {
            try
            {
                _ffmpegProcess?.StandardInput.WriteLine("q"); //null-conditional evitei o if (_ffmpegProcess?.HasExited == false)
                _ffmpegProcess?.WaitForExit(1000);
            }
            catch (InvalidOperationException) { /* Ignora erros*/ }

        }
        finally
        {
            _ffmpegProcess?.Kill();
            _readerThread?.Join(2000);
            _ffmpegProcess?.Dispose();
        }
    }
}
