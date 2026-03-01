#!/usr/bin/env python3
"""
Library Integration Verification Script
Tests all integrated libraries from OLD_PROJECT_INTEGRATION

This script verifies that all libraries are properly installed and integrated.
"""

import logging
import sys

logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
logger = logging.getLogger(__name__)

# Test results
results = {
    "passed": [],
    "failed": [],
    "skipped": [],
}


def test_import(module_name: str, import_statement: str, description: str):
    """Test if a module can be imported."""
    try:
        exec(import_statement)
        results["passed"].append(f"{module_name}: {description}")
        logger.info(f"✅ {module_name}: {description}")
        return True
    except ImportError as e:
        results["failed"].append(f"{module_name}: {description} - {e}")
        logger.error(f"❌ {module_name}: {description} - {e}")
        return False
    except Exception as e:
        results["failed"].append(f"{module_name}: {description} - {e}")
        logger.error(f"❌ {module_name}: {description} - {e}")
        return False


def test_module_functionality(module_name: str, test_func, description: str):
    """Test if a module's functionality works."""
    try:
        if test_func():
            results["passed"].append(f"{module_name}: {description}")
            logger.info(f"✅ {module_name}: {description}")
            return True
        else:
            results["failed"].append(
                f"{module_name}: {description} - Test returned False"
            )
            logger.error(f"❌ {module_name}: {description} - Test returned False")
            return False
    except Exception as e:
        results["failed"].append(f"{module_name}: {description} - {e}")
        logger.error(f"❌ {module_name}: {description} - {e}")
        return False


