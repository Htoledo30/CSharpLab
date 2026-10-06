namespace CSharpLab.Core.Settings;

/// <summary>Uma única instância pode usar preferências e recuperação da mesma pasta.</summary>
public sealed class InstanceLease : IDisposable
{
    private readonly FileStream _lock;

    private InstanceLease(FileStream file) => _lock = file;

    public static InstanceLease? TryAcquire(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            return new InstanceLease(new FileStream(Path.Combine(directory, "instance.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _lock.Dispose();
}
