using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;

namespace FileAction.Core;

/// <summary>
/// Password protection for PDF files through the already-referenced PDFsharp build. PDFsharp 6.2 writes
/// AES-256 (security handler V5, PDF 2.0) and owner permissions; nothing is faked or downgraded.
/// </summary>
public static class PdfPasswordProtection
{
    /// <summary>Encrypts <paramref name="sourcePath"/> into a new PDF at <paramref name="outputPath"/>.</summary>
    public static void Protect(
        string sourcePath,
        string outputPath,
        string password,
        PdfPermissionPolicy permissions)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (Encoding.UTF8.GetByteCount(password) > 127)
            throw new PasswordProtectionException(
                PasswordProtectionFailure.TooLong, "tool.protect.error.pdfPassword");

        try
        {
            using var document = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Modify);
            if (document.SecuritySettings.IsEncrypted)
                throw new PasswordProtectionException(
                    PasswordProtectionFailure.AlreadyProtected, "tool.protect.error.pdfAlready");

            // AES-256 is defined for PDF 2.0; PDFsharp picks the matching header version.
            document.Version = 20;

            // Choose the handler first so setting a password cannot fall back to 128-bit AES.
            document.SecurityHandler.SetEncryptionToV5(encryptMetadata: true);

            document.SecuritySettings.UserPassword = password;
            // A separate, unrecorded owner password is what makes a "no print / no copy" bit stick for
            // recipients who only know the user password. The owner value is never shown or saved.
            document.SecuritySettings.OwnerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            document.SecuritySettings.PermitPrint = permissions.AllowPrinting;
            document.SecuritySettings.PermitFullQualityPrint = permissions.AllowPrinting;
            document.SecuritySettings.PermitExtractContent = permissions.AllowCopying;
            document.SecuritySettings.PermitModifyDocument = true;
            document.SecuritySettings.PermitAnnotations = true;
            document.SecuritySettings.PermitFormsFill = true;
            document.SecuritySettings.PermitAssembleDocument = true;

            document.Save(outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PasswordProtectionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PasswordProtectionException(
                PasswordProtectionFailure.Generic, "tool.protect.error.pdf", ex);
        }
    }

    /// <summary>Writes an unencrypted copy of a PDF opened with its correct password.</summary>
    public static void Unprotect(string sourcePath, string outputPath, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentException.ThrowIfNullOrEmpty(password);

        try
        {
            using var document = PdfReader.Open(
                sourcePath,
                password,
                PdfDocumentOpenMode.Modify,
                args => args.Abort = true);

            if (!document.SecuritySettings.IsEncrypted)
                throw new PasswordProtectionException(
                    PasswordProtectionFailure.NotEncrypted, "tool.unlock.error.notEncrypted");

            document.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
            document.Save(outputPath);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PasswordProtectionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Some documents opened with the user password refuse an in-place save (owner permissions).
            // Importing the pages into a fresh PDF still produces a readable, unencrypted copy; outlines
            // and form fields are not carried over in that fallback.
        }

        try
        {
            using var source = PdfReader.Open(
                sourcePath,
                password,
                PdfDocumentOpenMode.Import,
                args => args.Abort = true);

            using var target = new PdfDocument();
            for (var index = 0; index < source.PageCount; index++)
                target.AddPage(source.Pages[index]);
            target.Save(outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PasswordProtectionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PasswordProtectionException(
                PasswordProtectionFailure.WrongPasswordOrCorrupt, "tool.unlock.error.password", ex);
        }
    }
}
