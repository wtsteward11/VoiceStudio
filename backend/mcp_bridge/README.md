# MCP Bridge Layer

**Status: Experimental / Proof-of-Concept** (Arch Review Task 1.6)

This layer is a **proof-of-concept** only. It does not represent a production MCP integration.

## Current Implementation

- **PDF Unlocker Client** (`pdf_unlocker_client.py`) - Read and extract text from PDFs via MCP-style client
- No MCP dashboard, no full MCP server orchestration, no design tokens or AI engine integration

## Planned Capabilities (Future)

- Figma (design tokens)
- Magic UI, Flux UI, Shadcn
- TTS/VC models, Whisper
- Full MCP server orchestration

## PDF Unlocker Integration (POC)

The PDF Unlocker client enables:
- Text extraction from PDFs for voice synthesis
- Protected PDF support
- Page selection and metadata extraction

**Usage**: See `backend/api/routes/pdf.py` for PDF endpoints. The client is at `pdf_unlocker_client.py`.

## References

- ADR-045: MCP Integration Strategy
- FUTURE_WORK.md: MCP integration roadmap
