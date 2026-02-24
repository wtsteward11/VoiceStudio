"""
Auto Trainer Module for VoiceStudio
Automatic training with parameter optimization

Compatible with:
- Python 3.10+
- torch>=2.0.0
"""

from __future__ import annotations

import logging
from collections.abc import Callable
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

# Import unified trainer
try:
    from .unified_trainer import UnifiedTrainer

    HAS_UNIFIED_TRAINER = True
except ImportError:
    HAS_UNIFIED_TRAINER = False
    logger.warning("Unified trainer not available")

# Import quality metrics
try:
    from app.core.audio.enhanced_quality_metrics import EnhancedQualityMetrics

    HAS_QUALITY_METRICS = True
except ImportError:
    HAS_QUALITY_METRICS = False
    logger.warning("Enhanced quality metrics not available")


class AutoTrainer:
    """
    Auto Trainer for automatic training with parameter optimization.

    Supports:
    - Automatic parameter optimization
    - Hyperparameter tuning
    - Quality-based training selection
    - Adaptive training strategies
    - Multi-run training with best model selection
    """

    def __init__(
        self,
        engine: str = "xtts",
        device: str | None = None,
        gpu: bool = True,
        output_dir: str | None = None,
    ):
        """
        Initialize Auto Trainer.

        Args:
            engine: Training engine name
            device: Device to use
            gpu: Whether to use GPU
            output_dir: Output directory
        """
        self.engine = engine
        self.device = device
        self.gpu = gpu
        self.output_dir = Path(output_dir) if output_dir else Path("models/auto_trained")
        self.output_dir.mkdir(parents=True, exist_ok=True)

        self.quality_metrics = None
        if HAS_QUALITY_METRICS:
            try:
                self.quality_metrics = EnhancedQualityMetrics()
            except Exception as e:
                logger.warning(f"Failed to initialize quality metrics: {e}")

    async def auto_train(
        self,
        metadata_path: str,
        validation_audio: str | None = None,
        max_runs: int = 3,
        optimize_params: bool = True,
        progress_callback: Callable[[dict], None] | None = None,
    ) -> dict[str, Any]:
        """
        Automatically train with parameter optimization.

        Args:
            metadata_path: Path to dataset metadata
            validation_audio: Optional validation audio for quality assessment
            max_runs: Maximum number of training runs
            optimize_params: Whether to optimize hyperparameters
            progress_callback: Optional progress callback

        Returns:
            Dictionary with training results and best model path
        """
        if not HAS_UNIFIED_TRAINER:
            raise RuntimeError("Unified trainer not available")

        best_model = None
        best_quality = -1.0
        best_params = None
        training_history = []

        # Generate parameter sets to try
        if optimize_params:
            param_sets = self._generate_parameter_sets(max_runs)
        else:
            # Use default parameters
            param_sets = [
                {
                    "epochs": 100,
                    "batch_size": 4,
                    "learning_rate": 0.0001,
                }
            ]

        for run_idx, params in enumerate(param_sets):
            if progress_callback:
                progress_callback(
                    {
                        "run": run_idx + 1,
                        "total_runs": len(param_sets),
                        "status": "starting",
                        "params": params,
                    }
                )

            logger.info(f"Auto training run {run_idx + 1}/{len(param_sets)} with params: {params}")

            # Create trainer for this run
            trainer = UnifiedTrainer(
                engine=self.engine,
                device=self.device,
                gpu=self.gpu,
                output_dir=str(self.output_dir / f"run_{run_idx + 1}"),
            )

            try:
                # Initialize model
                if not trainer.initialize_model():
                    logger.warning(f"Model initialization failed for run {run_idx + 1}")
                    continue

                # Train
                def run_progress_callback(update):
                    if progress_callback:
                        progress_callback(
                            {
                                "run": run_idx + 1,
                                "total_runs": len(param_sets),
                                "status": "training",
                                "params": params,
                                "training_progress": update,
                            }
                        )

                result = await trainer.train(
                    metadata_path=metadata_path,
                    epochs=params["epochs"],
                    batch_size=params["batch_size"],
                    learning_rate=params["learning_rate"],
                    progress_callback=run_progress_callback,
                )

                # Evaluate quality if validation audio provided
                quality_score = 0.0
                if validation_audio and self.quality_metrics:
                    try:
                        # Export model
                        model_path = trainer.export_model()

                        # Load trained model and synthesize test audio
                        try:
                            import numpy as np
                            import soundfile as sf

                            from app.core.engines.quality_metrics import (
                                calculate_mos_score,
                                calculate_similarity,
                            )
                            from app.core.engines.xtts_engine import XTTSEngine

                            # Load validation audio
                            _val_audio, val_sr = sf.read(validation_audio)

                            # Create engine with trained model
                            test_engine = XTTSEngine(
                                model_name=model_path, device=self.device, gpu=self.gpu
                            )
                            if not test_engine.initialize():
                                raise RuntimeError("Failed to initialize test engine")

                            # Synthesize test audio using a standard test sentence
                            test_text = "The quick brown fox jumps over the lazy dog."
                            synthesized_audio = test_engine.synthesize(
                                text=test_text, speaker_wav=validation_audio, language="en"
                            )

                            if synthesized_audio is not None:
                                if not isinstance(synthesized_audio, np.ndarray):
                                    raise RuntimeError("Synthesis returned unexpected type")

                                similarity = calculate_similarity(
                                    reference_audio=validation_audio,
                                    generated_audio=synthesized_audio,
                                    method="embedding",
                                )

                                mos_score = calculate_mos_score(synthesized_audio)

                                if similarity is not None and mos_score is not None:
                                    quality_score = (similarity * 0.6) + ((mos_score / 5.0) * 0.4)
                                    quality_score = max(0.0, min(1.0, quality_score))

                                    logger.info(
                                        f"Quality evaluation: similarity={similarity:.3f}, MOS={mos_score:.2f}, combined={quality_score:.3f}"
                                    )
                                else:
                                    logger.warning(
                                        "Quality metrics returned None, using loss-based score"
                                    )
                            else:
                                logger.warning("Synthesis failed, using loss-based quality score")
                                raise RuntimeError("Synthesis failed")

                        except ImportError as e:
                            logger.warning(
                                f"Quality metrics not available: {e}, using loss-based score"
                            )
                            raise
                        except Exception as e:
                            logger.warning(
                                f"Quality evaluation failed: {e}, using loss-based score"
                            )
                            raise

                    except Exception as e:
                        logger.warning(
                            f"Quality evaluation failed: {e}, falling back to loss-based score"
                        )

                # Use loss as quality indicator if no validation
                if quality_score == 0.0 and result.get("final_loss"):
                    # Lower loss = better quality
                    quality_score = max(0.0, 1.0 - result["final_loss"])

                training_history.append(
                    {
                        "run": run_idx + 1,
                        "params": params,
                        "result": result,
                        "quality_score": quality_score,
                    }
                )

                # Track best model
                if quality_score > best_quality:
                    best_quality = quality_score
                    best_model = trainer.export_model()
                    best_params = params

                if progress_callback:
                    progress_callback(
                        {
                            "run": run_idx + 1,
                            "total_runs": len(param_sets),
                            "status": "completed",
                            "params": params,
                            "quality_score": quality_score,
                        }
                    )

            except Exception as e:
                logger.error(f"Training run {run_idx + 1} failed: {e}")
                training_history.append(
                    {
                        "run": run_idx + 1,
                        "params": params,
                        "error": str(e),
                    }
                )

        return {
            "best_model_path": best_model,
            "best_quality": best_quality,
            "best_params": best_params,
            "training_history": training_history,
            "total_runs": len(param_sets),
            "successful_runs": len([h for h in training_history if "error" not in h]),
        }

    def _generate_parameter_sets(self, num_sets: int) -> list[dict[str, Any]]:
        """
        Generate parameter sets for hyperparameter optimization.

        Args:
            num_sets: Number of parameter sets to generate

        Returns:
            List of parameter dictionaries
        """
        param_sets = []

        # Define parameter ranges
        epochs_options = [50, 100, 150, 200]
        batch_size_options = [2, 4, 8, 16]
        learning_rate_options = [0.00001, 0.0001, 0.0005, 0.001]

        # Generate combinations
        for i in range(
            min(
                num_sets, len(epochs_options) * len(batch_size_options) * len(learning_rate_options)
            )
        ):
            epochs_idx = i % len(epochs_options)
            batch_idx = (i // len(epochs_options)) % len(batch_size_options)
            lr_idx = (i // (len(epochs_options) * len(batch_size_options))) % len(
                learning_rate_options
            )

            param_sets.append(
                {
                    "epochs": epochs_options[epochs_idx],
                    "batch_size": batch_size_options[batch_idx],
                    "learning_rate": learning_rate_options[lr_idx],
                }
            )

        return param_sets[:num_sets]

    def get_recommended_params(
        self,
        dataset_size: int,
        audio_duration: float,
        quality_target: str = "standard",
    ) -> dict[str, Any]:
        """
        Get recommended training parameters based on dataset characteristics.

        Args:
            dataset_size: Number of audio files in dataset
            audio_duration: Average audio duration in seconds
            quality_target: Quality target ("fast", "standard", "high", "ultra")

        Returns:
            Dictionary with recommended parameters
        """
        # Calculate total training data duration
        dataset_size * audio_duration

        # Base parameters
        base_epochs = 100
        base_batch_size = 4
        base_lr = 0.0001

        # Adjust based on dataset size
        if dataset_size < 10:
            epochs = int(base_epochs * 1.5)  # More epochs for small datasets
            batch_size = max(2, base_batch_size // 2)
        elif dataset_size < 50:
            epochs = base_epochs
            batch_size = base_batch_size
        else:
            epochs = int(base_epochs * 0.8)  # Fewer epochs for large datasets
            batch_size = min(16, base_batch_size * 2)

        # Adjust based on quality target
        if quality_target == "fast":
            epochs = int(epochs * 0.5)
            learning_rate = base_lr * 2.0  # Higher LR for faster training
        elif quality_target == "high":
            epochs = int(epochs * 1.5)
            learning_rate = base_lr * 0.5  # Lower LR for better quality
        elif quality_target == "ultra":
            epochs = int(epochs * 2.0)
            learning_rate = base_lr * 0.25  # Very low LR for maximum quality
        else:  # standard
            learning_rate = base_lr

        return {
            "epochs": epochs,
            "batch_size": batch_size,
            "learning_rate": learning_rate,
            "quality_target": quality_target,
        }


def create_auto_trainer(
    engine: str = "xtts",
    device: str | None = None,
    gpu: bool = True,
    output_dir: str | None = None,
) -> AutoTrainer:
    """
    Factory function to create an Auto Trainer instance.

    Args:
        engine: Training engine name
        device: Device to use
        gpu: Whether to use GPU
        output_dir: Output directory

    Returns:
        Initialized AutoTrainer instance
    """
    return AutoTrainer(engine=engine, device=device, gpu=gpu, output_dir=output_dir)
