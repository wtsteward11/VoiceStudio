"""
Speaker Embedding Explorer Routes

Endpoints for exploring and visualizing speaker embeddings.
"""

from __future__ import annotations

import logging
import asyncio
import os
import uuid
from datetime import datetime
from pathlib import Path

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

logger = logging.getLogger(__name__)

# Try to import speaker embedding libraries
HAS_RESEMBLYZER = False
HAS_SPEECHBRAIN = False
_voice_encoder = None

try:
    from resemblyzer import VoiceEncoder, preprocess_wav
    from resemblyzer.audio import sampling_rate as RESEMBLYZER_SR

    HAS_RESEMBLYZER = True
    logger.info("Resemblyzer available for speaker embedding extraction")
except ImportError:
    logger.debug("Resemblyzer not installed. Install with: pip install resemblyzer")

try:
    from speechbrain.inference.speaker import EncoderClassifier

    HAS_SPEECHBRAIN = True
    logger.info("SpeechBrain available for speaker embedding extraction")
except ImportError:
    logger.debug("SpeechBrain not installed. Install with: pip install speechbrain")

from typing import Any

HAS_LIBROSA = False
librosa: Any = None
try:
    import librosa as _librosa_mod

    librosa = _librosa_mod
    HAS_LIBROSA = True
except ImportError:  # ALLOWED: bare except - optional librosa
    pass

router = APIRouter(prefix="/api/embedding-explorer", tags=["embedding-explorer"])

# In-memory embedding data (replace with database in production)
_embeddings: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class EmbeddingVector(BaseModel):
    """Speaker embedding vector."""

    embedding_id: str
    voice_profile_id: str
    vector: list[float]  # Embedding vector (typically 256-512 dimensions)
    dimension: int
    created: str


class EmbeddingSimilarity(BaseModel):
    """Similarity between two embeddings."""

    embedding_id_1: str
    embedding_id_2: str
    similarity: float  # 0.0 to 1.0 (cosine similarity)
    distance: float  # Euclidean distance


class EmbeddingCluster(BaseModel):
    """Cluster of similar embeddings."""

    cluster_id: str
    embedding_ids: list[str]
    centroid: list[float]
    size: int


class EmbeddingVisualization(BaseModel):
    """2D/3D visualization data for embeddings."""

    embedding_id: str
    x: float
    y: float
    z: float | None = None
    color: str | None = None
    label: str | None = None
    cluster: str | None = None


class EmbeddingExtractRequest(BaseModel):
    """Request to extract embedding from audio."""

    audio_id: str
    voice_profile_id: str | None = None
    method: str = "default"  # Extraction method


class EmbeddingCompareRequest(BaseModel):
    """Request to compare embeddings."""

    embedding_id_1: str
    embedding_id_2: str


@router.post("/extract", response_model=EmbeddingVector)
async def extract_embedding(request: EmbeddingExtractRequest):
    """
    Extract speaker embedding from audio.

    Supports multiple extraction methods:
    - resemblyzer: Fast and lightweight speaker encoder
    - speechbrain: High-quality embeddings using ECAPA-TDNN
    """
    global _voice_encoder

    # Check if any embedding library is available
    if not (HAS_RESEMBLYZER or HAS_SPEECHBRAIN):
        raise HTTPException(
            status_code=400,
            detail=(
                "Speaker embedding extraction requires an embedding library. "
                "Install one of: pip install resemblyzer OR pip install speechbrain"
            ),
        )

    if not HAS_LIBROSA:
        raise HTTPException(
            status_code=400,
            detail="Audio loading requires librosa. Install with: pip install librosa",
        )

    try:
        # Resolve audio file path from audio_id
        # In a real implementation, this would look up the audio file in a database/storage
        audio_storage_path = os.environ.get("VOICESTUDIO_AUDIO_STORAGE", "data/audio")
        audio_path = Path(audio_storage_path) / f"{request.audio_id}.wav"

        # Try alternative extensions if .wav not found
        if not audio_path.exists():
            for ext in [".mp3", ".flac", ".ogg", ""]:
                alt_path = Path(audio_storage_path) / f"{request.audio_id}{ext}"
                if alt_path.exists():
                    audio_path = alt_path
                    break

        if not audio_path.exists():
            raise HTTPException(
                status_code=404,
                detail=f"Audio file not found for audio_id: {request.audio_id}",
            )

        # Extract embedding based on available library
        embedding_vector: list[float] = []

        if HAS_RESEMBLYZER and (request.method == "default" or request.method == "resemblyzer"):
            # Use Resemblyzer for embedding extraction
            if _voice_encoder is None:
                _voice_encoder = VoiceEncoder()
                logger.info("Loaded Resemblyzer VoiceEncoder")

            # Load and preprocess audio
            wav = preprocess_wav(str(audio_path))

            # Extract embedding (256-dimensional)
            embedding = _voice_encoder.embed_utterance(wav)
            embedding_vector = embedding.tolist()

        elif HAS_SPEECHBRAIN and (request.method == "default" or request.method == "speechbrain"):
            # Use SpeechBrain for embedding extraction
            classifier = EncoderClassifier.from_hparams(
                source="speechbrain/spkrec-ecapa-voxceleb",
                savedir="models/speechbrain_speaker",
            )

            # Load audio
            audio, _sr = librosa.load(str(audio_path), sr=16000)

            # Extract embedding (192-dimensional for ECAPA-TDNN)
            import torch

            audio_tensor = torch.tensor(audio).unsqueeze(0)
            embedding = classifier.encode_batch(audio_tensor)
            embedding_vector = embedding.squeeze().numpy().tolist()
        else:
            raise HTTPException(
                status_code=400,
                detail=f"Unknown extraction method: {request.method}. Use 'resemblyzer' or 'speechbrain'",
            )

        # Generate embedding ID
        embedding_id = str(uuid.uuid4())[:8]
        voice_profile_id = request.voice_profile_id or request.audio_id

        # Create embedding record
        embedding_data = {
            "embedding_id": embedding_id,
            "voice_profile_id": voice_profile_id,
            "vector": embedding_vector,
            "dimension": len(embedding_vector),
            "created": datetime.now().isoformat(),
            "audio_id": request.audio_id,
            "method": request.method,
        }

        # Store embedding
        _embeddings[embedding_id] = embedding_data

        logger.info(f"Extracted {len(embedding_vector)}-dim embedding for audio {request.audio_id}")

        return EmbeddingVector(
            embedding_id=embedding_id,
            voice_profile_id=voice_profile_id,
            vector=embedding_vector,
            dimension=len(embedding_vector),
            created=str(embedding_data["created"]),
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to extract embedding: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to extract embedding: {e!s}",
        ) from e


