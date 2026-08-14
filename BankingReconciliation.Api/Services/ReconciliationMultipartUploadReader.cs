using BankingReconciliation.Api.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationMultipartUploadReader : IReconciliationMultipartUploadReader
{
    private const int BoundaryLengthLimit = 128;
    private readonly IReconciliationTemporaryFileStore _temporaryFileStore;
    private readonly long _maxFileSizeBytes;
    private readonly string[] _allowedFileExtensions;

    public ReconciliationMultipartUploadReader(
        IReconciliationTemporaryFileStore temporaryFileStore,
        IOptions<ReconciliationUploadOptions> uploadOptions)
    {
        _temporaryFileStore = temporaryFileStore;
        _maxFileSizeBytes = uploadOptions.Value.MaxCsvFileSizeBytes;
        _allowedFileExtensions = uploadOptions.Value.AllowedFileExtensions;
    }

    public async Task<ReconciliationStreamedUpload> ReadAsync(
        HttpRequest request,
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var boundary = GetBoundary(request.ContentType);
        var reader = new MultipartReader(boundary, request.Body)
        {
            BodyLengthLimit = _maxFileSizeBytes == long.MaxValue
                ? long.MaxValue
                : _maxFileSizeBytes + 1,
            HeadersCountLimit = 16,
            HeadersLengthLimit = 16 * 1024
        };
        string? branchFileName = null;
        string? bankFileName = null;
        string? currentFieldName = null;
        var completed = false;

        try
        {
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
            {
                var (fieldName, fileName) = GetFileSection(section);
                currentFieldName = fieldName;
                var displayFileName = GetSafeDisplayFileName(fileName);
                ValidateFileExtension(fieldName, displayFileName);

                if (string.Equals(fieldName, "branchFile", StringComparison.Ordinal))
                {
                    if (branchFileName is not null)
                    {
                        throw new ReconciliationMultipartUploadException(
                            "DuplicateFileField",
                            "branchFile can only be supplied once.");
                    }

                    var length = await _temporaryFileStore.SaveBranchStreamAsync(
                        batchId,
                        section.Body,
                        cancellationToken);
                    EnsureNotEmpty("branchFile", "MissingBranchFile", length);
                    branchFileName = displayFileName;
                    continue;
                }

                if (string.Equals(fieldName, "bankFile", StringComparison.Ordinal))
                {
                    if (bankFileName is not null)
                    {
                        throw new ReconciliationMultipartUploadException(
                            "DuplicateFileField",
                            "bankFile can only be supplied once.");
                    }

                    var length = await _temporaryFileStore.SaveBankStreamAsync(
                        batchId,
                        section.Body,
                        cancellationToken);
                    EnsureNotEmpty("bankFile", "MissingBankFile", length);
                    bankFileName = displayFileName;
                    continue;
                }

                throw new ReconciliationMultipartUploadException(
                    "UnexpectedFileField",
                    "Only branchFile and bankFile multipart file fields are accepted.");
            }

            if (branchFileName is null)
            {
                throw new ReconciliationMultipartUploadException(
                    "MissingBranchFile",
                    "branchFile is required and cannot be empty.");
            }

            if (bankFileName is null)
            {
                throw new ReconciliationMultipartUploadException(
                    "MissingBankFile",
                    "bankFile is required and cannot be empty.");
            }

            completed = true;
            return new ReconciliationStreamedUpload(branchFileName, bankFileName);
        }
        catch (ReconciliationTemporaryFileLimitException exception)
        {
            throw new ReconciliationMultipartUploadException(
                "FileTooLarge",
                $"{currentFieldName ?? "Uploaded file"} must be {exception.MaxFileSizeBytes} bytes or smaller.");
        }
        catch (InvalidDataException exception)
        {
            throw new ReconciliationMultipartUploadException(
                "InvalidMultipartContent",
                $"Multipart upload could not be read: {exception.Message}");
        }
        finally
        {
            if (!completed)
            {
                await _temporaryFileStore.DeleteAsync(batchId, CancellationToken.None);
            }
        }
    }

    private static string GetBoundary(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReconciliationMultipartUploadException(
                "InvalidMultipartContent",
                "Content-Type must be multipart/form-data.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > BoundaryLengthLimit)
        {
            throw new ReconciliationMultipartUploadException(
                "InvalidMultipartContent",
                "Multipart boundary is missing or too long.");
        }

        return boundary;
    }

    private static (string FieldName, string FileName) GetFileSection(MultipartSection section)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
            !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase) ||
            (!disposition.FileName.HasValue && !disposition.FileNameStar.HasValue))
        {
            throw new ReconciliationMultipartUploadException(
                "UnexpectedMultipartSection",
                "Every multipart section must be a branchFile or bankFile upload.");
        }

        var fieldName = HeaderUtilities.RemoveQuotes(disposition.Name).Value?.Trim();
        var fileName = HeaderUtilities.RemoveQuotes(
            disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
        if (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(fileName))
        {
            throw new ReconciliationMultipartUploadException(
                "InvalidMultipartContent",
                "Multipart file name and field name are required.");
        }

        return (fieldName, fileName);
    }

    private void ValidateFileExtension(string fieldName, string fileName)
    {
        if (!_allowedFileExtensions.Contains(
                Path.GetExtension(fileName),
                StringComparer.OrdinalIgnoreCase))
        {
            throw new ReconciliationMultipartUploadException(
                "InvalidFileExtension",
                $"{fieldName} must use one of these extensions: {string.Join(", ", _allowedFileExtensions)}.");
        }
    }

    private static void EnsureNotEmpty(string fieldName, string errorCode, long length)
    {
        if (length == 0)
        {
            throw new ReconciliationMultipartUploadException(
                errorCode,
                $"{fieldName} is required and cannot be empty.");
        }
    }

    private static string GetSafeDisplayFileName(string fileName)
    {
        var normalized = fileName.Replace('\\', '/');
        var displayName = normalized[(normalized.LastIndexOf('/') + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "uploaded-file";
        }

        return displayName.Length <= 260 ? displayName : displayName[..260];
    }
}
