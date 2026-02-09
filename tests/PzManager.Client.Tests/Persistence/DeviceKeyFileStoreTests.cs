using PzManager.Client.Persistence;
using Xunit;

namespace PzManager.Client.Tests.Persistence;

public sealed class DeviceKeyFileStoreTests
{
    [Fact]
    public void LoadOrCreate_CreatesAndLoads()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "device-key.json");
            var k1 = DeviceKeyFileStore.LoadOrCreate(path);
            var k2 = DeviceKeyFileStore.LoadOrCreate(path);

            Assert.Equal(k1.PublicKey, k2.PublicKey);
            Assert.Equal(k1.PrivateKey, k2.PrivateKey);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "device-key.json");
            var key = DeviceKeyFileStore.LoadOrCreate(path);

            DeviceKeyFileStore.Save(path, key);
            var loaded = DeviceKeyFileStore.Load(path);

            Assert.Equal(key.PublicKey, loaded.PublicKey);
            Assert.Equal(key.PrivateKey, loaded.PrivateKey);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pzmanager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }
}

