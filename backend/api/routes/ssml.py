"""
SSML Control Routes

Endpoints for SSML (Speech Synthesis Markup Language) editing and processing.
"""

from __future__ import annotations

import logging
import asyncio
import sys
from datetime import datetime
from pathlib import Path

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..optimization import cache_response

# Add app to path for NLP imports
project_root = Path(__file__).parent.parent.parent.parent
sys.path.insert(0, str(project_root / "app"))

# Try importing NLP text processor
try:
    from app.core.nlp.text_processing import get_text_preprocessor

    HAS_NLP = True
except ImportError:
    HAS_NLP = False

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/ssml", tags=["ssml"])

# In-memory SSML documents storage (replace with database in production)
from backend.api.routes._persistent_store import PersistentStore

_ssml_documents: PersistentStore = PersistentStore("ssml_documents")
_state_lock = asyncio.Lock()


class SSMLDocument(BaseModel):
    """An SSML document."""

    id: str
    name: str
    content: str  # SSML markup
    profile_id: str | None = None
    project_id: str | None = None
    created: str  # ISO datetime string
    modified: str  # ISO datetime string


class SSMLCreateRequest(BaseModel):
    """Request to create an SSML document."""

    name: str
    content: str
    profile_id: str | None = None
    project_id: str | None = None


class SSMLUpdateRequest(BaseModel):
    """Request to update an SSML document."""

    name: str | None = None
    content: str | None = None
    profile_id: str | None = None


class SSMLValidateResponse(BaseModel):
    """Response from SSML validation."""

    valid: bool
    errors: list[str] = []
    warnings: list[str] = []


class SSMLPreviewRequest(BaseModel):
    """Request for SSML preview."""

    content: str
    profile_id: str | None = None
    engine: str | None = None


class SSMLPreviewResponse(BaseModel):
    """Response from SSML preview."""

    audio_id: str
    duration: float
    message: str


@router.get("", response_model=list[SSMLDocument])
@cache_response(ttl=60)  # Cache 60s (document list may change frequently)
async def get_ssml_documents(
    project_id: str | None = None,
    profile_id: str | None = None,
):
    """Get all SSML documents, optionally filtered."""
    documents = list(_ssml_documents.values())

    if project_id:
        documents = [d for d in documents if d.get("project_id") == project_id]

    if profile_id:
        documents = [d for d in documents if d.get("profile_id") == profile_id]

    # Sort by modified date (newest first)
    documents.sort(key=lambda d: d.get("modified", ""), reverse=True)

    return [
        SSMLDocument(
            id=str(d.get("id", "")),
            name=str(d.get("name", "")),
            content=str(d.get("content", "")),
            profile_id=d.get("profile_id"),
            project_id=d.get("project_id"),
            created=str(d.get("created", "")),
            modified=str(d.get("modified", "")),
        )
        for d in documents
    ]


@router.get("/{document_id}", response_model=SSMLDocument)
@cache_response(ttl=300)  # Cache 5min (document content is relatively static)
async def get_ssml_document(document_id: str):
    """Get a specific SSML document."""
    if document_id not in _ssml_documents:
        raise HTTPException(status_code=404, detail="SSML document not found")

    doc = _ssml_documents[document_id]
    return SSMLDocument(
        id=str(doc.get("id", "")),
        name=str(doc.get("name", "")),
        content=str(doc.get("content", "")),
        profile_id=doc.get("profile_id"),
        project_id=doc.get("project_id"),
        created=str(doc.get("created", "")),
        modified=str(doc.get("modified", "")),
    )


@router.post("", response_model=SSMLDocument)
async def create_ssml_document(request: SSMLCreateRequest):
    """Create a new SSML document."""
    import uuid

    try:
        document_id = f"ssml-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        document = {
            "id": document_id,
            "name": request.name,
            "content": request.content,
            "profile_id": request.profile_id,
            "project_id": request.project_id,
            "created": now,
            "modified": now,
        }

        _ssml_documents[document_id] = document
        logger.info(f"Created SSML document: {document_id} ({request.name})")

        return SSMLDocument(
            id=document_id,
            name=request.name,
            content=request.content,
            profile_id=request.profile_id,
            project_id=request.project_id,
            created=now,
            modified=now,
        )
    except Exception as e:
        logger.error(f"Failed to create SSML document: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to create document: {e!s}") from e


