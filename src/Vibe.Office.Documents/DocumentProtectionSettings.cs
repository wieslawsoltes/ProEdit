namespace Vibe.Office.Documents;

public sealed class DocumentProtectionSettings
{
    public string? EditMode { get; set; }
    public bool? Enforcement { get; set; }
    public bool? Formatting { get; set; }
    public string? CryptProviderType { get; set; }
    public string? CryptAlgorithmClass { get; set; }
    public string? CryptAlgorithmType { get; set; }
    public int? CryptAlgorithmSid { get; set; }
    public int? CryptSpinCount { get; set; }
    public string? Hash { get; set; }
    public string? Salt { get; set; }

    public bool HasValues =>
        !string.IsNullOrWhiteSpace(EditMode)
        || Enforcement.HasValue
        || Formatting.HasValue
        || !string.IsNullOrWhiteSpace(CryptProviderType)
        || !string.IsNullOrWhiteSpace(CryptAlgorithmClass)
        || !string.IsNullOrWhiteSpace(CryptAlgorithmType)
        || CryptAlgorithmSid.HasValue
        || CryptSpinCount.HasValue
        || !string.IsNullOrWhiteSpace(Hash)
        || !string.IsNullOrWhiteSpace(Salt);

    public DocumentProtectionSettings Clone()
    {
        return new DocumentProtectionSettings
        {
            EditMode = EditMode,
            Enforcement = Enforcement,
            Formatting = Formatting,
            CryptProviderType = CryptProviderType,
            CryptAlgorithmClass = CryptAlgorithmClass,
            CryptAlgorithmType = CryptAlgorithmType,
            CryptAlgorithmSid = CryptAlgorithmSid,
            CryptSpinCount = CryptSpinCount,
            Hash = Hash,
            Salt = Salt
        };
    }
}
