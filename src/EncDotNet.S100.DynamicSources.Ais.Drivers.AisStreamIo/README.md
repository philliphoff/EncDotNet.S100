# EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo

`IAisMessageSource` driver targeting [aisstream.io](https://aisstream.io)'s
free WebSocket AIS streaming service.

## What ships here

| Type | Purpose |
| --- | --- |
| `AisStreamIoOptions` | API key, endpoint URI, subscribe deadline, reconnect backoff settings. |
| `IAisStreamIoTransport` | Test seam over `ClientWebSocket`; exchanges JSON text frames. |
| `ClientWebSocketTransport` | Production transport — wraps `System.Net.WebSockets.ClientWebSocket`, reassembles fragmented frames. |
| `AisStreamIoMessageSource` | `IAisMessageSource` backed by aisstream.io. |
| (internal) `AisStreamIoSubscription` | Per-call subscription with automatic reconnect (truncated exponential backoff, 250 ms → 30 s) and swap-and-replace `TryUpdateArea`. |
| (internal) `AisStreamIoJson` | Hand-rolled JSON contract for `PositionReport` and `ShipStaticData`; sentinel collapse (511 / 360 / 102.3 / -128); API-key redaction for logs. |

## Dependencies

BCL-only — `System.Net.WebSockets.ClientWebSocket` plus
`System.Text.Json`. No third-party AIS / WebSocket libraries.

## Notes

- aisstream.io is a BETA service with no published SLA. Treat as
  best-effort; the driver reconnects transparently on disconnect.
- The service requires the subscribe frame within 3 s of connect.
- Updating the spatial filter is by **resending** the subscribe
  frame on the same socket — `TryUpdateArea` does this and always
  returns `true`.
- API keys are sensitive. The driver never logs them — every
  outgoing frame goes through `AisStreamIoJson.RedactApiKey` before
  emission, and a regression test pins this behaviour.

## See also

- `docs/design/ais-source.md` §12.
- `EncDotNet.S100.DynamicSources.Ais` for the abstraction layer.
