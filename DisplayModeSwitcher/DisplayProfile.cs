namespace DisplayModeSwitcher;

public enum ProfileRetentionPolicy
{
    Once,
    Startup,
    Continuous
}

public sealed record DisplayProfile(DisplayMode Mode, ProfileRetentionPolicy Policy = ProfileRetentionPolicy.Startup);

public static class ProfileRetentionPolicyText
{
    public static string ToDisplayName(ProfileRetentionPolicy policy) => policy switch
    {
        ProfileRetentionPolicy.Once => "Einmalig",
        ProfileRetentionPolicy.Startup => "Während Startphase stabilisieren",
        ProfileRetentionPolicy.Continuous => "Dauerhaft erzwingen",
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };

    public static string ToShortDisplayName(ProfileRetentionPolicy policy) => policy switch
    {
        ProfileRetentionPolicy.Once => "Einmalig",
        ProfileRetentionPolicy.Startup => "Startphase",
        ProfileRetentionPolicy.Continuous => "Dauerhaft",
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };
}
