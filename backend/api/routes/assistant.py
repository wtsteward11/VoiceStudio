"""
AI Production Assistant Routes (Phase 9.1.5)

Endpoints for AI-powered production assistance and guidance.
Replaces the 501 stub with real LLM integration using local-first
Ollama provider with fallback to cloud providers.
"""

from __future__ import annotations

import logging
import uuid
from datetime import datetime

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/assistant", tags=["assistant"])

# In-memory conversation history (future: persist via JsonFileStore)
_conversations: dict[str, dict] = {}


class AssistantMessage(BaseModel):
    """Message in assistant conversation."""

    message_id: str
    conversation_id: str
    role: str  # user, assistant
    content: str
    timestamp: str
    suggestions: list[str] | None = None


class Conversation(BaseModel):
    """Assistant conversation."""

    conversation_id: str
    title: str
    messages: list[AssistantMessage]
    created: str
    updated: str


class ChatRequest(BaseModel):
    """Request to chat with assistant."""

    conversation_id: str | None = None
    message: str
    context: dict | None = None  # Project context, current state, etc.
    stream: bool = False


class ChatResponse(BaseModel):
    """Response from assistant."""

    conversation_id: str
    message_id: str
    content: str
    suggestions: list[str]
    timestamp: str
    tool_calls: list[dict] | None = None
    usage: dict | None = None


class TaskSuggestion(BaseModel):
    """AI-suggested production task."""

    task_id: str
    title: str
    description: str
    category: str  # mixing, mastering, editing, synthesis, etc.
    priority: str  # high, medium, low
    estimated_time: int | None = None  # minutes
    confidence: float = 0.0  # 0.0 to 1.0


def _get_llm_provider():
    """
    Get the best available LLM provider.

    Priority: Ollama (local) > LocalAI > OpenAI (cloud)

    Uses LLMProviderService for clean architecture compliance.
    """
    from backend.ml.models.llm_provider_service import get_llm_provider_service

    provider_service = get_llm_provider_service()
    return provider_service.get_best_provider_instance()


def _build_system_prompt(context: dict | None = None) -> str:
    """Build a system prompt with optional project context."""
    base_prompt = (
        "You are VoiceStudio AI Assistant, an expert in audio production, "
        "voice synthesis, voice cloning, and audio engineering. You help users "
        "with their voice production projects. You can suggest next steps, "
        "explain audio concepts, and help optimize their workflow. "
        "Keep responses concise and actionable."
    )

    if context:
        context_parts = []
        if context.get("project_name"):
            context_parts.append(f"Current project: {context['project_name']}")
        if context.get("active_engine"):
            context_parts.append(f"Active engine: {context['active_engine']}")
        if context.get("recent_action"):
            context_parts.append(f"Recent action: {context['recent_action']}")

        if context_parts:
            base_prompt += "\n\nCurrent context:\n" + "\n".join(context_parts)

    return base_prompt


def _generate_suggestions(content: str) -> list[str]:
    """Generate follow-up suggestions based on response content."""
    suggestions = []
    content_lower = content.lower()

    if any(w in content_lower for w in ["synthesize", "voice", "speak"]):
        suggestions.append("Try synthesizing with a different voice")
    if any(w in content_lower for w in ["quality", "improve", "better"]):
        suggestions.append("Run a quality analysis on the audio")
    if any(w in content_lower for w in ["clone", "cloning"]):
        suggestions.append("Upload a reference audio for cloning")
    if any(w in content_lower for w in ["effect", "filter", "reverb"]):
        suggestions.append("Preview the effect on your audio")

    if not suggestions:
        suggestions = [
            "What would you like to do next?",
            "Show me available voice profiles",
        ]

    return suggestions[:3]


@router.post("/chat", response_model=ChatResponse)
async def chat_with_assistant(request: ChatRequest):
    """Chat with AI production assistant."""
    now = datetime.utcnow().isoformat()

    # Get or create conversation
    if request.conversation_id and request.conversation_id in _conversations:
        conversation = Conversation(**_conversations[request.conversation_id])
    else:
        conversation_id = f"conv-{uuid.uuid4().hex[:8]}"
        conversation = Conversation(
            conversation_id=conversation_id,
            title=(request.message[:50] + "..." if len(request.message) > 50 else request.message),
            messages=[],
            created=now,
            updated=now,
        )

    # Add user message
    user_message = AssistantMessage(
        message_id=f"msg-{uuid.uuid4().hex[:8]}",
        conversation_id=conversation.conversation_id,
        role="user",
        content=request.message,
        timestamp=now,
    )
    conversation.messages.append(user_message)

    # Get LLM provider
    provider = _get_llm_provider()

    if provider is None:
        # No LLM available - return helpful guidance
        assistant_content = (
            "I'm currently unable to connect to a language model. "
            "To enable the AI assistant, please start Ollama locally "
            "(ollama serve) or configure an API key for a cloud provider. "
            f'\n\nYour message: "{request.message}"'
        )
        suggestions = [
            "Install Ollama: https://ollama.com",
            "Start Ollama: ollama serve",
            "Pull a model: ollama pull llama3.2",
        ]
    else:
        try:
            from backend.ml.models.llm_function_calling import get_function_registry

            from app.core.engines.llm_interface import LLMConfig, Message, MessageRole

            # Build message history
            llm_messages = []
            for msg in conversation.messages:
                role = MessageRole.USER if msg.role == "user" else MessageRole.ASSISTANT
                llm_messages.append(Message(role=role, content=msg.content))

            # Get function specs
            registry = get_function_registry()
            function_specs = registry.get_specs()

            # Configure with system prompt
            config = LLMConfig(
                system_prompt=_build_system_prompt(request.context),
                temperature=0.7,
                max_tokens=1024,
            )

            # Generate response
            response = await provider.generate(
                messages=llm_messages,
                config=config,
                functions=function_specs if function_specs else None,
            )

            # Handle tool calls if any
            tool_results = None
            if response.tool_calls:
                tool_results = await registry.process_tool_calls(response.tool_calls)
                # Generate follow-up response with tool results
                for result in tool_results:
                    llm_messages.append(
                        Message(
                            role=MessageRole.TOOL,
                            content=result["content"],
                            tool_call_id=result.get("tool_call_id", ""),
                        )
                    )
                follow_up = await provider.generate(
                    messages=llm_messages,
                    config=config,
                )
                assistant_content = follow_up.content
            else:
                assistant_content = response.content

            suggestions = _generate_suggestions(assistant_content)

        except Exception as exc:
            logger.error(f"LLM generation failed: {exc}")
            assistant_content = (
                f"I encountered an error processing your request: {exc!s}. "
                "Please check that your LLM provider is running correctly."
            )
            suggestions = ["Check Ollama status", "Try again"]

    # Add assistant message
    assistant_msg_id = f"msg-{uuid.uuid4().hex[:8]}"
    assistant_message = AssistantMessage(
        message_id=assistant_msg_id,
        conversation_id=conversation.conversation_id,
        role="assistant",
        content=assistant_content,
        timestamp=datetime.utcnow().isoformat(),
        suggestions=suggestions,
    )
    conversation.messages.append(assistant_message)

    # Persist conversation
    conversation.updated = datetime.utcnow().isoformat()
    _conversations[conversation.conversation_id] = conversation.model_dump()

    return ChatResponse(
        conversation_id=conversation.conversation_id,
        message_id=assistant_msg_id,
        content=assistant_content,
        suggestions=suggestions,
        timestamp=assistant_message.timestamp,
    )


