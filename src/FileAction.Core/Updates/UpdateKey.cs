namespace FileAction.Core.Updates;

/// <summary>
/// The update signing public key (ECDSA P-256, SubjectPublicKeyInfo, base64). The matching private key exists
/// only as the <c>UPDATE_SIGNING_KEY</c> GitHub Actions secret; it is never in this repository.
/// The app refuses any update whose <c>.sig</c> does not verify against this exact key.
/// </summary>
public static class UpdateKey
{
    public const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUCQv0in24CEgz0uiGoER/25EFZY5w/x0wvpl9TyRyG8VxFTqv3mKuGBxMwoknk9FBHrj2P9c56kCUU2POE5Glw==";
}
