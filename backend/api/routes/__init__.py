"""
VoiceStudio API Routes Package

Task 2.4: Context aggregators in routes/contexts/ (voice, audio, project, ml,
platform, plugins, media) provide APIRouter grouping for OpenAPI tags.
Routes remain in flat structure; contexts aggregate for documentation.

API Naming Conventions (GAP-INT-003):

    1. RESOURCE NAMING
       - Use plural nouns: /profiles, /engines, /clips
       - Use lowercase with hyphens: /voice-profiles, /audio-streams
       - Avoid verbs in URLs (use HTTP methods instead)

    2. HTTP METHODS
       - GET: Retrieve resource(s)
       - POST: Create resource or trigger action
       - PUT: Update entire resource
       - PATCH: Partial update
       - DELETE: Remove resource

    3. STANDARD PATTERNS
       - Collection: GET /resources
       - Single item: GET /resources/{id}
       - Create: POST /resources
       - Update: PUT /resources/{id}
       - Delete: DELETE /resources/{id}
       - Action: POST /resources/{id}/action-name

    4. VERSIONING
       - Routes under /api/v1/ for versioned endpoints
       - Non-versioned routes are v1 by default

    5. WEBSOCKET ENDPOINTS
       - Use /ws/ prefix or /stream suffix: /synthesize/stream
       - Follow protocol in backend/api/ws/protocol.py

    6. QUERY PARAMETERS
       - Filtering: ?engine=xtts&status=active
       - Pagination: ?page=1&limit=20
       - Sorting: ?sort=created_at&order=desc

    7. RESPONSE FORMAT
       - Success: {"status": "success", "data": {...}}
       - Error: {"status": "error", "message": "...", "code": "..."}
       - List: {"items": [...], "total": N, "page": 1}

Migration Notes:
    Some legacy endpoints don't follow these conventions. New endpoints
    MUST follow these patterns. Existing endpoints should be migrated
    when making significant changes.
"""