@router.put("/{document_id}", response_model=SSMLDocument)
async def update_ssml_document(document_id: str, request: SSMLUpdateRequest):
    """Update an SSML document."""
    if document_id not in _ssml_documents:
        raise HTTPException(status_code=404, detail="SSML document not found")

    try:
        doc = _ssml_documents[document_id].copy()

        if request.name is not None:
            doc["name"] = request.name
        if request.content is not None:
            doc["content"] = request.content
        if request.profile_id is not None:
            doc["profile_id"] = request.profile_id

        doc["modified"] = datetime.utcnow().isoformat()
        _ssml_documents[document_id] = doc

        logger.debug(f"Updated SSML document: {document_id}")

        return SSMLDocument(
            id=str(doc.get("id", "")),
            name=str(doc.get("name", "")),
            content=str(doc.get("content", "")),
            profile_id=doc.get("profile_id"),
            project_id=doc.get("project_id"),
            created=str(doc.get("created", "")),
            modified=str(doc.get("modified", "")),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update SSML document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to update document: {e!s}") from e


@router.delete("/{document_id}")
async def delete_ssml_document(document_id: str):
    """Delete an SSML document."""
    if document_id not in _ssml_documents:
        raise HTTPException(status_code=404, detail="SSML document not found")

    try:
        del _ssml_documents[document_id]
        logger.info(f"Deleted SSML document: {document_id}")
        return {"success": True}
    except Exception as e:
        logger.error(f"Failed to delete SSML document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to delete document: {e!s}") from e


@router.post("/validate", response_model=SSMLValidateResponse)
async def validate_ssml(request: SSMLCreateRequest):
    """Validate SSML content."""
    import re
    import xml.etree.ElementTree as ET

    errors: list[str] = []
    warnings: list[str] = []

    if not request.content.strip():
        errors.append("SSML content cannot be empty")
        return SSMLValidateResponse(valid=False, errors=errors, warnings=warnings)

    # Check for <speak> tag
    if "<speak>" not in request.content:
        errors.append("SSML must contain <speak> tag")
    if "</speak>" not in request.content:
        errors.append("SSML must contain closing </speak> tag")

    # Try to parse as XML
    try:
        # Wrap in speak tag if not present for parsing
        content = request.content.strip()
        if not content.startswith("<speak"):
            content = f"<speak>{content}</speak>"

        # Parse XML
        root = ET.fromstring(content)

        # Validate root element
        if root.tag != "speak":
            errors.append("Root element must be <speak>")

        # Check for valid SSML elements
        valid_elements = {
            "speak",
            "p",
            "s",
            "break",
            "prosody",
            "emphasis",
            "say-as",
            "phoneme",
            "sub",
            "audio",
            "mark",
            "lang",
        }

        # Recursively validate elements
        def validate_element(elem, path=""):
            current_path = f"{path}/{elem.tag}" if path else elem.tag

            # Check if element is valid
            if elem.tag not in valid_elements and not elem.tag.startswith("{"):
                warnings.append(f"Unknown SSML element: {elem.tag} at {current_path}")

            # Validate attributes
            if elem.tag == "prosody":
                valid_attrs = {"rate", "pitch", "volume"}
                for attr in elem.attrib:
                    if attr not in valid_attrs:
                        warnings.append(f"Unknown prosody attribute: {attr} " f"at {current_path}")

            if elem.tag == "break":
                if "time" not in elem.attrib and "strength" not in elem.attrib:
                    warnings.append(
                        f"<break> should have 'time' or 'strength' " f"attribute at {current_path}"
                    )

            if elem.tag == "say-as" and "interpret-as" not in elem.attrib:
                warnings.append(
                    f"<say-as> should have 'interpret-as' " f"attribute at {current_path}"
                )

            # Recursively validate children
            for child in elem:
                validate_element(child, current_path)

        validate_element(root)

    except ET.ParseError as e:
        errors.append(f"Invalid XML structure: {e!s}")
    except Exception as e:
        warnings.append(f"XML parsing warning: {e!s}")

    # Check for common SSML issues
    # Unclosed tags
    open_tags = re.findall(r"<([^/>]+)>", request.content)
    close_tags = re.findall(r"</([^>]+)>", request.content)
    tag_counts: dict[str, int] = {}
    for tag in open_tags:
        tag_name = tag.split()[0] if " " in tag else tag
        tag_counts[tag_name] = tag_counts.get(tag_name, 0) + 1
    for tag in close_tags:
        tag_counts[tag] = tag_counts.get(tag, 0) - 1

    for tag, count in tag_counts.items():
        if count > 0:
            errors.append(f"Unclosed tag: <{tag}>")
        elif count < 0:
            errors.append(f"Extra closing tag: </{tag}>")

    return SSMLValidateResponse(valid=len(errors) == 0, errors=errors, warnings=warnings)


@router.post("/preview", response_model=SSMLPreviewResponse)
async def preview_ssml(request: SSMLPreviewRequest):
    """Preview SSML by synthesizing it."""
    import xml.etree.ElementTree as ET

    try:
        # Parse SSML to extract text content and apply SSML features
        ssml_text = request.content.strip()

        # Ensure wrapped in <speak> tag
        if not ssml_text.startswith("<speak"):
            ssml_text = f"<speak>{ssml_text}</speak>"

        # Parse SSML XML
        try:
            root = ET.fromstring(ssml_text)
        except ET.ParseError as e:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid SSML XML structure: {e!s}",
            )

        # Extract text content and process SSML tags
        def extract_text_with_ssml(elem):
            """Extract text from SSML element, processing SSML tags."""
            text_parts = []

            # Get direct text content
            if elem.text:
                text_parts.append(elem.text.strip())

            # Process child elements
            for child in elem:
                if child.tag == "break":
                    # Add pause based on break tag
                    if "time" in child.attrib:
                        # Convert time to pause (e.g., "500ms" -> pause)
                        # Note: Break timing would be applied in synthesis
                        text_parts.append(" ")  # Space for pause
                    elif "strength" in child.attrib:
                        # Map strength to pause duration
                        # Note: Break strength would be applied in synthesis
                        text_parts.append(" ")  # Space for pause
                elif child.tag == "prosody":
                    # Extract prosody-controlled text
                    prosody_text = extract_text_with_ssml(child)
                    # Note: Prosody parameters would be applied in synthesis
                    text_parts.append(prosody_text)
                elif child.tag == "emphasis":
                    # Extract emphasized text
                    emphasis_text = extract_text_with_ssml(child)
                    # Note: Emphasis level would be applied in synthesis
                    text_parts.append(emphasis_text)
                elif child.tag == "say-as":
                    # Extract say-as text
                    say_as_text = extract_text_with_ssml(child)
                    # Note: say-as interpretation would be applied in synthesis
                    text_parts.append(say_as_text)
                elif child.tag in ("p", "s"):
                    # Paragraph or sentence - extract text
                    para_text = extract_text_with_ssml(child)
                    text_parts.append(para_text)
                else:
                    # Other tags - extract text content
                    child_text = extract_text_with_ssml(child)
                    text_parts.append(child_text)

                # Add tail text
                if child.tail:
                    text_parts.append(child.tail.strip())

            return " ".join(filter(None, text_parts))

        text_content = extract_text_with_ssml(root).strip()

        # Use NLP preprocessing for SSML if available
        if HAS_NLP:
            try:
                preprocessor = get_text_preprocessor()
                # Preprocess text for SSML (normalize, segment sentences)
                preprocessed = preprocessor.preprocess_for_ssml(
                    text_content,
                    language="en",  # Could extract from SSML lang attribute
                    add_prosody_hints=True,
                )
                text_content = preprocessed
            except Exception as e:
                logger.warning(f"NLP preprocessing failed, using raw text: {e}")

        if not text_content:
            raise HTTPException(
                status_code=400,
                detail="SSML content contains no text to synthesize",
            )

        # Determine engine (default to xtts if not specified)
        engine = request.engine or "xtts"

        # Use voice synthesis endpoint to synthesize the extracted text
        from ..models_additional import VoiceSynthesizeRequest
        from .voice import synthesize

        # Create synthesis request
        synth_request = VoiceSynthesizeRequest(
            profile_id=request.profile_id or "",
            text=text_content,
            engine=engine,
            language="en",  # Could be extracted from SSML lang attribute
        )

        # Synthesize using voice endpoint
        synth_response = await synthesize(synth_request)

        # Return audio ID and duration from synthesis response
        return SSMLPreviewResponse(
            audio_id=synth_response.audio_id,
            duration=synth_response.duration,
            message="SSML preview synthesized successfully",
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to preview SSML: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to synthesize SSML preview: {e!s}",
        ) from e
