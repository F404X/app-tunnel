namespace AppTunnel.Core;

public enum UpdateChannel { Stable = 0, Beta = 1, Nightly = 2 }

// AppTunnel release versions: X.Y.Z for stable, X.Y.Z-beta.N for betas and X.Y.Z-nightly.YYYYMMDD.RUN for nightly
// builds, ordered by SemVer 2.0 precedence (numeric parts compared as numbers; build metadata after '+' ignored).
public sealed record SemVersion(int Major, int Minor, int Patch, string[] Prerelease) : IComparable<SemVersion>
{
    public bool IsStable => Prerelease.Length == 0;
    public bool IsBeta => Prerelease is ["beta", _];
    public bool IsNightly => Prerelease is ["nightly", _, _];

    public UpdateChannel Channel => IsNightly ? UpdateChannel.Nightly : IsBeta ? UpdateChannel.Beta : UpdateChannel.Stable;

    public Version Numeric => new(Major, Minor, Patch);

    public static SemVersion? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 256) return null;
        var plus = text.IndexOf('+');
        var core = plus >= 0 ? text[..plus] : text;
        if (core.Length > 64) return null;
        var dash = core.IndexOf('-');
        var numbers = (dash >= 0 ? core[..dash] : core).Split('.');
        if (numbers.Length != 3 || !numbers.All(IsNumber)) return null;
        var prerelease = dash >= 0 ? core[(dash + 1)..].Split('.') : [];
        var version = new SemVersion(int.Parse(numbers[0]), int.Parse(numbers[1]), int.Parse(numbers[2]), prerelease);
        // Only the suffixes AppTunnel publishes are accepted.
        if (prerelease.Length != 0 && !(version.IsBeta && IsNumber(prerelease[1]) ||
            version.IsNightly && prerelease[1].Length == 8 && IsNumber(prerelease[1]) && IsNumber(prerelease[2])))
            return null;
        return version;
    }

    private static bool IsNumber(string part) => part.Length is > 0 and <= 9 && part.All(char.IsAsciiDigit) &&
        (part.Length == 1 || part[0] != '0');

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        var numeric = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (numeric != 0) return numeric;
        // A release sorts above its own prereleases.
        if (IsStable || other.IsStable) return IsStable == other.IsStable ? 0 : IsStable ? 1 : -1;
        for (var i = 0; i < Math.Min(Prerelease.Length, other.Prerelease.Length); i++)
        {
            var a = Prerelease[i];
            var b = other.Prerelease[i];
            var aNumber = a.All(char.IsAsciiDigit);
            var bNumber = b.All(char.IsAsciiDigit);
            var result = aNumber && bNumber ? long.Parse(a).CompareTo(long.Parse(b))
                : aNumber ? -1 : bNumber ? 1 : string.CompareOrdinal(a, b);
            if (result != 0) return result;
        }
        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }

    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;

    public bool Equals(SemVersion? other) => other is not null && CompareTo(other) == 0;
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Prerelease));

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Prerelease.Length == 0 ? "" : "-" + string.Join('.', Prerelease));
}
