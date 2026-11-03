namespace Zashita.Core;

/// <summary>Кэш JSON-списков по времени изменения файла (без повторной десериализации).</summary>
public sealed class JsonFileListCache<T>
{
    private readonly object _sync = new();
    private List<T>? _items;
    private DateTime _fileWriteUtc = DateTime.MinValue;

    public List<T> GetOrLoad(string filePath, Func<List<T>> loader)
    {
        lock (_sync)
        {
            var writeUtc = File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : DateTime.MinValue;
            if (_items is not null && _fileWriteUtc == writeUtc)
            {
                return _items;
            }

            _items = loader();
            _fileWriteUtc = writeUtc;
            return _items;
        }
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _items = null;
            _fileWriteUtc = DateTime.MinValue;
        }
    }

    public void Replace(List<T> items, string filePath)
    {
        lock (_sync)
        {
            _items = items;
            _fileWriteUtc = File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : DateTime.UtcNow;
        }
    }
}