@router.get("/embeddings", response_model=list[EmbeddingVector])
async def list_embeddings(voice_profile_id: str | None = None):
    """List all embeddings."""
    embeddings = list(_embeddings.values())

    if voice_profile_id:
        embeddings = [e for e in embeddings if e.get("voice_profile_id") == voice_profile_id]

    return [EmbeddingVector(**e) for e in embeddings]


@router.get("/embeddings/{embedding_id}", response_model=EmbeddingVector)
async def get_embedding(embedding_id: str):
    """Get an embedding by ID."""
    if embedding_id not in _embeddings:
        raise HTTPException(status_code=404, detail="Embedding not found")

    return EmbeddingVector(**_embeddings[embedding_id])


@router.delete("/embeddings/{embedding_id}")
async def delete_embedding(embedding_id: str):
    """Delete an embedding."""
    if embedding_id not in _embeddings:
        raise HTTPException(status_code=404, detail="Embedding not found")

    del _embeddings[embedding_id]
    logger.info(f"Deleted embedding: {embedding_id}")
    return {"success": True}


@router.post("/compare", response_model=EmbeddingSimilarity)
async def compare_embeddings(request: EmbeddingCompareRequest):
    """Compare two embeddings and calculate similarity."""
    if request.embedding_id_1 not in _embeddings:
        raise HTTPException(status_code=404, detail="Embedding 1 not found")

    if request.embedding_id_2 not in _embeddings:
        raise HTTPException(status_code=404, detail="Embedding 2 not found")

    emb1 = EmbeddingVector(**_embeddings[request.embedding_id_1])
    emb2 = EmbeddingVector(**_embeddings[request.embedding_id_2])

    # Calculate cosine similarity
    import math

    dot_product = sum(a * b for a, b in zip(emb1.vector, emb2.vector))
    magnitude1 = math.sqrt(sum(a * a for a in emb1.vector))
    magnitude2 = math.sqrt(sum(a * a for a in emb2.vector))

    similarity = dot_product / (magnitude1 * magnitude2) if (magnitude1 * magnitude2) > 0 else 0.0

    # Calculate Euclidean distance
    distance = math.sqrt(sum((a - b) ** 2 for a, b in zip(emb1.vector, emb2.vector)))

    return EmbeddingSimilarity(
        embedding_id_1=request.embedding_id_1,
        embedding_id_2=request.embedding_id_2,
        similarity=similarity,
        distance=distance,
    )


