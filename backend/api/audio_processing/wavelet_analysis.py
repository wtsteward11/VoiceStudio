"""
Wavelet Analysis Integration
Integrates pywavelets library for wavelet transforms and analysis.
"""

from __future__ import annotations

import logging
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)

# Try importing pywavelets
HAS_PYWAVELETS = False
try:
    import pywt

    HAS_PYWAVELETS = True
except ImportError:
    logger.warning("pywavelets not available. Wavelet analysis unavailable.")


class WaveletAnalyzer:
    """
    Wavelet analysis using pywavelets library.
    """

    def __init__(self):
        """Initialize wavelet analyzer."""
        self.pywavelets_available = HAS_PYWAVELETS

    def decompose(
        self,
        signal: np.ndarray,
        wavelet: str = "db4",
        mode: str = "symmetric",
        level: int | None = None,
    ) -> tuple[list[np.ndarray], np.ndarray]:
        """
        Decompose signal using wavelet transform.

        Args:
            signal: Input signal
            wavelet: Wavelet name (e.g., 'db4', 'haar', 'coif2')
            mode: Signal extension mode
            level: Decomposition level (auto if None)

        Returns:
            Tuple of (coefficients list, approximation array)
        """
        if not self.pywavelets_available:
            raise ImportError("pywavelets library not available")

        try:
            if level is None:
                level = pywt.dwt_max_level(len(signal), wavelet)

            coeffs = pywt.wavedec(signal, wavelet, mode=mode, level=level)
            return coeffs[1:], coeffs[0]  # Details, Approximation
        except Exception as e:
            logger.error(f"Error in wavelet decomposition: {e}", exc_info=True)
            raise

    def reconstruct(
        self,
        coeffs: list[np.ndarray],
        approximation: np.ndarray,
        wavelet: str = "db4",
        mode: str = "symmetric",
    ) -> np.ndarray:
        """
        Reconstruct signal from wavelet coefficients.

        Args:
            coeffs: Detail coefficients list
            approximation: Approximation coefficients
            wavelet: Wavelet name
            mode: Signal extension mode

        Returns:
            Reconstructed signal
        """
        if not self.pywavelets_available:
            raise ImportError("pywavelets library not available")

        try:
            # Combine approximation and details
            all_coeffs = [approximation, *coeffs]
            reconstructed: np.ndarray = np.asarray(pywt.waverec(all_coeffs, wavelet, mode=mode))
            return reconstructed
        except Exception as e:
            logger.error(f"Error in wavelet reconstruction: {e}", exc_info=True)
            raise

    def get_wavelet_features(
        self,
        signal: np.ndarray,
        wavelet: str = "db4",
        level: int | None = None,
    ) -> dict:
        """
        Extract features from wavelet decomposition.

        Args:
            signal: Input signal
            wavelet: Wavelet name
            level: Decomposition level

        Returns:
            Dictionary of wavelet features
        """
        if not self.pywavelets_available:
            raise ImportError("pywavelets library not available")

        try:
            coeffs, approx = self.decompose(signal, wavelet, level=level)

            energy_approx = float(np.sum(approx**2))
            total_energy = float(np.sum(signal**2))

            features: dict[str, Any] = {
                "num_levels": len(coeffs),
                "energy_approximation": energy_approx,
                "energy_details": [float(np.sum(c**2)) for c in coeffs],
                "total_energy": total_energy,
            }
            features["energy_ratio"] = energy_approx / total_energy if total_energy > 0 else 0.0

            return features
        except Exception as e:
            logger.error(f"Error extracting wavelet features: {e}", exc_info=True)
            raise

    def get_available_wavelets(self) -> list[str]:
        """
        Get list of available wavelets.

        Returns:
            List of wavelet names
        """
        if not self.pywavelets_available:
            return []

        try:
            return list(pywt.wavelist())
        except Exception:
            return []
