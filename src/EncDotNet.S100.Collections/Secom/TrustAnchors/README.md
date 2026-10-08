# Built-in SECOM trust anchors

`SecomTrustAnchors.BuiltIn` trusts the certificates in this folder. They are
embedded in the Collections assembly.

| File | Name shown | What it holds |
|---|---|---|
| `mcp-mcc.pem` | MCP MCC | The Maritime Connectivity Platform (MCP) CA chain of the MCC instance: "MCP Root Certificate" and its intermediate "MCP Identity Registry" (`urn:mrn:mcp:ca:mcc:mcp-idreg-new`). This is the instance SECOM signers such as CCG and DMA hold certificates from. |

## Source

`mcp-mcc.pem` is a byte-for-byte copy of the file the MCP documentation links
to under "Using MCP testbed Certificates" and "MCC Testbed Truststore":

- URL: <https://raw.githubusercontent.com/maritimeconnectivity/docs.maritimeconnectivity.net/refs/heads/files/mcp-ca-chain.pem>
- Repository: [maritimeconnectivity/docs.maritimeconnectivity.net](https://github.com/maritimeconnectivity/docs.maritimeconnectivity.net), MIT licence. These are public CA certificates, meant to be added to trust stores.
- Fetched: 2026-10-07, branch `files` at `913322eb8116bcb4b48e7b13d4460b5dcca2ad06`.
- SHA-256 of the file: `abee59593250606712ef89b7b468be253ad78607f679868261d96c057a1d66ea`.

Certificate SHA-256 fingerprints. `SecomTrustAnchorsTests` checks these values:

| Certificate | SHA-256 | Valid until |
|---|---|---|
| MCP Root Certificate | `ec1938782d8c8c228bc214d19fbf1e65e2db689675d4e4f27e2f6fbedcefd8db` | 2030-11-15 |
| MCP Identity Registry (`mcp-idreg-new`) | `45c34d53a13cff3338f6472502965c59a4ae16bd436daef8790357a53f628ac4` | 2030-09-15 |

To refresh, download the file again and check its certificates with
`openssl crl2pkcs7 -nocrl -certfile mcp-mcc.pem | openssl pkcs7 -print_certs`.
Then update the tables above and the test. The MCC test instance (`mcc-test`)
is deliberately not trusted: anyone can get a certificate from it.
