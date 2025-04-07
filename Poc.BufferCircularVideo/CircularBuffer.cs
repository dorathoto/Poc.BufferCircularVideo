namespace Poc.BufferCircularVideo;

/// <summary>
/// 
/// </summary>
public class CircularBuffer
{
    // Controle de thread safety para acesso concorrente ao buffer
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

    private readonly byte[] _buffer;          // Armazenamento principal dos dados de vídeo
    private int _writeIndex;                  // Posição atual de escrita
    private int _readIndex;                   // Posição atual de leitura
    private bool _isFull;                     // Flag indicando se o buffer está cheio
    
    
    // Controle temporal simplificado para gestão do conteúdo do buffer
    private readonly int _maxDurationSeconds = 30; // Capacidade máxima do buffer em segundos
    private DateTime _lastWriteTime = DateTime.MinValue; // Último registro de escrita

    public CircularBuffer(int size)
    {
        _buffer = new byte[size]; // Inicializa o buffer com o tamanho especificado
    }

    public void Write(byte[] data, int offset, int count)
    {
        _lock.EnterWriteLock(); // Bloqueio exclusivo para escrita
        try
        {
            foreach (byte b in data.AsSpan(offset, count))
            {
                _buffer[_writeIndex] = b;
                _writeIndex = (_writeIndex + 1) % _buffer.Length;       // Avança índice circularmente

                // Atualiza estado do buffer
                if (_writeIndex == _readIndex) _isFull = true;  // Buffer cheio quando índices coincidem
                if (_isFull) _readIndex = _writeIndex;          // Mantém o buffer sempre sobrescrevendo o mais antigo quando cheio
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public byte[] ReadLastApproxSeconds(int targetSeconds)
    {
        _lock.EnterReadLock();      // Bloqueio compartilhado para leitura
        try
        {
            // Retorna vazio se buffer nunca foi escrito
            if (_writeIndex == 0 && !_isFull)
                return Array.Empty<byte>();

            // 1. Calcula quantos bytes representam ~20s (com margem)
            int approxBytesPerSecond = _buffer.Length / _maxDurationSeconds;
            int bytesNeeded = approxBytesPerSecond * targetSeconds;

            // 2. Limita ao tamanho do buffer e dados disponíveis
            int availableBytes = _isFull ? _buffer.Length : _writeIndex;
            bytesNeeded = Math.Min(bytesNeeded, availableBytes);
            bytesNeeded = Math.Min(bytesNeeded, (int)(_buffer.Length * 0.8)); // Limite de 80% do buffer

            if (bytesNeeded <= 0)
                return Array.Empty<byte>();

            // 3. Cópia circular -  // Lógica de cópia circular considerando wrap-around do buffer
            byte[] result = new byte[bytesNeeded];
            int startIndex = (_writeIndex - bytesNeeded + _buffer.Length) % _buffer.Length;

            if (startIndex + bytesNeeded <= _buffer.Length)
            {
                Array.Copy(_buffer, startIndex, result, 0, bytesNeeded);
            }
            else
            {
                int firstPart = _buffer.Length - startIndex;
                Array.Copy(_buffer, startIndex, result, 0, firstPart);
                Array.Copy(_buffer, 0, result, firstPart, bytesNeeded - firstPart);
            }

            return result;
        }
        finally
        {
            _lock.ExitReadLock();
        }

    }


    public byte[] ReadLastByTime(int milliseconds, int _currentBitrateKbps)
    {
        lock (_lock)// Bloqueio exclusivo para operação combinada
        {
            if (_writeIndex == 0 && !_isFull)
                return Array.Empty<byte>();


            // CORREÇÃO: Usar _currentBitrateKbps corretamente (Kbps → bytes/segundo)
            double seconds = milliseconds / 1000.0;
            int bytesNeeded = (int)((_currentBitrateKbps * 1024L / 8) * seconds);

            // Usa 80% do buffer como limite máximo para evitar overreads
            int maxReadableBytes = (int)(_buffer.Length * 0.8);
            bytesNeeded = Math.Min(bytesNeeded, maxReadableBytes);

            // Ajusta para dados disponíveis
            int availableBytes = _isFull ? _buffer.Length : _writeIndex;
            int bytesToRead = Math.Min(bytesNeeded, availableBytes);

            // logs pra debug
            Console.WriteLine($"[DEBUG] Bitrate usado: {_currentBitrateKbps} Kbps");
            Console.WriteLine($"[DEBUG] Bytes necessários: {bytesNeeded}");
            Console.WriteLine($"[DEBUG] Bytes lidos: {bytesToRead}");


            // Cálculo do índice de início
            int startIndex = (_writeIndex - bytesToRead + _buffer.Length) % _buffer.Length;

            // Cópia dos dados
            byte[] result = new byte[bytesToRead];
            if (startIndex + bytesToRead <= _buffer.Length)
            {
                Array.Copy(_buffer, startIndex, result, 0, bytesToRead);
            }
            else
            {
                int firstPart = _buffer.Length - startIndex;
                Array.Copy(_buffer, startIndex, result, 0, firstPart);
                Array.Copy(_buffer, 0, result, firstPart, bytesToRead - firstPart);
            }

            return result;
        }
    }
}