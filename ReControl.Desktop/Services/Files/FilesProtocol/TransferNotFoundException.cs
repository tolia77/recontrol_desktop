using System;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Thrown when a file transfer operation references a transferId that is
/// not present in the <see cref="TransferRegistry"/>. Mapped by
/// <see cref="FilesCtlChannel"/> to FilesErrorCode.TRANSFER_NOT_FOUND.
///
/// Note: <c>files.transfer.cancel</c> does NOT throw this -- it returns an
/// empty success envelope when the entry is already gone (cancel-after-complete
/// race). This exception fires from <c>files.upload.complete</c> (the more
/// common case where the registry should still hold the upload).
/// </summary>
public sealed class TransferNotFoundException : Exception
{
    public uint TransferId { get; }

    public TransferNotFoundException(uint transferId)
        : base($"Transfer not found: {transferId}")
    {
        TransferId = transferId;
    }
}