@router.post("/visualize", response_model=list[EmbeddingVisualization])
async def visualize_embeddings(
    embedding_ids: list[str],
    method: str = "pca",  # pca, t-sne, umap
    dimensions: int = 2,  # 2 or 3
):
    """Generate 2D/3D visualization coordinates for embeddings."""
    if not embedding_ids:
        raise HTTPException(
            status_code=422,
            detail="embedding_ids must contain at least one embedding ID",
        )

    # Load embeddings
    embeddings_data = []
    for emb_id in embedding_ids:
        if emb_id in _embeddings:
            embeddings_data.append(EmbeddingVector(**_embeddings[emb_id]))

    if not embeddings_data:
        raise HTTPException(status_code=404, detail="No embeddings found")

    # Perform dimensionality reduction
    try:
        import numpy as np
    except ImportError:
        raise HTTPException(
            status_code=400,
            detail="numpy is required for visualization. Install with: pip install numpy",
        )

    vectors = np.array([e.vector for e in embeddings_data])

    if method == "pca":
        try:
            from sklearn.decomposition import PCA
        except ImportError:
            raise HTTPException(
                status_code=400,
                detail="PCA requires scikit-learn. Install with: pip install scikit-learn",
            )
        reducer = PCA(n_components=min(dimensions, vectors.shape[1], vectors.shape[0]))
        coords = reducer.fit_transform(vectors)
    elif method == "t-sne" or method == "tsne":
        try:
            from sklearn.manifold import TSNE
        except ImportError:
            raise HTTPException(
                status_code=400,
                detail="t-SNE requires scikit-learn. Install with: pip install scikit-learn",
            )
        perplexity = min(30, max(1, len(vectors) - 1))
        reducer = TSNE(n_components=dimensions, perplexity=perplexity, random_state=42)
        coords = reducer.fit_transform(vectors)
    elif method == "umap":
        try:
            import umap
        except ImportError:
            raise HTTPException(
                status_code=400,
                detail="UMAP requires umap-learn. Install with: pip install umap-learn",
            )
        n_neighbors = min(15, max(2, len(vectors) - 1))
        reducer = umap.UMAP(n_components=dimensions, n_neighbors=n_neighbors, random_state=42)
        coords = reducer.fit_transform(vectors)
    else:
        raise HTTPException(
            status_code=400,
            detail=f"Unknown method '{method}'. Use 'pca', 't-sne', or 'umap'.",
        )

    results = []
    for i, emb in enumerate(embeddings_data):
        point = coords[i].tolist()
        results.append(
            EmbeddingVisualization(
                embedding_id=emb.embedding_id,
                x=point[0],
                y=point[1] if len(point) > 1 else 0.0,
                z=point[2] if len(point) > 2 else None,
                label=emb.voice_profile_id or emb.embedding_id,
                cluster=None,
            )
        )
    return results


@router.post("/cluster", response_model=list[EmbeddingCluster])
async def cluster_embeddings(
    embedding_ids: list[str],
    num_clusters: int = 5,
    method: str = "kmeans",  # kmeans, dbscan, hierarchical
):
    """Cluster embeddings by similarity."""
    if not embedding_ids:
        raise HTTPException(
            status_code=422,
            detail="embedding_ids must contain at least one embedding ID",
        )

    # Load embeddings
    embeddings_data = []
    for emb_id in embedding_ids:
        if emb_id in _embeddings:
            embeddings_data.append(EmbeddingVector(**_embeddings[emb_id]))

    if not embeddings_data:
        raise HTTPException(status_code=404, detail="No embeddings found")

    try:
        import numpy as np
    except ImportError:
        raise HTTPException(
            status_code=400,
            detail="numpy is required for clustering. Install with: pip install numpy",
        )

    vectors = np.array([e.vector for e in embeddings_data])

    if method == "kmeans":
        try:
            from sklearn.cluster import KMeans
        except ImportError:
            raise HTTPException(
                status_code=400,
                detail="K-Means requires scikit-learn. Install with: pip install scikit-learn",
            )
        k = min(num_clusters, len(vectors))
        model = KMeans(n_clusters=k, random_state=42, n_init="auto")
        labels = model.fit_predict(vectors)
        centroids = model.cluster_centers_
    elif method == "dbscan":
        try:
            from sklearn.cluster import DBSCAN
        except ImportError:
            raise HTTPException(
                status_code=400,
                detail="DBSCAN requires scikit-learn. Install with: pip install scikit-learn",
            )
        model = DBSCAN(eps=0.5, min_samples=2)
        labels = model.fit_predict(vectors)
        unique_labels = set(labels)
        centroids = (
            np.array(
                [vectors[labels == lbl].mean(axis=0) for lbl in sorted(unique_labels) if lbl != -1]
            )
            if any(lbl != -1 for lbl in unique_labels)
            else np.empty((0, vectors.shape[1]))
        )
    elif method == "hierarchical":
        try:
            from sklearn.cluster import AgglomerativeClustering
        except ImportError:
            raise HTTPException(
                status_code=400,
                detail="Hierarchical clustering requires scikit-learn. Install with: pip install scikit-learn",
            )
        k = min(num_clusters, len(vectors))
        model = AgglomerativeClustering(n_clusters=k)
        labels = model.fit_predict(vectors)
        centroids = np.array([vectors[labels == lbl].mean(axis=0) for lbl in range(k)])
    else:
        raise HTTPException(
            status_code=400,
            detail=f"Unknown method '{method}'. Use 'kmeans', 'dbscan', or 'hierarchical'.",
        )

    # Group embeddings by cluster
    from collections import defaultdict

    cluster_groups: dict[int, list[str]] = defaultdict(list)
    for i, emb in enumerate(embeddings_data):
        cluster_groups[int(labels[i])].append(emb.embedding_id)

    results = []
    for cluster_id in sorted(cluster_groups.keys()):
        centroid = centroids[cluster_id].tolist() if cluster_id < len(centroids) else []
        results.append(
            EmbeddingCluster(
                cluster_id=str(cluster_id),
                embedding_ids=cluster_groups[cluster_id],
                centroid=centroid,
                size=len(cluster_groups[cluster_id]),
            )
        )
    return results
