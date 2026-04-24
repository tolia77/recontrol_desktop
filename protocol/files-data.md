# files-data binary framing

`files-data` is the WebRTC data channel that carries raw file bytes between the desktop client and the browser. JSON control (start / ack / cancel) rides on the sibling `files-ctl` channel; `files-data` is binary-only.

This document is the human-readable specification of the 16-byte chunk header and of the wire contract that surrounds it. The header is implemented on both sides by a small amount of hand-written code (`ChunkHeader.cs` and `ChunkHeader.ts`) rather than codegen: the binary shape is trivial, JSON Schema cannot describe binary offsets, and both sides must stay byte-identical regardless of generator output.

Phase 9 scope: this spec is authored in Phase 9 so plans 09-04 and beyond have a fixed contract to target. The actual data-transfer pipeline (begin / end / cancel state machine, disk streaming, backpressure) is implemented in Phase 11.

## Chunk header

Every `files-data` message is exactly one chunk. A chunk is a 16-byte header followed by 0 to 16384 bytes of payload. All header integers are **little-endian**.

```
offset | size | type | field       | notes
-------+------+------+-------------+------------------------------------------------
  0    |  4   | u32  | transferId  | Identifier returned by the handshake command
  4    |  4   | u32  | seq         | Per-transfer chunk sequence, starts at 0
  8    |  8   | u64  | offset      | Byte offset of this chunk's payload inside
       |      |      |             | the logical file (matches the write offset
       |      |      |             | on the receiver, so out-of-order delivery
       |      |      |             | can be reassembled even though SCTP
       |      |      |             | reliable+ordered already orders by default)
-------+------+------+-------------+------------------------------------------------
 total  16   bytes
```

No flag bits are reserved in the header. A "final chunk" flag was explicitly considered and rejected -- see "End-of-transfer" below.

### Endianness

All multi-byte integers in the header are **little-endian**. Rationale:

- Browser `DataView.getUint32(offset, true)` / `setUint32(offset, value, true)` defaults are explicitly little-endian when the third argument is `true`, and that is the only form used by `ChunkHeader.ts`.
- .NET `System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian` / `WriteUInt32LittleEndian` (and the 64-bit equivalents) are architecture-neutral wrappers around the little-endian shape, so the C# side produces the same bytes on x86, ARM and big-endian targets.
- Both x86 and ARM64 desktop CPUs (the client-side targets) use little-endian natively, so there is no byte-swap cost on the hot path.

## Payload cap

Maximum payload per chunk: **16384 bytes (16 KiB)**.

Maximum total wire frame: 16 bytes header + 16384 bytes payload = **16400 bytes**.

This value is a target, not a guarantee. The effective cap is `min(16400, announced max-message-size)` where `announced max-message-size` comes from the SCTP `a=max-message-size:` SDP attribute of the answerer (the desktop client).

> **PENDING_SPIKE_D.** Plan 09-04 must check `.planning/phases/09-backend-foundation/09-SPIKE-FINDINGS.md` (Spike D -- `max-message-size`) once it lands. If SIPSorcery announces a value lower than 16400 bytes, the payload cap in this document and in the chunk writer must be revised downward to keep chunks inside the announced limit. Until Spike D results exist, implementers must treat 16 KiB as the target and 16400 bytes as the target frame size; see also 09-CONTEXT.md decision "16 KiB chunk size (not 64 KiB) for cross-browser SCTP safety".

The 16 KiB choice (rather than 64 KiB) reflects empirical cross-browser SCTP safety: Chrome and Firefox buffer data channel writes in 16 KiB increments and some intermediaries (including older SIPSorcery revisions) can misbehave with larger single SCTP messages. A smaller chunk adds negligible per-chunk overhead on a reliable+ordered channel.

## Start-of-transfer

The `files-data` channel carries no self-describing "begin" chunk. A transfer begins with a handshake on `files-ctl`:

- Upload (browser -> desktop): `files.upload.begin` request -> response containing `transferId` and `expectedChunkCount`.
- Download (desktop -> browser): `files.download.begin` request -> response containing `transferId` and `expectedChunkCount`.

These two control commands are defined and wired up in Phase 11. Phase 9 only reserves the approach and does not add them to `files-ctl.schema.json` yet (they are not needed until the pipeline is real).

Once a `transferId` is agreed, the sender emits chunks with that id starting at `seq = 0`, `offset = 0`. Both sides validate that incoming `transferId` matches an active transfer they know about.

## End-of-transfer

End-of-transfer is signaled **on `files-ctl`**, not via a flag bit in the header. Rationale:

- `files-ctl` already carries the per-transfer state machine (begin, progress, cancel, error), so keeping the terminal state on the same channel keeps the state machine in one place.
- Flagging a final chunk in the binary header would require reserving bits in an otherwise dense 16-byte header, and those bits would then be wasted on every non-final chunk forever.
- Reliable+ordered delivery guarantees the receiver sees every chunk; the receiver does not need an in-band "this is the last chunk" marker to stop early.

Concretely, Phase 11 will add a `files.transfer.complete` (or equivalent) ack command on `files-ctl` whose `transferId` matches the chunks just sent, and whose response releases any receiver-side resources.

## Integrity

`files-data` relies on **SCTP reliable + ordered delivery** for integrity. No per-chunk CRC is included; no per-transfer SHA / checksum is required.

Rationale:

- SCTP is reliable (retransmits lost messages) and ordered (delivers in sequence), so the receiver sees every byte exactly once in the same order the sender wrote them.
- DTLS (the transport security layer beneath SCTP on WebRTC data channels) provides message authentication codes, so opportunistic corruption is already caught at that layer.
- A CRC inside each chunk would add cost to every write on the hot path with no observable benefit, given DTLS+SCTP below it.

This is revisited in Phase 11 if the spike work or practical testing shows the assumption is wrong.

## Cross-language parity

The C# struct `ChunkHeader` (at `recontrol_desktop/ReControl.Desktop/Services/Files/FilesProtocol/ChunkHeader.cs`) and the TypeScript class `ChunkHeader` (at `recontrol_frontend/src/pages/DeviceControl/services/files/ChunkHeader.ts`) must produce and consume byte-identical buffers. Parity is enforced by a **shared test vector** committed alongside the C# test:

```
hex:   78 56 34 12  DD CC BB AA  EF CD AB 89 67 45 23 01
values: transferId = 0x12345678
        seq        = 0xAABBCCDD
        offset     = 0x0123456789ABCDEF
```

The xUnit test in `recontrol_desktop/ReControl.Desktop.Tests/Files/ChunkHeaderTests.cs` verifies both reading and writing of this exact byte sequence. A future frontend unit test (once the frontend adopts a test framework) will verify the same vector decodes identically in `ChunkHeader.ts`; until then, the hex vector itself is the contract, and a developer can paste the 16 bytes into a browser console and call `ChunkHeader.read` to spot-check.

## References

- JSON control envelope: `recontrol_desktop/protocol/files-ctl.schema.json`
- Chunk size rationale: `.planning/PROJECT.md` key-decisions table, "16 KiB chunk size (not 64 KiB)"
- Spike results feeding max-message-size: `.planning/phases/09-backend-foundation/09-SPIKE-FINDINGS.md` (Spike D; pending)
- Broader phase design: `.planning/phases/09-backend-foundation/09-CONTEXT.md`, `.planning/phases/09-backend-foundation/09-RESEARCH.md`
