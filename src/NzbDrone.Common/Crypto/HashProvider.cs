using System.Security.Cryptography;
using NzbDrone.Common.Disk;

namespace NzbDrone.Common.Crypto;

public interface IHashProvider
{
    byte[] ComputeMd5(string path);
}

public class HashProvider(IDiskProvider diskProvider) : IHashProvider
{
    private readonly IDiskProvider _diskProvider = diskProvider;

    public byte[] ComputeMd5(string path)
    {
        using (var md5 = MD5.Create())
        {
            using (var stream = _diskProvider.OpenReadStream(path))
            {
                return md5.ComputeHash(stream);
            }
        }
    }
}
