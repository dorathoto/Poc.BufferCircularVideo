namespace Poc.BufferCircularVideo;

internal class Program
{
    static void Main(string[] args)
    {
        //aqui seria uma url de RTSP, ex: rtsp://user:password@192.168.1.100:554
        var urlIP = "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";


        var recorder2 = new FFmpegCircularRecorder(urlIP);
        recorder2.Start();
        Console.WriteLine($"Gravação iniciada...{DateTime.Now}");
        
        
        //await Task.Delay(15 * 1000); //automático depois de 15 segundos
        Console.WriteLine("Pressione qualquer tecla para Salvar a gravação do buffer...");
        Console.ReadKey();
        var outputFile = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
        recorder2.SaveClip(outputFile, 10, 10);
        recorder2.Dispose();

        Console.Write($"FIM {outputFile}");
        Console.ReadKey();
    }
}
