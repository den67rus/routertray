using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace RouterTray;

internal sealed class KeeneticNdw4Keys : IDisposable
{
    private const string ClientKeyLabel = "NDW4 Interactive Client Key";
    private const string ServerKeyLabel = "NDW4 Interactive Server Key";

    private readonly byte[] _clientKey;
    private readonly byte[] _storedKey;
    private readonly byte[] _serverKey;
    private bool _disposed;

    private KeeneticNdw4Keys(byte[] clientKey, byte[] storedKey, byte[] serverKey)
    {
        _clientKey = clientKey;
        _storedKey = storedKey;
        _serverKey = serverKey;
    }

    public static KeeneticNdw4Keys Derive(
        string password,
        byte[] salt,
        int iterations,
        int memoryCost)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var derivedKey = new byte[64];
        try
        {
            var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
                .WithVersion(Argon2Parameters.Version13)
                .WithSalt(salt)
                .WithIterations(iterations)
                .WithMemoryAsKB(memoryCost)
                .WithParallelism(1)
                .Build();

            var generator = new Argon2BytesGenerator();
            generator.Init(parameters);
            generator.GenerateBytes(passwordBytes, derivedKey);

            var clientKey = ComputeHmac(derivedKey, Encoding.UTF8.GetBytes(ClientKeyLabel));
            var storedKey = ComputeSha3(clientKey);
            var serverKey = ComputeHmac(derivedKey, Encoding.UTF8.GetBytes(ServerKeyLabel));
            return new KeeneticNdw4Keys(clientKey, storedKey, serverKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    public byte[] CreateClientProof(string authenticationMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var signature = ComputeHmac(
            _storedKey,
            Encoding.UTF8.GetBytes(authenticationMessage));
        try
        {
            return Xor(_clientKey, signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    public bool VerifyServerSignature(string authenticationMessage, byte[] signature)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(signature);

        var expected = ComputeHmac(
            _serverKey,
            Encoding.UTF8.GetBytes(authenticationMessage));
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static byte[] ComputeSha3(byte[] data)
    {
        var digest = new Sha3Digest(512);
        digest.BlockUpdate(data, 0, data.Length);
        var result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return result;
    }

    private static byte[] ComputeHmac(byte[] key, byte[] data)
    {
        var hmac = new HMac(new Sha3Digest(512));
        hmac.Init(new KeyParameter(key));
        hmac.BlockUpdate(data, 0, data.Length);
        var result = new byte[hmac.GetMacSize()];
        hmac.DoFinal(result, 0);
        return result;
    }

    private static byte[] Xor(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            throw new CryptographicException("NDW4 proof operands have different lengths.");
        }

        var result = new byte[left.Length];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = (byte)(left[index] ^ right[index]);
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_clientKey);
        CryptographicOperations.ZeroMemory(_storedKey);
        CryptographicOperations.ZeroMemory(_serverKey);
        _disposed = true;
    }
}