def main():
    """Run all verification tests."""
    logger.info("=" * 60)
    logger.info("Library Integration Verification")
    logger.info("=" * 60)
    logger.info("")

    # ============================================================================
    # Audio Quality Libraries
    # ============================================================================
    logger.info("Testing Audio Quality Libraries...")
    logger.info("-" * 60)

    # pesq
    test_import("pesq", "import pesq", "PESQ quality metric")

    # pystoi
    test_import("pystoi", "import pystoi", "STOI intelligibility metric")

    # voicefixer
    test_import(
        "voicefixer", "from voicefixer import VoiceFixer", "VoiceFixer restoration"
    )

    # deepfilternet
    try:
        import deepfilternet

        results["passed"].append("deepfilternet: DeepFilterNet enhancement")
        logger.info("✅ deepfilternet: DeepFilterNet enhancement")
    except ImportError:
        # Try alternative import
        try:
            import DeepFilterNet

            results["passed"].append(
                "deepfilternet: DeepFilterNet enhancement (alternative)"
            )
            logger.info("✅ deepfilternet: DeepFilterNet enhancement (alternative)")
        except ImportError as e:
            results["skipped"].append(
                f"deepfilternet: DeepFilterNet enhancement - {e} (optional)"
            )
            logger.warning(
                f"⚠️  deepfilternet: DeepFilterNet enhancement - {e} (optional)"
            )

    # resampy
    test_import("resampy", "import resampy", "High-quality resampling")

    # pyrubberband
    test_import(
        "pyrubberband",
        "import pyrubberband as pyrb",
        "Time-stretching and pitch-shifting",
    )

    # pedalboard
    test_import(
        "pedalboard", "from pedalboard import Pedalboard", "Professional audio effects"
    )

    # audiomentations
    test_import("audiomentations", "import audiomentations", "Audio data augmentation")

    logger.info("")

    # ============================================================================
    # RVC Dependencies
    # ============================================================================
    logger.info("Testing RVC Dependencies...")
    logger.info("-" * 60)

    # faiss-cpu
    test_import("faiss", "import faiss", "Vector similarity search")

    # pyworld
    test_import("pyworld", "import pyworld", "Vocoder features")

    # praat-parselmouth
    test_import("praat-parselmouth", "import parselmouth as pm", "Prosody analysis")

    logger.info("")

    # ============================================================================
    # Performance Monitoring
    # ============================================================================
    logger.info("Testing Performance Monitoring...")
    logger.info("-" * 60)

    # py-cpuinfo (imports as cpuinfo)
    test_import("py-cpuinfo", "import cpuinfo", "CPU information")

    # GPUtil
    test_import("GPUtil", "import GPUtil", "GPU monitoring")

    # nvidia-ml-py
    test_import("nvidia-ml-py", "import pynvml", "NVIDIA GPU monitoring")

    # wandb
    test_import("wandb", "import wandb", "Experiment tracking")

    # tensorboard
    try:
        from torch.utils.tensorboard import SummaryWriter

        results["passed"].append("tensorboard: Training visualization (torch.utils)")
        logger.info("✅ tensorboard: Training visualization (torch.utils)")
    except ImportError:
        try:
            from tensorboard import SummaryWriter

            results["passed"].append("tensorboard: Training visualization (standalone)")
            logger.info("✅ tensorboard: Training visualization (standalone)")
        except ImportError:
            results["failed"].append(
                "tensorboard: Training visualization - Not available"
            )
            logger.error("❌ tensorboard: Training visualization - Not available")

    logger.info("")

    # ============================================================================
    # Advanced Utilities
    # ============================================================================
    logger.info("Testing Advanced Utilities...")
    logger.info("-" * 60)

    # webrtcvad
    test_import("webrtcvad", "import webrtcvad", "Voice activity detection")

    # umap-learn
    test_import("umap-learn", "import umap", "Dimensionality reduction")

    # spacy
    test_import("spacy", "import spacy", "NLP processing")

    logger.info("")

    # ============================================================================
    # Metrics & Monitoring
    # ============================================================================
    logger.info("Testing Metrics & Monitoring...")
    logger.info("-" * 60)

    # prometheus-client
    test_import(
        "prometheus-client",
        "from prometheus_client import Counter",
        "Prometheus metrics",
    )

    # prometheus-fastapi-instrumentator
    test_import(
        "prometheus-fastapi-instrumentator",
        "from prometheus_fastapi_instrumentator import Instrumentator",
        "FastAPI Prometheus integration",
    )

    logger.info("")

    # ============================================================================
    # Deepfake & Video Processing
    # ============================================================================
    logger.info("Testing Deepfake & Video Processing...")
    logger.info("-" * 60)

    # insightface
    test_import("insightface", "import insightface", "Advanced face detection")

    # opencv-contrib-python
    try:
        import cv2

        if hasattr(cv2, "face") or hasattr(cv2, "xfeatures2d"):
            results["passed"].append("opencv-contrib-python: Contrib modules available")
            logger.info("✅ opencv-contrib-python: Contrib modules available")
        else:
            results["skipped"].append(
                "opencv-contrib-python: Contrib modules not available (using opencv-python)"
            )
            logger.warning(
                "⚠️  opencv-contrib-python: Contrib modules not available (using opencv-python)"
            )
    except ImportError:
        results["failed"].append("opencv-contrib-python: OpenCV not available")
        logger.error("❌ opencv-contrib-python: OpenCV not available")

    logger.info("")

    # ============================================================================
    # Integration Tests
    # ============================================================================
    logger.info("Testing Module Integrations...")
    logger.info("-" * 60)

    # Test audio_utils integrations
    try:
        from app.core.audio.audio_utils import (
            HAS_DEEPFILTERNET,
            HAS_PYRUBBERBAND,
            HAS_RESAMPY,
            HAS_VOICEFIXER,
            HAS_WEBRTCVAD,
        )

        if HAS_VOICEFIXER:
            results["passed"].append("audio_utils: VoiceFixer integration")
            logger.info("✅ audio_utils: VoiceFixer integration")
        else:
            results["skipped"].append("audio_utils: VoiceFixer not available")
            logger.warning("⚠️  audio_utils: VoiceFixer not available")

        if HAS_DEEPFILTERNET:
            results["passed"].append("audio_utils: DeepFilterNet integration")
            logger.info("✅ audio_utils: DeepFilterNet integration")
        else:
            results["skipped"].append("audio_utils: DeepFilterNet not available")
            logger.warning("⚠️  audio_utils: DeepFilterNet not available")

        if HAS_RESAMPY:
            results["passed"].append("audio_utils: resampy integration")
            logger.info("✅ audio_utils: resampy integration")
        else:
            results["skipped"].append("audio_utils: resampy not available")
            logger.warning("⚠️  audio_utils: resampy not available")

        if HAS_PYRUBBERBAND:
            results["passed"].append("audio_utils: pyrubberband integration")
            logger.info("✅ audio_utils: pyrubberband integration")
        else:
            results["skipped"].append("audio_utils: pyrubberband not available")
            logger.warning("⚠️  audio_utils: pyrubberband not available")

        if HAS_WEBRTCVAD:
            results["passed"].append("audio_utils: webrtcvad integration")
            logger.info("✅ audio_utils: webrtcvad integration")
        else:
            results["skipped"].append("audio_utils: webrtcvad not available")
            logger.warning("⚠️  audio_utils: webrtcvad not available")

    except Exception as e:
        results["failed"].append(f"audio_utils: Integration test failed - {e}")
        logger.error(f"❌ audio_utils: Integration test failed - {e}")

    # Test quality_metrics integrations
    try:
        from app.core.engines.quality_metrics import HAS_PESQ, HAS_PYSTOI

        if HAS_PESQ:
            results["passed"].append("quality_metrics: PESQ integration")
            logger.info("✅ quality_metrics: PESQ integration")
        else:
            results["skipped"].append("quality_metrics: PESQ not available")
            logger.warning("⚠️  quality_metrics: PESQ not available")

        if HAS_PYSTOI:
            results["passed"].append("quality_metrics: STOI integration")
            logger.info("✅ quality_metrics: STOI integration")
        else:
            results["skipped"].append("quality_metrics: STOI not available")
            logger.warning("⚠️  quality_metrics: STOI not available")

    except Exception as e:
        results["failed"].append(f"quality_metrics: Integration test failed - {e}")
        logger.error(f"❌ quality_metrics: Integration test failed - {e}")

    # Test post_fx integrations
    try:
        from app.core.audio.post_fx import HAS_PEDALBOARD

        if HAS_PEDALBOARD:
            results["passed"].append("post_fx: Pedalboard integration")
            logger.info("✅ post_fx: Pedalboard integration")
        else:
            results["skipped"].append("post_fx: Pedalboard not available")
            logger.warning("⚠️  post_fx: Pedalboard not available")

    except Exception as e:
        results["failed"].append(f"post_fx: Integration test failed - {e}")
        logger.error(f"❌ post_fx: Integration test failed - {e}")

    # Test training integrations
    try:
        from app.core.training.xtts_trainer import HAS_AUDIOMENTATIONS

        if HAS_AUDIOMENTATIONS:
            results["passed"].append("xtts_trainer: audiomentations integration")
            logger.info("✅ xtts_trainer: audiomentations integration")
        else:
            results["skipped"].append("xtts_trainer: audiomentations not available")
            logger.warning("⚠️  xtts_trainer: audiomentations not available")

    except Exception as e:
        results["failed"].append(f"xtts_trainer: Integration test failed - {e}")
        logger.error(f"❌ xtts_trainer: Integration test failed - {e}")

    logger.info("")

    # ============================================================================
    # Summary
    # ============================================================================
    logger.info("=" * 60)
    logger.info("Verification Summary")
    logger.info("=" * 60)
    logger.info(f"✅ Passed: {len(results['passed'])}")
    logger.info(f"❌ Failed: {len(results['failed'])}")
    logger.info(f"⚠️  Skipped: {len(results['skipped'])}")
    logger.info("")

    if results["failed"]:
        logger.error("Failed Tests:")
        for failure in results["failed"]:
            logger.error(f"  - {failure}")
        logger.info("")

    if results["skipped"]:
        logger.warning("Skipped Tests (optional dependencies):")
        for skipped in results["skipped"]:
            logger.warning(f"  - {skipped}")
        logger.info("")

    # Return exit code
    if results["failed"]:
        logger.error(
            "❌ Verification FAILED - Some libraries are not properly integrated"
        )
        return 1
    else:
        logger.info(
            "✅ Verification PASSED - All critical libraries are properly integrated"
        )
        return 0


if __name__ == "__main__":
    sys.exit(main())
