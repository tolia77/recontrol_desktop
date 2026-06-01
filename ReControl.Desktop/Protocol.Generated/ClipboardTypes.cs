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
    /// clipboard wire protocol: set / refused / capabilities envelopes for v1.3 mutual clipboard.
    /// </summary>
    public partial class ClipboardTypes
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("capabilitiesEnvelope")]
        public ClipboardCapabilitiesEnvelope CapabilitiesEnvelope { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("chunkEnvelope")]
        public ClipboardChunkEnvelope ChunkEnvelope { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("refusalReason")]
        public ClipboardRefusalReason? RefusalReason { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("refusedEnvelope")]
        public ClipboardRefusedEnvelope RefusedEnvelope { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("setEnvelope")]
        public ClipboardSetEnvelope SetEnvelope { get; set; }
    }

    /// <summary>
    /// Sent on every channel-open and on every settings change. Receiver caches latest as policy.
    /// </summary>
    public partial class ClipboardCapabilitiesEnvelope
    {
        [JsonPropertyName("inboundEnabled")]
        public bool InboundEnabled { get; set; }

        [JsonPropertyName("kind")]
        public CapabilitiesEnvelopeKind Kind { get; set; }

        /// <summary>
        /// Server-advertised cap; v1.3 = 2000000.
        /// </summary>
        [JsonPropertyName("maxBytes")]
        public long MaxBytes { get; set; }

        [JsonPropertyName("originId")]
        public string OriginId { get; set; }

        [JsonPropertyName("outboundEnabled")]
        public bool OutboundEnabled { get; set; }

        /// <summary>
        /// Semantic protocol marker, e.g. "1.0".
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonPropertyName("seq")]
        public long Seq { get; set; }

        [JsonPropertyName("ts")]
        public long Ts { get; set; }
    }

    /// <summary>
    /// Chunk metadata envelope for chunked-fallback transport mode.
    /// </summary>
    public partial class ClipboardChunkEnvelope
    {
        [JsonPropertyName("chunkOf")]
        public long ChunkOf { get; set; }

        [JsonPropertyName("chunkSeq")]
        public long ChunkSeq { get; set; }

        [JsonPropertyName("kind")]
        public ChunkEnvelopeKind Kind { get; set; }

        [JsonPropertyName("originId")]
        public string OriginId { get; set; }

        /// <summary>
        /// Hex-encoded binary chunk payload for JSON-channel spike compatibility.
        /// </summary>
        [JsonPropertyName("payloadHex")]
        public string PayloadHex { get; set; }

        [JsonPropertyName("seq")]
        public long Seq { get; set; }

        [JsonPropertyName("transferId")]
        public string TransferId { get; set; }

        [JsonPropertyName("ts")]
        public long Ts { get; set; }
    }

    /// <summary>
    /// Refusal in response to a `set` we cannot apply.
    /// </summary>
    public partial class ClipboardRefusedEnvelope
    {
        [JsonPropertyName("kind")]
        public RefusedEnvelopeKind Kind { get; set; }

        /// <summary>
        /// Echo of the offending envelope's originId for correlation.
        /// </summary>
        [JsonPropertyName("originId")]
        public string OriginId { get; set; }

        [JsonPropertyName("reason")]
        public ClipboardRefusalReason Reason { get; set; }

        [JsonPropertyName("seq")]
        public long Seq { get; set; }

        [JsonPropertyName("ts")]
        public long Ts { get; set; }
    }

    /// <summary>
    /// Apply this text to the receiver's clipboard (subject to receiver-side loop and policy
    /// gates).
    /// </summary>
    public partial class ClipboardSetEnvelope
    {
        /// <summary>
        /// Total chunk count (chunked transfers only).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("chunkOf")]
        public long? ChunkOf { get; set; }

        /// <summary>
        /// 0-indexed chunk sequence (chunked transfers only).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("chunkSeq")]
        public long? ChunkSeq { get; set; }

        /// <summary>
        /// UTF-8 text payload. <= 2_000_000 UTF-8 bytes pre-hash (TRANSPORT-02).
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; }

        /// <summary>
        /// SHA-256 of UTF-8 content bytes, first 8 bytes, lowercase hex (LOOP-02).
        /// </summary>
        [JsonPropertyName("contentHash")]
        public string ContentHash { get; set; }

        [JsonPropertyName("kind")]
        public SetEnvelopeKind Kind { get; set; }

        /// <summary>
        /// Sender per-channel-open UUID v4. Receiver MUST drop on self-match.
        /// </summary>
        [JsonPropertyName("originId")]
        public string OriginId { get; set; }

        /// <summary>
        /// Monotonic per-origin counter. Diagnostic only (D-04).
        /// </summary>
        [JsonPropertyName("seq")]
        public long Seq { get; set; }

        /// <summary>
        /// Sender wall-clock epoch ms. Diagnostic only (D-04).
        /// </summary>
        [JsonPropertyName("ts")]
        public long Ts { get; set; }
    }

    public enum CapabilitiesEnvelopeKind { Capabilities };

    public enum ChunkEnvelopeKind { Chunk };

    /// <summary>
    /// Categorized refusal reason. Stable; new entries added rather than repurposed. Locked by
    /// CONTEXT D-03.
    /// </summary>
    public enum ClipboardRefusalReason { CapsUnknown, InboundDisabled, MasterDisabled, NonText, Paused, PermissionDenied, TooLarge };

    public enum RefusedEnvelopeKind { Refused };

    public enum SetEnvelopeKind { Set };

    internal static class ClipboardConverter
    {
        public static readonly JsonSerializerOptions Settings = new(JsonSerializerDefaults.General)
        {
            Converters =
            {
                CapabilitiesEnvelopeKindConverter.Singleton,
                ChunkEnvelopeKindConverter.Singleton,
                ClipboardRefusalReasonConverter.Singleton,
                RefusedEnvelopeKindConverter.Singleton,
                SetEnvelopeKindConverter.Singleton,
                new ClipboardDateOnlyConverter(),
                new ClipboardTimeOnlyConverter(),
                ClipboardIsoDateTimeOffsetConverter.Singleton
            },
        };
    }

    internal class CapabilitiesEnvelopeKindConverter : JsonConverter<CapabilitiesEnvelopeKind>
    {
        public override bool CanConvert(Type t) => t == typeof(CapabilitiesEnvelopeKind);

        public override CapabilitiesEnvelopeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value == "capabilities")
            {
                return CapabilitiesEnvelopeKind.Capabilities;
            }
            throw new Exception("Cannot unmarshal type CapabilitiesEnvelopeKind");
        }

        public override void Write(Utf8JsonWriter writer, CapabilitiesEnvelopeKind value, JsonSerializerOptions options)
        {
            if (value == CapabilitiesEnvelopeKind.Capabilities)
            {
                JsonSerializer.Serialize(writer, "capabilities", options);
                return;
            }
            throw new Exception("Cannot marshal type CapabilitiesEnvelopeKind");
        }

        public static readonly CapabilitiesEnvelopeKindConverter Singleton = new CapabilitiesEnvelopeKindConverter();
    }

    internal class ChunkEnvelopeKindConverter : JsonConverter<ChunkEnvelopeKind>
    {
        public override bool CanConvert(Type t) => t == typeof(ChunkEnvelopeKind);

        public override ChunkEnvelopeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value == "chunk")
            {
                return ChunkEnvelopeKind.Chunk;
            }
            throw new Exception("Cannot unmarshal type ChunkEnvelopeKind");
        }

        public override void Write(Utf8JsonWriter writer, ChunkEnvelopeKind value, JsonSerializerOptions options)
        {
            if (value == ChunkEnvelopeKind.Chunk)
            {
                JsonSerializer.Serialize(writer, "chunk", options);
                return;
            }
            throw new Exception("Cannot marshal type ChunkEnvelopeKind");
        }

        public static readonly ChunkEnvelopeKindConverter Singleton = new ChunkEnvelopeKindConverter();
    }

    internal class ClipboardRefusalReasonConverter : JsonConverter<ClipboardRefusalReason>
    {
        public override bool CanConvert(Type t) => t == typeof(ClipboardRefusalReason);

        public override ClipboardRefusalReason Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            switch (value)
            {
                case "CAPS_UNKNOWN":
                    return ClipboardRefusalReason.CapsUnknown;
                case "INBOUND_DISABLED":
                    return ClipboardRefusalReason.InboundDisabled;
                case "MASTER_DISABLED":
                    return ClipboardRefusalReason.MasterDisabled;
                case "NON_TEXT":
                    return ClipboardRefusalReason.NonText;
                case "PAUSED":
                    return ClipboardRefusalReason.Paused;
                case "PERMISSION_DENIED":
                    return ClipboardRefusalReason.PermissionDenied;
                case "TOO_LARGE":
                    return ClipboardRefusalReason.TooLarge;
            }
            throw new Exception("Cannot unmarshal type ClipboardRefusalReason");
        }

        public override void Write(Utf8JsonWriter writer, ClipboardRefusalReason value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case ClipboardRefusalReason.CapsUnknown:
                    JsonSerializer.Serialize(writer, "CAPS_UNKNOWN", options);
                    return;
                case ClipboardRefusalReason.InboundDisabled:
                    JsonSerializer.Serialize(writer, "INBOUND_DISABLED", options);
                    return;
                case ClipboardRefusalReason.MasterDisabled:
                    JsonSerializer.Serialize(writer, "MASTER_DISABLED", options);
                    return;
                case ClipboardRefusalReason.NonText:
                    JsonSerializer.Serialize(writer, "NON_TEXT", options);
                    return;
                case ClipboardRefusalReason.Paused:
                    JsonSerializer.Serialize(writer, "PAUSED", options);
                    return;
                case ClipboardRefusalReason.PermissionDenied:
                    JsonSerializer.Serialize(writer, "PERMISSION_DENIED", options);
                    return;
                case ClipboardRefusalReason.TooLarge:
                    JsonSerializer.Serialize(writer, "TOO_LARGE", options);
                    return;
            }
            throw new Exception("Cannot marshal type ClipboardRefusalReason");
        }

        public static readonly ClipboardRefusalReasonConverter Singleton = new ClipboardRefusalReasonConverter();
    }

    internal class RefusedEnvelopeKindConverter : JsonConverter<RefusedEnvelopeKind>
    {
        public override bool CanConvert(Type t) => t == typeof(RefusedEnvelopeKind);

        public override RefusedEnvelopeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value == "refused")
            {
                return RefusedEnvelopeKind.Refused;
            }
            throw new Exception("Cannot unmarshal type RefusedEnvelopeKind");
        }

        public override void Write(Utf8JsonWriter writer, RefusedEnvelopeKind value, JsonSerializerOptions options)
        {
            if (value == RefusedEnvelopeKind.Refused)
            {
                JsonSerializer.Serialize(writer, "refused", options);
                return;
            }
            throw new Exception("Cannot marshal type RefusedEnvelopeKind");
        }

        public static readonly RefusedEnvelopeKindConverter Singleton = new RefusedEnvelopeKindConverter();
    }

    internal class SetEnvelopeKindConverter : JsonConverter<SetEnvelopeKind>
    {
        public override bool CanConvert(Type t) => t == typeof(SetEnvelopeKind);

        public override SetEnvelopeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value == "set")
            {
                return SetEnvelopeKind.Set;
            }
            throw new Exception("Cannot unmarshal type SetEnvelopeKind");
        }

        public override void Write(Utf8JsonWriter writer, SetEnvelopeKind value, JsonSerializerOptions options)
        {
            if (value == SetEnvelopeKind.Set)
            {
                JsonSerializer.Serialize(writer, "set", options);
                return;
            }
            throw new Exception("Cannot marshal type SetEnvelopeKind");
        }

        public static readonly SetEnvelopeKindConverter Singleton = new SetEnvelopeKindConverter();
    }
    
    public class ClipboardDateOnlyConverter : JsonConverter<DateOnly>
    {
        private readonly string serializationFormat;
        public ClipboardDateOnlyConverter() : this(null) { }

        public ClipboardDateOnlyConverter(string? serializationFormat)
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

    public class ClipboardTimeOnlyConverter : JsonConverter<TimeOnly>
    {
        private readonly string serializationFormat;

        public ClipboardTimeOnlyConverter() : this(null) { }

        public ClipboardTimeOnlyConverter(string? serializationFormat)
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

    internal class ClipboardIsoDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
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


        public static readonly ClipboardIsoDateTimeOffsetConverter Singleton = new ClipboardIsoDateTimeOffsetConverter();
    }
}
