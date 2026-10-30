namespace VoteGlass.Services;

public static class AppTime
{
    private static TimeZoneInfo Zone => TimeZoneInfo.Local;

    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static string Format(DateTime utc) => ToLocal(utc).ToString("dd MMM yyyy, h:mm tt");
}