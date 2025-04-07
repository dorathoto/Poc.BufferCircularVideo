namespace Poc.BufferCircularVideo;

public class CircularBuffer
{
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly byte[] _buffer;
    private int _writeIndex;
    private int _readIndex;
    private bool _isFull;

    // Controle simplificado de tempo (sem precisão absoluta)
    private readonly int _maxDurationSeconds = 30; // Buffer para até 30s
    private DateTime _lastWriteTime = DateTime.MinValue;

    public CircularBuffer(int size)
    {
        _buffer = new byte[size];
    }

    public void Write(byte[] data, int offset, int count)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (byte b in data.AsSpan(offset, count))
            {
                _buffer[_writeIndex] = b;
                _writeIndex = (_writeIndex + 1) % _buffer.Length;
                if (_writeIndex == _readIndex) _isFull = true;
                if (_isFull) _readIndex = _writeIndex;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public byte[] ReadLastApproxSeconds(int targetSeconds)
    {
        _lock.EnterReadLock();
        try
        {
            if (_writeIndex == 0 && !_isFull)
                return Array.Empty<byte>();

            // 1. Calcula quantos bytes representam ~20s (com margem)
            int approxBytesPerSecond = _buffer.Length / _maxDurationSeconds;
            int bytesNeeded = approxBytesPerSecond * targetSeconds;

            // 2. Limita ao tamanho do buffer e dados disponíveis
            int availableBytes = _isFull ? _buffer.Length : _writeIndex;
            bytesNeeded = Math.Min(bytesNeeded, availableBytes);
            bytesNeeded = Math.Min(bytesNeeded, (int)(_buffer.Length * 0.8));

            if (bytesNeeded <= 0)
                return Array.Empty<byte>();

            // 3. Cópia circular 
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
        lock (_lock)
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