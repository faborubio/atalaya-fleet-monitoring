using System.Globalization;

namespace Atalaya.Persistence;

/// <summary>
/// Convención de nombres de las particiones diarias de <c>telemetry</c>:
/// <c>telemetry_pYYYYMMDD</c>. Centralizado y testeable para que la creación (archivo) y el
/// borrado por retención (AUD-015 p2) hablen del mismo formato.
/// </summary>
public static class PartitionName
{
    private const string Prefix = "telemetry_p";

    public static string ForDate(DateOnly date) => $"{Prefix}{date:yyyyMMdd}";

    /// <summary>Fecha de una partición por su nombre, o <c>null</c> si no encaja con el patrón.</summary>
    public static DateOnly? DateOf(string partition)
    {
        if (!partition.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var stamp = partition[Prefix.Length..];
        return DateOnly.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
