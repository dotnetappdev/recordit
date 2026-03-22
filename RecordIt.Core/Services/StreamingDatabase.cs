using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace RecordIt.Core.Services;

/// <summary>
/// Persists streaming platform API/stream keys in a local SQLite database.
///
/// Keys are encrypted at rest using AES-256-GCM with a machine-derived
/// entropy key so they are not stored as plain text.
/// </summary>
public class StreamingDatabase : IDisposable
{
    private readonly string _connectionString;
    private bool _initialised;

    public StreamingDatabase(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};";
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    public async Task InitialiseAsync()
    {
        if (_initialised) return;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS streaming_keys (
                platform    TEXT NOT NULL PRIMARY KEY,
                key_blob    BLOB NOT NULL,
                updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """;
        await cmd.ExecuteNonQueryAsync();
        _initialised = true;
    }

    // ── Public CRUD ───────────────────────────────────────────────────────────

    /// <summary>Returns the decrypted stream key for <paramref name="platformId"/>, or null if not set.</summary>
    public async Task<string?> GetStreamKeyAsync(string platformId)
    {
        await EnsureInitialised();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key_blob FROM streaming_keys WHERE platform = $p LIMIT 1;";
        cmd.Parameters.AddWithValue("$p", platformId);

        var result = await cmd.ExecuteScalarAsync();
        if (result is byte[] blob)
            return Decrypt(blob);

        return null;
    }

    /// <summary>Encrypts and upserts the stream key for <paramref name="platformId"/>.</summary>
    public async Task SetStreamKeyAsync(string platformId, string streamKey)
    {
        await EnsureInitialised();
        var blob = Encrypt(streamKey);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO streaming_keys (platform, key_blob, updated_at)
            VALUES ($p, $b, datetime('now'))
            ON CONFLICT(platform) DO UPDATE SET key_blob = excluded.key_blob, updated_at = excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("$p", platformId);
        cmd.Parameters.AddWithValue("$b", blob);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Deletes the stored key for <paramref name="platformId"/>.</summary>
    public async Task DeleteStreamKeyAsync(string platformId)
    {
        await EnsureInitialised();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM streaming_keys WHERE platform = $p;";
        cmd.Parameters.AddWithValue("$p", platformId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Returns all stored platform IDs.</summary>
    public async Task<IReadOnlyList<string>> GetStoredPlatformsAsync()
    {
        await EnsureInitialised();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT platform FROM streaming_keys ORDER BY updated_at DESC;";
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await reader.ReadAsync()) list.Add(reader.GetString(0));
        return list;
    }

    // ── Encryption helpers ────────────────────────────────────────────────────
    // Uses AES-GCM with a per-machine key derived via PBKDF2.
    // Layout of the encrypted blob: [12-byte nonce][16-byte tag][ciphertext]

    private static readonly byte[] s_salt = Encoding.UTF8.GetBytes("RecordIt.StreamingDB.v1");

    private static byte[] DeriveKey()
    {
        // Machine-unique entropy: combine machine name + user name for a
        // stable but device-specific key.  Not a secret in the cryptographic
        // sense, but prevents the DB being trivially copied and read on
        // another machine.
        var entropy = Encoding.UTF8.GetBytes(
            Environment.MachineName + "|" + Environment.UserName);

        return Rfc2898DeriveBytes.Pbkdf2(
            entropy, s_salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    private static byte[] Encrypt(string plaintext)
    {
        var key = DeriveKey();
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Blob = nonce + tag + ciphertext
        var blob = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce,       0, blob, 0,                            nonce.Length);
        Buffer.BlockCopy(tag,         0, blob, nonce.Length,                 tag.Length);
        Buffer.BlockCopy(ciphertext,  0, blob, nonce.Length + tag.Length,    ciphertext.Length);
        return blob;
    }

    private static string Decrypt(byte[] blob)
    {
        var key = DeriveKey();
        const int nonceLen = 12, tagLen = 16;

        var nonce      = blob[..nonceLen];
        var tag        = blob[nonceLen..(nonceLen + tagLen)];
        var ciphertext = blob[(nonceLen + tagLen)..];
        var plaintext  = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, tagLen);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task EnsureInitialised()
    {
        if (!_initialised) await InitialiseAsync();
    }

    public void Dispose() { /* connection is per-operation; nothing to dispose */ }
}
