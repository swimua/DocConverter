# Word-to-PDF Service — API Reference

## Authentication

Send either:

- `X-API-Key: <key>` header, **or**
- `Authorization: Bearer <jwt>` (when JWT signing key is configured)

## Endpoints

### `GET /health`
Unauthenticated. Returns HTTP 200 with `"Healthy"` when the service is up.

### `POST /api/v1/convert`
Converts an uploaded document to PDF.

Request:
- `Content-Type: multipart/form-data`
- Form field `file` — the `.docx` (also supports `.doc`, `.rtf`, `.odt`, `.txt`)
- Max upload size — controlled by `Converter:MaxUploadMb` (default **50 MB**)

Success response: `200 OK`, `Content-Type: application/pdf`, body = the PDF.

Error responses:
| Code | Meaning |
|------|---------|
| 400  | Missing file / wrong content type |
| 401  | Missing/invalid API key or JWT |
| 413  | Upload larger than the configured limit |
| 422  | LibreOffice couldn't convert the file (corrupt doc, timeout, unsupported) |
| 5xx  | Unexpected server error |

Example (curl):
```bash
curl -X POST https://word-to-pdf.example.com/api/v1/convert \
  -H "X-API-Key: $API_KEY" \
  -F "file=@quote.docx" \
  --output quote.pdf
```
