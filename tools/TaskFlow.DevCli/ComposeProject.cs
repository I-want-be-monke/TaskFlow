using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TaskFlow.DevCli;

internal sealed class ComposeProject
{
    private const int MaxProjectNameLength = 63;

    public ComposeProject(RepositoryContext repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        string normalizedRoot = NormalizeRoot(repository.Root);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot));
        var configured = Environment.GetEnvironmentVariable("TASKFLOW_COMPOSE_PROJECT_NAME");

        Name = string.IsNullOrWhiteSpace(configured)
            ? BuildName(repository.Root, hash)
            : Validate(configured.Trim());

        int portOffset = BinaryPrimitives.ReadUInt16BigEndian(hash.AsSpan(6, 2)) % 10000;
        DefaultHttpsPort = 20000 + portOffset;

        int subnetOctet = 20 + (hash[8] % 200);
        DefaultBackendSubnet = $"172.28.{subnetOctet}.0/24";
        DefaultProxyIp = $"172.28.{subnetOctet}.10";
    }

    public string Name { get; }

    public int DefaultHttpsPort { get; }

    public string DefaultBackendSubnet { get; }

    public string DefaultProxyIp { get; }

    public string PostgresVolumeName => $"{Name}_taskflow-postgres";

    private static string BuildName(string repositoryRoot, ReadOnlySpan<byte> hash)
    {
        var folderName = new DirectoryInfo(repositoryRoot).Name;
        var slug = Slugify(folderName);
        var suffix = Convert.ToHexString(hash[..6]).ToLowerInvariant();

        const string prefix = "taskflow-";
        var maxSlugLength = MaxProjectNameLength - prefix.Length - 1 - suffix.Length;
        if (slug.Length > maxSlugLength)
        {
            slug = slug[..maxSlugLength].Trim('-');
        }

        if (string.IsNullOrEmpty(slug))
        {
            slug = "local";
        }

        return $"{prefix}{slug}-{suffix}";
    }

    private static string NormalizeRoot(string root)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return OperatingSystem.IsWindows()
            ? fullPath.ToUpperInvariant()
            : fullPath;
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasDash = false;

        foreach (var character in value.ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
                previousWasDash = false;
                continue;
            }

            if (!previousWasDash && builder.Length > 0)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string Validate(string name)
    {
        if (name.Length > MaxProjectNameLength)
        {
            throw new ArgumentException(
                $"TASKFLOW_COMPOSE_PROJECT_NAME must be at most {MaxProjectNameLength} characters.");
        }

        if (name.Length == 0 ||
            !IsLowerAlphaNumeric(name[0]) ||
            name.Any(character => !IsLowerAlphaNumeric(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "TASKFLOW_COMPOSE_PROJECT_NAME must start with a lowercase letter or digit and contain only lowercase letters, digits, '-' or '_'.");
        }

        return name;
    }

    private static bool IsLowerAlphaNumeric(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';
}
