# ReControl Desktop -- SIPSorcery Empirical Spikes

These four console-app spikes answer specific SIPSorcery 10.0.3 behavioral
questions that block confident Phase 11 transfer-pipeline design. They are
**standalone repros**, not part of the main `ReControl.Desktop.slnx`
solution -- so the primary app's build never depends on them.

Each spike prints its observed behavior to stdout. Observations are recorded
in:

    .planning/phases/09-backend-foundation/09-SPIKE-FINDINGS.md

**When to re-run:** whenever SIPSorcery is bumped to a new minor version,
or more than six months have passed since the last recorded run (pick
earlier). The findings doc is only trustworthy for the SIPSorcery version
noted in its metadata.

## Spike summary

| Name                   | Question (GitHub issue)                                       | Browser? |
| ---------------------- | ------------------------------------------------------------- | -------- |
| SpikeReliabilityInit   | Does SIPSorcery honor RTCDataChannelInit options? (#701)      | No       |
| SpikeBufferedAmount    | Does bufferedAmount rise/drain on heavy send? (#383)          | Yes      |
| SpikeDcClose           | Does dc.close() reach 'closed' on both peers? (#882)          | Yes      |
| SpikeMaxMessageSize    | Does the SDP answer honor a=max-message-size? (RFC 8841)      | No       |

## Spike A -- SpikeReliabilityInit

Creates four data channels with non-default init options (ordered=false,
maxRetransmits=5, negotiated=true/id=42, maxPacketLifeTime=1500) and
prints the properties reported back plus the generated offer SDP.

Run:

    dotnet run --project recontrol_desktop/Spikes/SpikeReliabilityInit

Look for: each "Channel N reported" line must match the "Requested" line
above it. The offer SDP must contain an `a=max-message-size` attribute
in the SCTP m-section.

## Spike B -- SpikeBufferedAmount

Accepts a WebRTC offer from a browser page, opens the inbound data channel,
sends 10 MB in 16-KiB chunks, and polls `dc.bufferedAmount` every 50 ms
for 30 seconds. Prints the full bufferedAmount timeline.

Run:

    # Terminal 1
    dotnet run --project recontrol_desktop/Spikes/SpikeBufferedAmount

    # Terminal 2 (from the same directory)
    python3 -m http.server 8080 --bind 127.0.0.1

    # Browser (Chromium-based, Firefox works too)
    http://127.0.0.1:8080/spike-b-frontend.html

Look for: a non-zero peak bufferedAmount during the send and a drain to 0
within a second or two after the send loop completes. If peak is 0 the
whole run, SIPSorcery is not tracking buffered bytes and Phase 11 must use
app-level windowing instead of bufferedAmount-based backpressure.

## Spike C -- SpikeDcClose

Accepts a WebRTC offer, lets the frontend call `dc.close()` two seconds
after the channel opens, and polls the desktop's `dc.readyState` every
500 ms for 15 seconds. Logs whether `onclose` fires.

Run:

    # Terminal 1
    dotnet run --project recontrol_desktop/Spikes/SpikeDcClose

    # Terminal 2
    python3 -m http.server 8080 --bind 127.0.0.1

    # Browser
    http://127.0.0.1:8080/spike-c-frontend.html

Look for: `readyState` transitioning open -> closing -> closed and an
`onclose` event on the desktop side. If the state stays `open` for the
full 15-second window, frontend-initiated `dc.close()` does not propagate
and Phase 9 / Phase 11 must tear channels down by closing the
RTCPeerConnection instead.

## Spike D -- SpikeMaxMessageSize

Feeds a hard-coded offer SDP that advertises `a=max-message-size:262144`
to a desktop RTCPeerConnection and prints the answer SDP. No browser is
involved; the spike is offline SDP manipulation only.

Run:

    dotnet run --project recontrol_desktop/Spikes/SpikeMaxMessageSize

Look for: the answer's `a=max-message-size` line. If present and equal to
262144, SIPSorcery honors the offered value. If absent, RFC 8841's implied
default of 64 KiB applies. Either outcome constrains the `files-data`
chunk-header design (a payload cap at or below the announced size).

## Headless automation (CI / sandbox)

Spikes B and C normally need a visible browser, but both also run
headlessly. The scripts below produce the same observations used when
writing `09-SPIKE-FINDINGS.md`.

    cd recontrol_desktop/Spikes/SpikeBufferedAmount
    dotnet run --no-build &
    python3 -m http.server 8080 --bind 127.0.0.1 &
    google-chrome --headless=new --no-sandbox --disable-gpu \
        --disable-features=WebRtcHideLocalIpsWithMdns \
        --user-data-dir=/tmp/chrome-profile \
        http://127.0.0.1:8080/spike-b-frontend.html

(Let Chrome run ~35 s for Spike B, ~18 s for Spike C, then kill the
background jobs.)