@router.get("/conversations", response_model=list[Conversation])
async def list_conversations():
    """List all conversations."""
    conversations = list(_conversations.values())
    return [Conversation(**c) for c in conversations]


@router.get("/conversations/{conversation_id}", response_model=Conversation)
async def get_conversation(conversation_id: str):
    """Get a conversation by ID."""
    if conversation_id not in _conversations:
        raise HTTPException(status_code=404, detail="Conversation not found")
    return Conversation(**_conversations[conversation_id])


@router.delete("/conversations/{conversation_id}")
async def delete_conversation(conversation_id: str):
    """Delete a conversation."""
    if conversation_id not in _conversations:
        raise HTTPException(status_code=404, detail="Conversation not found")
    del _conversations[conversation_id]
    logger.info(f"Deleted conversation: {conversation_id}")
    return {"success": True}


@router.post("/suggest-tasks", response_model=list[TaskSuggestion])
async def suggest_tasks(
    project_id: str,
    context: dict | None = None,
):
    """Get AI-suggested production tasks."""
    provider = _get_llm_provider()

    if provider is None:
        raise HTTPException(
            status_code=503,
            detail=(
                "No LLM provider available. Start Ollama (ollama serve) " "or configure an API key."
            ),
        )

    try:
        from app.core.engines.llm_interface import LLMConfig, Message, MessageRole

        prompt = (
            f"Based on a voice production project (ID: {project_id}), "
            "suggest 3-5 specific production tasks. For each task, provide:\n"
            "1. A short title\n2. A description\n"
            "3. Category (mixing/mastering/editing/synthesis/cloning)\n"
            "4. Priority (high/medium/low)\n"
            "5. Estimated time in minutes\n\n"
            "Format each as: TITLE | DESCRIPTION | CATEGORY | PRIORITY | MINUTES"
        )

        if context:
            prompt += f"\n\nProject context: {context}"

        config = LLMConfig(temperature=0.5, max_tokens=512)
        response = await provider.generate(
            messages=[Message(role=MessageRole.USER, content=prompt)],
            config=config,
        )

        # Parse response into structured tasks
        tasks = []
        for line in response.content.strip().split("\n"):
            parts = [p.strip() for p in line.split("|")]
            if len(parts) >= 4:
                try:
                    est_time = int(parts[4]) if len(parts) > 4 else None
                except (ValueError, IndexError):
                    est_time = None

                tasks.append(
                    TaskSuggestion(
                        task_id=f"task-{uuid.uuid4().hex[:8]}",
                        title=parts[0],
                        description=parts[1],
                        category=parts[2].lower(),
                        priority=parts[3].lower(),
                        estimated_time=est_time,
                        confidence=0.75,
                    )
                )

        if not tasks:
            # Fallback: return the raw response as a single suggestion
            tasks.append(
                TaskSuggestion(
                    task_id=f"task-{uuid.uuid4().hex[:8]}",
                    title="AI Suggestion",
                    description=response.content[:200],
                    category="general",
                    priority="medium",
                    confidence=0.5,
                )
            )

        return tasks

    except Exception as exc:
        logger.error(f"Failed to suggest tasks: {exc}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to generate suggestions: {exc!s}",
        ) from exc


@router.get("/providers")
async def list_providers():
    """List available LLM providers and their status."""
    from backend.ml.models.llm_provider_service import get_llm_provider_service

    provider_service = get_llm_provider_service()
    providers = []

    for p in provider_service.get_available_providers():
        provider_entry = {
            "name": p.name,
            "type": "local" if p.local else "cloud",
            "available": p.available,
        }
        # Add endpoint URL for local providers
        if p.endpoint:
            provider_entry["url"] = p.endpoint
        # Add note for OpenAI
        if p.name == "openai":
            provider_entry["note"] = "Requires OPENAI_API_KEY environment variable"
        providers.append(provider_entry)

    return {"providers": providers}
