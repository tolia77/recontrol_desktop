#nullable disable
#pragma warning disable CS8618, CS8632
namespace ReControl.Desktop.Protocol.Generated
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    /// <summary>
    /// files-ctl wire protocol: request/response envelopes and payloads for every command the
    /// web UI may invoke on the desktop client. Transferred as JSON over the 'files-ctl' WebRTC
    /// data channel. Phase 9 introduced request/response; Phase 11 adds server-pushed event
    /// envelopes (status:'event') and the six transfer-control commands
    /// (files.upload.begin/complete, files.download.begin/complete, files.transfer.cancel,
    /// files.transfer.error).
    /// </summary>
    public partial class FilesCtlTypes
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("error")]
        public FilesError Error { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("errorCode")]
        public FilesErrorCode? ErrorCode { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("errorEnvelope")]
        public FilesErrorEnvelope ErrorEnvelope { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("fileEntry")]
        public FileEntry FileEntry { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesCopyRequest")]
        public FilesCopyRequest FilesCopyRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesCopyResponse")]
        public FilesCopyResponse FilesCopyResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesDeleteRequest")]
        public FilesDeleteRequest FilesDeleteRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesDeleteResponse")]
        public FilesDeleteResponse FilesDeleteResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesDownloadBeginRequest")]
        public FilesDownloadBeginRequest FilesDownloadBeginRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesDownloadBeginResponse")]
        public FilesDownloadBeginResponse FilesDownloadBeginResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesDownloadCompletePayload")]
        public FilesDownloadCompletePayload FilesDownloadCompletePayload { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesEventEnvelope")]
        public FilesEventEnvelope FilesEventEnvelope { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesListRequest")]
        public FilesListRequest FilesListRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesListResponse")]
        public FilesListResponse FilesListResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesListRootsRequest")]
        public FilesListRootsRequest FilesListRootsRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesListRootsResponse")]
        public FilesListRootsResponse FilesListRootsResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesMkdirRequest")]
        public FilesMkdirRequest FilesMkdirRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesMkdirResponse")]
        public FilesMkdirResponse FilesMkdirResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesMoveRequest")]
        public FilesMoveRequest FilesMoveRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesMoveResponse")]
        public FilesMoveResponse FilesMoveResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesRenameRequest")]
        public FilesRenameRequest FilesRenameRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesRenameResponse")]
        public FilesRenameResponse FilesRenameResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesTransferCancelRequest")]
        public FilesTransferCancelRequest FilesTransferCancelRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesTransferCancelResponse")]
        public FilesTransferCancelResponse FilesTransferCancelResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesTransferErrorPayload")]
        public FilesTransferErrorPayload FilesTransferErrorPayload { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesUploadBeginRequest")]
        public FilesUploadBeginRequest FilesUploadBeginRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesUploadBeginResponse")]
        public FilesUploadBeginResponse FilesUploadBeginResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesUploadCompleteRequest")]
        public FilesUploadCompleteRequest FilesUploadCompleteRequest { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("filesUploadCompleteResponse")]
        public FilesUploadCompleteResponse FilesUploadCompleteResponse { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("invalidNameReason")]
        public InvalidNameReason? InvalidNameReason { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("nameConflictMode")]
        public NameConflictMode? NameConflictMode { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("request")]
        public FilesRequestEnvelope Request { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("success")]
        public FilesSuccessEnvelope Success { get; set; }
    }

    /// <summary>
    /// Structured error object carried in FilesErrorEnvelope.error. Code is stable; message is a
    /// fallback string the backend may supply for developer logs (the frontend should key off
    /// code for user-facing text). Data is a free-form object whose shape depends on code (e.g.,
    /// INVALID_NAME uses { reason: InvalidNameReason }).
    ///
    /// Structured error describing why the request failed.
    ///
    /// Structured FilesError describing the failure (typical codes: STALLED, IO_ERROR,
    /// DISK_FULL, INTERNAL_ERROR).
    /// </summary>
    public partial class FilesError
    {
        /// <summary>
        /// Stable machine-readable identifier for the error class.
        /// </summary>
        [JsonPropertyName("code")]
        public FilesErrorCode Code { get; set; }

        /// <summary>
        /// Optional structured context whose shape depends on code. For INVALID_NAME: { reason:
        /// InvalidNameReason }. For ALLOWLIST_VIOLATION: { path: string }. For IO_ERROR: { errno?:
        /// string }.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("data")]
        public Dictionary<string, object> Data { get; set; }

        /// <summary>
        /// Fallback human-readable description for logs / debugging. Not a user-facing string.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    /// <summary>
    /// Negative response envelope. status is always 'error' and error carries the structured
    /// FilesError.
    /// </summary>
    public partial class FilesErrorEnvelope
    {
        /// <summary>
        /// Structured error describing why the request failed.
        /// </summary>
        [JsonPropertyName("error")]
        public FilesError Error { get; set; }

        /// <summary>
        /// Same correlation id as the request.
        /// </summary>
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        /// <summary>
        /// Discriminator: always the literal string 'error' on this envelope.
        /// </summary>
        [JsonPropertyName("status")]
        public ErrorEnvelopeStatus Status { get; set; }
    }

    /// <summary>
    /// Metadata for a single file or directory within an allowlisted root.
    /// </summary>
    public partial class FileEntry
    {
        /// <summary>
        /// True if the entry is a directory, false for regular files. Symlinks that escape the
        /// allowlist are never returned.
        /// </summary>
        [JsonPropertyName("isDirectory")]
        public bool IsDirectory { get; set; }

        /// <summary>
        /// True if the entry is hidden per the host platform: FileAttributes.Hidden on Windows,
        /// leading dot on POSIX. Frontend filters these out by default and exposes a 'Show hidden
        /// files' toggle.
        /// </summary>
        [JsonPropertyName("isHidden")]
        public bool IsHidden { get; set; }

        /// <summary>
        /// Last-modified timestamp in ISO-8601 / RFC-3339 UTC form (e.g. 2026-04-24T08:25:31Z).
        /// </summary>
        [JsonPropertyName("modifiedUtc")]
        public DateTimeOffset ModifiedUtc { get; set; }

        /// <summary>
        /// Base name of the entry (no path separators).
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Canonical absolute path of the entry as observed by the desktop after allowlist
        /// resolution.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }

        /// <summary>
        /// Size in bytes. For directories, value is 0 and callers should ignore it.
        /// </summary>
        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }
    }

    /// <summary>
    /// Request payload for files.copy. Copies a single file from src to dst. Directory copy is
    /// out of scope for Phase 9.
    /// </summary>
    public partial class FilesCopyRequest
    {
        /// <summary>
        /// Absolute canonical destination path. Parent must exist.
        /// </summary>
        [JsonPropertyName("dst")]
        public string Dst { get; set; }

        /// <summary>
        /// Conflict behavior when destination name already exists.
        /// </summary>
        [JsonPropertyName("mode")]
        public NameConflictMode Mode { get; set; }

        /// <summary>
        /// Absolute canonical source path (file, not directory).
        /// </summary>
        [JsonPropertyName("src")]
        public string Src { get; set; }
    }

    /// <summary>
    /// Response payload for files.copy. Echoes the resolved src and dst.
    /// </summary>
    public partial class FilesCopyResponse
    {
        /// <summary>
        /// Canonical destination path of the created copy.
        /// </summary>
        [JsonPropertyName("dst")]
        public string Dst { get; set; }

        /// <summary>
        /// Canonical source path.
        /// </summary>
        [JsonPropertyName("src")]
        public string Src { get; set; }
    }

    /// <summary>
    /// Request payload for files.delete. Deletes a file or empty directory; recursive deletion
    /// is explicitly out of scope for Phase 9.
    /// </summary>
    public partial class FilesDeleteRequest
    {
        /// <summary>
        /// Absolute canonical path to delete.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Response payload for files.delete. No fields; an empty object indicates success.
    /// </summary>
    public partial class FilesDeleteResponse
    {
    }

    /// <summary>
    /// Request payload for files.download.begin. Asks the desktop to start streaming the bytes
    /// of path over files-data. The desktop allocates a transferId and begins sending chunks.
    /// </summary>
    public partial class FilesDownloadBeginRequest
    {
        /// <summary>
        /// Absolute canonical path of the file to download. Must resolve inside an allowlisted root
        /// and must be a regular file.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Response payload for files.download.begin. Returns the desktop-allocated transferId,
    /// total size, and base name so the browser can preallocate buffers and surface filename in
    /// UI.
    /// </summary>
    public partial class FilesDownloadBeginResponse
    {
        /// <summary>
        /// Base name of the file (no path separators). Used by the browser to suggest a Save-As
        /// filename.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Total size of the file in bytes as observed by the desktop at request time.
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// Unsigned 32-bit identifier allocated by the desktop. Every files-data chunk header will
        /// carry this id.
        /// </summary>
        [JsonPropertyName("transferId")]
        public long TransferId { get; set; }
    }

    /// <summary>
    /// Payload for the files.download.complete server-pushed event. Sent by the desktop on the
    /// files-ctl channel after the last chunk for transferId has been written to files-data, so
    /// the browser knows when to finalize / save the assembled bytes.
    /// </summary>
    public partial class FilesDownloadCompletePayload
    {
        /// <summary>
        /// Total bytes streamed. Browser uses this to verify reassembly length.
        /// </summary>
        [JsonPropertyName("totalBytes")]
        public long TotalBytes { get; set; }

        /// <summary>
        /// Identifier of the download that just finished.
        /// </summary>
        [JsonPropertyName("transferId")]
        public long TransferId { get; set; }
    }

    /// <summary>
    /// Server-pushed event envelope. Distinguished from request/response envelopes by
    /// status:'event'. Used for files.download.complete and files.transfer.error in Phase 11.
    /// The frontend's FilesChannelClient dispatches these to listeners registered by command
    /// name (NOT correlated by id).
    /// </summary>
    public partial class FilesEventEnvelope
    {
        /// <summary>
        /// Event identifier, e.g. 'files.download.complete' or 'files.transfer.error'.
        /// </summary>
        [JsonPropertyName("command")]
        public string Command { get; set; }

        /// <summary>
        /// Event-specific payload. Shape depends on command.
        /// </summary>
        [JsonPropertyName("payload")]
        public Dictionary<string, object> Payload { get; set; }

        [JsonPropertyName("status")]
        public FilesEventEnvelopeStatus Status { get; set; }
    }

    /// <summary>
    /// Request payload for files.list. Lists immediate children of path (non-recursive).
    /// </summary>
    public partial class FilesListRequest
    {
        /// <summary>
        /// Absolute canonical path to list. Must resolve inside an allowlisted root after symlink
        /// resolution.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Response payload for files.list. Mirrors the requested path and carries the enumerated
    /// entries.
    /// </summary>
    public partial class FilesListResponse
    {
        /// <summary>
        /// Direct children of path. Does not include '.' or '..'. Sort order is unspecified; the UI
        /// sorts client-side.
        /// </summary>
        [JsonPropertyName("entries")]
        public List<FileEntry> Entries { get; set; }

        /// <summary>
        /// Canonical path that was listed. Echoes the request after canonicalization.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Request payload for files.listRoots. No parameters.
    /// </summary>
    public partial class FilesListRootsRequest
    {
    }

    /// <summary>
    /// Response payload for files.listRoots. Returns the allowlisted roots the caller is
    /// permitted to browse.
    /// </summary>
    public partial class FilesListRootsResponse
    {
        /// <summary>
        /// Allowlisted roots exposed to the browser. Each entry is presented as a FileEntry with
        /// isDirectory=true.
        /// </summary>
        [JsonPropertyName("roots")]
        public List<FileEntry> Roots { get; set; }
    }

    /// <summary>
    /// Request payload for files.mkdir. Creates a single new subdirectory inside parentPath.
    /// </summary>
    public partial class FilesMkdirRequest
    {
        /// <summary>
        /// Name of the new directory. Must pass name validation (no reserved names, no illegal
        /// chars).
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Absolute canonical path of the parent directory. Must resolve inside an allowlisted root.
        /// </summary>
        [JsonPropertyName("parentPath")]
        public string ParentPath { get; set; }
    }

    /// <summary>
    /// Response payload for files.mkdir. Returns the path of the newly created directory.
    /// </summary>
    public partial class FilesMkdirResponse
    {
        /// <summary>
        /// Canonical path of the created directory.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Request payload for files.move. Moves src to dst; both must resolve inside allowlisted
    /// roots.
    /// </summary>
    public partial class FilesMoveRequest
    {
        /// <summary>
        /// Absolute canonical destination path. Parent must exist.
        /// </summary>
        [JsonPropertyName("dst")]
        public string Dst { get; set; }

        /// <summary>
        /// Conflict behavior when destination name already exists.
        /// </summary>
        [JsonPropertyName("mode")]
        public NameConflictMode Mode { get; set; }

        /// <summary>
        /// Absolute canonical source path.
        /// </summary>
        [JsonPropertyName("src")]
        public string Src { get; set; }
    }

    /// <summary>
    /// Response payload for files.move. Echoes the resolved src and dst.
    /// </summary>
    public partial class FilesMoveResponse
    {
        /// <summary>
        /// Canonical destination path after the move.
        /// </summary>
        [JsonPropertyName("dst")]
        public string Dst { get; set; }

        /// <summary>
        /// Canonical source path as it was at request time.
        /// </summary>
        [JsonPropertyName("src")]
        public string Src { get; set; }
    }

    /// <summary>
    /// Request payload for files.rename. Renames path to a sibling with newName; does not move
    /// across directories.
    /// </summary>
    public partial class FilesRenameRequest
    {
        /// <summary>
        /// New base name (no path separators). Must pass name validation.
        /// </summary>
        [JsonPropertyName("newName")]
        public string NewName { get; set; }

        /// <summary>
        /// Absolute canonical path of the entry to rename.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Response payload for files.rename. Returns the new canonical path.
    /// </summary>
    public partial class FilesRenameResponse
    {
        /// <summary>
        /// Canonical path of the entry after rename.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Request payload for files.transfer.cancel. Aborts an in-flight upload or download
    /// identified by transferId. Idempotent: cancelling an already-finished or already-cancelled
    /// transfer returns success.
    /// </summary>
    public partial class FilesTransferCancelRequest
    {
        /// <summary>
        /// Why the cancel is being issued. Pinned at schema level to prevent drift; receivers MAY
        /// treat unknown values as 'user'.
        /// </summary>
        [JsonPropertyName("reason")]
        public Reason Reason { get; set; }

        /// <summary>
        /// Identifier of the transfer to cancel, as returned by files.upload.begin or
        /// files.download.begin.
        /// </summary>
        [JsonPropertyName("transferId")]
        public long TransferId { get; set; }
    }

    /// <summary>
    /// Response payload for files.transfer.cancel. No fields; an empty object indicates success.
    /// </summary>
    public partial class FilesTransferCancelResponse
    {
    }

    /// <summary>
    /// Payload for the files.transfer.error server-pushed event. Sent by the desktop when an
    /// in-flight transfer fails (stall, IO error, disk full, etc). The frontend dispatches by
    /// command name and matches transferId to the active transfer.
    /// </summary>
    public partial class FilesTransferErrorPayload
    {
        /// <summary>
        /// Structured FilesError describing the failure (typical codes: STALLED, IO_ERROR,
        /// DISK_FULL, INTERNAL_ERROR).
        /// </summary>
        [JsonPropertyName("error")]
        public FilesError Error { get; set; }

        /// <summary>
        /// Identifier of the transfer that failed.
        /// </summary>
        [JsonPropertyName("transferId")]
        public long TransferId { get; set; }
    }

    /// <summary>
    /// Request payload for files.upload.begin. Reserves a transferId on the desktop side for an
    /// upload (browser->desktop) and returns the .partial path that will receive bytes over the
    /// files-data channel.
    /// </summary>
    public partial class FilesUploadBeginRequest
    {
        /// <summary>
        /// Conflict behavior when destination name already exists.
        /// </summary>
        [JsonPropertyName("mode")]
        public NameConflictMode Mode { get; set; }

        /// <summary>
        /// Final base name of the uploaded file (no path separators). Must pass name validation.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Absolute canonical path of the parent directory. Must resolve inside an allowlisted root.
        /// </summary>
        [JsonPropertyName("parentPath")]
        public string ParentPath { get; set; }

        /// <summary>
        /// Total size of the file in bytes as known to the browser. Used by the desktop to decide on
        /// disk-space and to verify completion.
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    /// <summary>
    /// Response payload for files.upload.begin. Returns the desktop-allocated transferId and the
    /// .partial path the desktop will write incoming chunks to.
    /// </summary>
    public partial class FilesUploadBeginResponse
    {
        /// <summary>
        /// Canonical absolute path of the .partial file the desktop will write into. Renamed to the
        /// final name on files.upload.complete.
        /// </summary>
        [JsonPropertyName("partialPath")]
        public string PartialPath { get; set; }

        /// <summary>
        /// Unsigned 32-bit identifier allocated by the desktop. The browser MUST echo this in every
        /// chunk header on files-data and in any subsequent files.upload.complete /
        /// files.transfer.cancel for this transfer.
        /// </summary>
        [JsonPropertyName("transferId")]
        public long TransferId { get; set; }
    }

    /// <summary>
    /// Request payload for files.upload.complete. Signals that all bytes have been sent on
    /// files-data. The desktop verifies the .partial size matches expectedBytes, then renames
    /// .partial to the final name.
    /// </summary>
    public partial class FilesUploadCompleteRequest
    {
        /// <summary>
        /// Total bytes the browser sent on files-data for this transfer. Desktop rejects if .partial
        /// size differs.
        /// </summary>
        [JsonPropertyName("expectedBytes")]
        public long ExpectedBytes { get; set; }

        /// <summary>
        /// Identifier returned by the matching files.upload.begin response.
        /// </summary>
        [JsonPropertyName("transferId")]
        public long TransferId { get; set; }
    }

    /// <summary>
    /// Response payload for files.upload.complete. Returns the canonical path of the final file
    /// after the .partial -> final rename.
    /// </summary>
    public partial class FilesUploadCompleteResponse
    {
        /// <summary>
        /// Canonical absolute path of the finalized file.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Every request sent from the frontend to the desktop on the files-ctl channel. id is a
    /// UUID chosen by the caller; the same id appears on the matching response envelope.
    /// </summary>
    public partial class FilesRequestEnvelope
    {
        /// <summary>
        /// Dot-namespaced command identifier, e.g. files.list.
        /// </summary>
        [JsonPropertyName("command")]
        public string Command { get; set; }

        /// <summary>
        /// Caller-chosen correlation id (UUID v4 recommended).
        /// </summary>
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        /// <summary>
        /// Command-specific request payload. Shape depends on command.
        /// </summary>
        [JsonPropertyName("payload")]
        public Dictionary<string, object> Payload { get; set; }
    }

    /// <summary>
    /// Positive response envelope. result shape depends on the request command.
    /// </summary>
    public partial class FilesSuccessEnvelope
    {
        /// <summary>
        /// Same correlation id as the request.
        /// </summary>
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        /// <summary>
        /// Command-specific success result. May be null for commands whose response carries no data
        /// (e.g. files.delete).
        /// </summary>
        [JsonPropertyName("result")]
        public Dictionary<string, object> Result { get; set; }

        /// <summary>
        /// Discriminator: always the literal string 'success' on this envelope.
        /// </summary>
        [JsonPropertyName("status")]
        public SuccessStatus Status { get; set; }
    }

    /// <summary>
    /// Stable machine-readable identifier for the error class.
    ///
    /// Stable machine-readable error codes. Codes are frozen for the lifetime of the protocol;
    /// add new codes rather than repurposing old ones. Human-readable messages are produced from
    /// these codes by the frontend i18n layer in Phase 12. Phase-11 additions
    /// (TRANSFER_NOT_FOUND, CANCELLED, STALLED, DISK_FULL) cover transfer-pipeline cancel races,
    /// stall pushes, and disk-full reports. Phase-12 additions (PERMISSION_READ,
    /// PERMISSION_WRITE, SOURCE_GONE, DESTINATION_GONE, NAME_CONFLICT) split permission errors
    /// by direction and add explicit codes for missing source/destination and destination-name
    /// collisions so the frontend can render actionable dialogs without parsing free-text
    /// messages.
    /// </summary>
    public enum FilesErrorCode { AllowlistViolation, Cancelled, ChannelNotOpen, DestinationGone, DiskFull, Disposed, InternalError, InvalidName, IoError, MalformedResponse, NameConflict, NotFound, PermissionDenied, PermissionRead, PermissionWrite, SourceGone, Stalled, Timeout, TransferNotFound, UnknownCommand };

    public enum ErrorEnvelopeStatus { Error };

    /// <summary>
    /// Conflict behavior when destination name already exists.
    ///
    /// Name-conflict behavior for upload/move/copy commands.
    /// </summary>
    public enum NameConflictMode { Fail, KeepBoth, Replace, Skip };

    public enum FilesEventEnvelopeStatus { Event };

    /// <summary>
    /// Why the cancel is being issued. Pinned at schema level to prevent drift; receivers MAY
    /// treat unknown values as 'user'.
    /// </summary>
    public enum Reason { DesktopError, Disconnect, Stalled, User };

    /// <summary>
    /// Refinement of INVALID_NAME errors so the UI can render a specific reason without parsing
    /// free-text messages.
    /// </summary>
    public enum InvalidNameReason { DotOnly, Empty, IllegalChar, Reserved, TooLong, TrailingSpaceOrDot };

    public enum SuccessStatus { Success };

    internal static class Converter
    {
        public static readonly JsonSerializerOptions Settings = new(JsonSerializerDefaults.General)
        {
            Converters =
            {
                FilesErrorCodeConverter.Singleton,
                ErrorEnvelopeStatusConverter.Singleton,
                NameConflictModeConverter.Singleton,
                FilesEventEnvelopeStatusConverter.Singleton,
                ReasonConverter.Singleton,
                InvalidNameReasonConverter.Singleton,
                SuccessStatusConverter.Singleton,
                new DateOnlyConverter(),
                new TimeOnlyConverter(),
                IsoDateTimeOffsetConverter.Singleton
            },
        };
    }

    internal class FilesErrorCodeConverter : JsonConverter<FilesErrorCode>
    {
        public override bool CanConvert(Type t) => t == typeof(FilesErrorCode);

        public override FilesErrorCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            switch (value)
            {
                case "ALLOWLIST_VIOLATION":
                    return FilesErrorCode.AllowlistViolation;
                case "CANCELLED":
                    return FilesErrorCode.Cancelled;
                case "CHANNEL_NOT_OPEN":
                    return FilesErrorCode.ChannelNotOpen;
                case "DESTINATION_GONE":
                    return FilesErrorCode.DestinationGone;
                case "DISK_FULL":
                    return FilesErrorCode.DiskFull;
                case "DISPOSED":
                    return FilesErrorCode.Disposed;
                case "INTERNAL_ERROR":
                    return FilesErrorCode.InternalError;
                case "INVALID_NAME":
                    return FilesErrorCode.InvalidName;
                case "IO_ERROR":
                    return FilesErrorCode.IoError;
                case "MALFORMED_RESPONSE":
                    return FilesErrorCode.MalformedResponse;
                case "NAME_CONFLICT":
                    return FilesErrorCode.NameConflict;
                case "NOT_FOUND":
                    return FilesErrorCode.NotFound;
                case "PERMISSION_DENIED":
                    return FilesErrorCode.PermissionDenied;
                case "PERMISSION_READ":
                    return FilesErrorCode.PermissionRead;
                case "PERMISSION_WRITE":
                    return FilesErrorCode.PermissionWrite;
                case "SOURCE_GONE":
                    return FilesErrorCode.SourceGone;
                case "STALLED":
                    return FilesErrorCode.Stalled;
                case "TIMEOUT":
                    return FilesErrorCode.Timeout;
                case "TRANSFER_NOT_FOUND":
                    return FilesErrorCode.TransferNotFound;
                case "UNKNOWN_COMMAND":
                    return FilesErrorCode.UnknownCommand;
            }
            throw new Exception("Cannot unmarshal type FilesErrorCode");
        }

        public override void Write(Utf8JsonWriter writer, FilesErrorCode value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case FilesErrorCode.AllowlistViolation:
                    JsonSerializer.Serialize(writer, "ALLOWLIST_VIOLATION", options);
                    return;
                case FilesErrorCode.Cancelled:
                    JsonSerializer.Serialize(writer, "CANCELLED", options);
                    return;
                case FilesErrorCode.ChannelNotOpen:
                    JsonSerializer.Serialize(writer, "CHANNEL_NOT_OPEN", options);
                    return;
                case FilesErrorCode.DestinationGone:
                    JsonSerializer.Serialize(writer, "DESTINATION_GONE", options);
                    return;
                case FilesErrorCode.DiskFull:
                    JsonSerializer.Serialize(writer, "DISK_FULL", options);
                    return;
                case FilesErrorCode.Disposed:
                    JsonSerializer.Serialize(writer, "DISPOSED", options);
                    return;
                case FilesErrorCode.InternalError:
                    JsonSerializer.Serialize(writer, "INTERNAL_ERROR", options);
                    return;
                case FilesErrorCode.InvalidName:
                    JsonSerializer.Serialize(writer, "INVALID_NAME", options);
                    return;
                case FilesErrorCode.IoError:
                    JsonSerializer.Serialize(writer, "IO_ERROR", options);
                    return;
                case FilesErrorCode.MalformedResponse:
                    JsonSerializer.Serialize(writer, "MALFORMED_RESPONSE", options);
                    return;
                case FilesErrorCode.NameConflict:
                    JsonSerializer.Serialize(writer, "NAME_CONFLICT", options);
                    return;
                case FilesErrorCode.NotFound:
                    JsonSerializer.Serialize(writer, "NOT_FOUND", options);
                    return;
                case FilesErrorCode.PermissionDenied:
                    JsonSerializer.Serialize(writer, "PERMISSION_DENIED", options);
                    return;
                case FilesErrorCode.PermissionRead:
                    JsonSerializer.Serialize(writer, "PERMISSION_READ", options);
                    return;
                case FilesErrorCode.PermissionWrite:
                    JsonSerializer.Serialize(writer, "PERMISSION_WRITE", options);
                    return;
                case FilesErrorCode.SourceGone:
                    JsonSerializer.Serialize(writer, "SOURCE_GONE", options);
                    return;
                case FilesErrorCode.Stalled:
                    JsonSerializer.Serialize(writer, "STALLED", options);
                    return;
                case FilesErrorCode.Timeout:
                    JsonSerializer.Serialize(writer, "TIMEOUT", options);
                    return;
                case FilesErrorCode.TransferNotFound:
                    JsonSerializer.Serialize(writer, "TRANSFER_NOT_FOUND", options);
                    return;
                case FilesErrorCode.UnknownCommand:
                    JsonSerializer.Serialize(writer, "UNKNOWN_COMMAND", options);
                    return;
            }
            throw new Exception("Cannot marshal type FilesErrorCode");
        }

        public static readonly FilesErrorCodeConverter Singleton = new FilesErrorCodeConverter();
    }

    internal class ErrorEnvelopeStatusConverter : JsonConverter<ErrorEnvelopeStatus>
    {
        public override bool CanConvert(Type t) => t == typeof(ErrorEnvelopeStatus);

        public override ErrorEnvelopeStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value == "error")
            {
                return ErrorEnvelopeStatus.Error;
            }
            throw new Exception("Cannot unmarshal type ErrorEnvelopeStatus");
        }

        public override void Write(Utf8JsonWriter writer, ErrorEnvelopeStatus value, JsonSerializerOptions options)
        {
            if (value == ErrorEnvelopeStatus.Error)
            {
                JsonSerializer.Serialize(writer, "error", options);
                return;
            }
            throw new Exception("Cannot marshal type ErrorEnvelopeStatus");
        }

        public static readonly ErrorEnvelopeStatusConverter Singleton = new ErrorEnvelopeStatusConverter();
    }

    internal class NameConflictModeConverter : JsonConverter<NameConflictMode>
    {
        public override bool CanConvert(Type t) => t == typeof(NameConflictMode);

        public override NameConflictMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            switch (value)
            {
                case "fail":
                    return NameConflictMode.Fail;
                case "keepBoth":
                    return NameConflictMode.KeepBoth;
                case "replace":
                    return NameConflictMode.Replace;
                case "skip":
                    return NameConflictMode.Skip;
            }
            throw new Exception("Cannot unmarshal type NameConflictMode");
        }

        public override void Write(Utf8JsonWriter writer, NameConflictMode value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case NameConflictMode.Fail:
                    JsonSerializer.Serialize(writer, "fail", options);
                    return;
                case NameConflictMode.KeepBoth:
                    JsonSerializer.Serialize(writer, "keepBoth", options);
                    return;
                case NameConflictMode.Replace:
                    JsonSerializer.Serialize(writer, "replace", options);
                    return;
                case NameConflictMode.Skip:
                    JsonSerializer.Serialize(writer, "skip", options);
                    return;
            }
            throw new Exception("Cannot marshal type NameConflictMode");
        }

        public static readonly NameConflictModeConverter Singleton = new NameConflictModeConverter();
    }

    internal class FilesEventEnvelopeStatusConverter : JsonConverter<FilesEventEnvelopeStatus>
    {
        public override bool CanConvert(Type t) => t == typeof(FilesEventEnvelopeStatus);

        public override FilesEventEnvelopeStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value == "event")
            {
                return FilesEventEnvelopeStatus.Event;
            }
            throw new Exception("Cannot unmarshal type FilesEventEnvelopeStatus");
        }

        public override void Write(Utf8JsonWriter writer, FilesEventEnvelopeStatus value, JsonSerializerOptions options)
        {
            if (value == FilesEventEnvelopeStatus.Event)
            {
                JsonSerializer.Serialize(writer, "event", options);
                return;
            }
            throw new Exception("Cannot marshal type FilesEventEnvelopeStatus");
        }

        public static readonly FilesEventEnvelopeStatusConverter Singleton = new FilesEventEnvelopeStatusConverter();
    }

    internal class ReasonConverter : JsonConverter<Reason>
    {
        public override bool CanConvert(Type t) => t == typeof(Reason);

        public override Reason Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            switch (value)
            {
                case "desktop_error":
                    return Reason.DesktopError;
                case "disconnect":
                    return Reason.Disconnect;
                case "stalled":
                    return Reason.Stalled;
                case "user":
                    return Reason.User;
            }
            throw new Exception("Cannot unmarshal type Reason");
        }

        public override void Write(Utf8JsonWriter writer, Reason value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case Reason.DesktopError:
                    JsonSerializer.Serialize(writer, "desktop_error", options);
                    return;
                case Reason.Disconnect:
                    JsonSerializer.Serialize(writer, "disconnect", options);
                    return;
                case Reason.Stalled:
                    JsonSerializer.Serialize(writer, "stalled", options);
                    return;
                case Reason.User:
                    JsonSerializer.Serialize(writer, "user", options);
                    return;
            }
            throw new Exception("Cannot marshal type Reason");
        }

        public static readonly ReasonConverter Singleton = new ReasonConverter();
    }

    internal class InvalidNameReasonConverter : JsonConverter<InvalidNameReason>
    {
        public override bool CanConvert(Type t) => t == typeof(InvalidNameReason);

        public override InvalidNameReason Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            switch (value)
            {
                case "DOT_ONLY":
                    return InvalidNameReason.DotOnly;
                case "EMPTY":
                    return InvalidNameReason.Empty;
                case "ILLEGAL_CHAR":
                    return InvalidNameReason.IllegalChar;
                case "RESERVED":
                    return InvalidNameReason.Reserved;
                case "TOO_LONG":
                    return InvalidNameReason.TooLong;
                case "TRAILING_SPACE_OR_DOT":
                    return InvalidNameReason.TrailingSpaceOrDot;
            }
            throw new Exception("Cannot unmarshal type InvalidNameReason");
        }

        public override void Write(Utf8JsonWriter writer, InvalidNameReason value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case InvalidNameReason.DotOnly:
                    JsonSerializer.Serialize(writer, "DOT_ONLY", options);
                    return;
                case InvalidNameReason.Empty:
                    JsonSerializer.Serialize(writer, "EMPTY", options);
                    return;
                case InvalidNameReason.IllegalChar:
                    JsonSerializer.Serialize(writer, "ILLEGAL_CHAR", options);
                    return;
                case InvalidNameReason.Reserved:
                    JsonSerializer.Serialize(writer, "RESERVED", options);
                    return;
                case InvalidNameReason.TooLong:
                    JsonSerializer.Serialize(writer, "TOO_LONG", options);
                    return;
                case InvalidNameReason.TrailingSpaceOrDot:
                    JsonSerializer.Serialize(writer, "TRAILING_SPACE_OR_DOT", options);
                    return;
            }
            throw new Exception("Cannot marshal type InvalidNameReason");
        }

        public static readonly InvalidNameReasonConverter Singleton = new InvalidNameReasonConverter();
    }

    internal class SuccessStatusConverter : JsonConverter<SuccessStatus>
    {
        public override bool CanConvert(Type t) => t == typeof(SuccessStatus);

        public override SuccessStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value == "success")
            {
                return SuccessStatus.Success;
            }
            throw new Exception("Cannot unmarshal type SuccessStatus");
        }

        public override void Write(Utf8JsonWriter writer, SuccessStatus value, JsonSerializerOptions options)
        {
            if (value == SuccessStatus.Success)
            {
                JsonSerializer.Serialize(writer, "success", options);
                return;
            }
            throw new Exception("Cannot marshal type SuccessStatus");
        }

        public static readonly SuccessStatusConverter Singleton = new SuccessStatusConverter();
    }
    
    public class DateOnlyConverter : JsonConverter<DateOnly>
    {
        private readonly string serializationFormat;
        public DateOnlyConverter() : this(null) { }

        public DateOnlyConverter(string? serializationFormat)
        {
                this.serializationFormat = serializationFormat ?? "yyyy-MM-dd";
        }

        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
                var value = reader.GetString();
                return DateOnly.Parse(value!);
        }

        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.ToString(serializationFormat));
    }

    public class TimeOnlyConverter : JsonConverter<TimeOnly>
    {
        private readonly string serializationFormat;

        public TimeOnlyConverter() : this(null) { }

        public TimeOnlyConverter(string? serializationFormat)
        {
                this.serializationFormat = serializationFormat ?? "HH:mm:ss.fff";
        }

        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
                var value = reader.GetString();
                return TimeOnly.Parse(value!);
        }

        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.ToString(serializationFormat));
    }

    internal class IsoDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override bool CanConvert(Type t) => t == typeof(DateTimeOffset);

        private const string DefaultDateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK";

        private DateTimeStyles _dateTimeStyles = DateTimeStyles.RoundtripKind;
        private string? _dateTimeFormat;
        private CultureInfo? _culture;

        public DateTimeStyles DateTimeStyles
        {
                get => _dateTimeStyles;
                set => _dateTimeStyles = value;
        }

        public string? DateTimeFormat
        {
                get => _dateTimeFormat ?? string.Empty;
                set => _dateTimeFormat = (string.IsNullOrEmpty(value)) ? null : value;
        }

        public CultureInfo Culture
        {
                get => _culture ?? CultureInfo.CurrentCulture;
                set => _culture = value;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
                string text;


                if ((_dateTimeStyles & DateTimeStyles.AdjustToUniversal) == DateTimeStyles.AdjustToUniversal
                        || (_dateTimeStyles & DateTimeStyles.AssumeUniversal) == DateTimeStyles.AssumeUniversal)
                {
                        value = value.ToUniversalTime();
                }

                text = value.ToString(_dateTimeFormat ?? DefaultDateTimeFormat, Culture);

                writer.WriteStringValue(text);
        }

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
                string? dateText = reader.GetString();

                if (string.IsNullOrEmpty(dateText) == false)
                {
                        if (!string.IsNullOrEmpty(_dateTimeFormat))
                        {
                                return DateTimeOffset.ParseExact(dateText, _dateTimeFormat, Culture, _dateTimeStyles);
                        }
                        else
                        {
                                return DateTimeOffset.Parse(dateText, Culture, _dateTimeStyles);
                        }
                }
                else
                {
                        return default(DateTimeOffset);
                }
        }


        public static readonly IsoDateTimeOffsetConverter Singleton = new IsoDateTimeOffsetConverter();
    }
}
