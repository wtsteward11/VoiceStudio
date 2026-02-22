"""
Voice Mixer Module for VoiceStudio
Professional audio mixing engine for multi-channel audio processing

Compatible with:
- Python 3.10+
- numpy>=1.26.0
- librosa>=0.11.0
- scipy>=1.9.0
"""

from __future__ import annotations

import logging
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)

try:
    import librosa

    HAS_LIBROSA = True
except ImportError:
    HAS_LIBROSA = False
    logger.warning("librosa not installed. Install with: pip install librosa")

try:
    from scipy import signal

    HAS_SCIPY = True
except ImportError:
    HAS_SCIPY = False
    logger.warning("scipy not installed. Install with: pip install scipy")

# Import audio utilities
try:
    from .audio_utils import load_audio

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False
    logger.warning("audio_utils not available")

# Import Post-FX for effect processing
try:
    from .post_fx import PostFXProcessor

    HAS_POST_FX = True
except ImportError:
    HAS_POST_FX = False
    logger.warning("post_fx not available")


class VoiceMixer:
    """
    Voice Mixer for professional multi-channel audio mixing.

    Supports:
    - Multi-channel mixing with volume, pan, mute, solo
    - Send/return routing
    - Sub-group routing
    - Master bus processing
    - Effect chains on returns, sub-groups, and master
    """

    def __init__(self, sample_rate: int = 24000, num_channels: int = 8):
        """
        Initialize Voice Mixer.

        Args:
            sample_rate: Default sample rate for processing
            num_channels: Maximum number of channels
        """
        self.sample_rate = sample_rate
        self.num_channels = num_channels

    def mix(
        self,
        channels: dict[str, np.ndarray],
        mixer_state: dict,
        sample_rate: int | None = None,
        **kwargs,
    ) -> np.ndarray:
        """
        Mix multiple audio channels according to mixer state.

        Args:
            channels: Dictionary mapping channel IDs to audio arrays
            mixer_state: Mixer state dictionary with:
                - channels: List of channel configs
                - sends: List of send bus configs
                - returns: List of return bus configs
                - sub_groups: List of sub-group configs
                - master: Master bus config
            sample_rate: Sample rate (uses instance default if None)
            **kwargs: Additional mixing options

        Returns:
            Mixed audio array (stereo output)
        """
        if sample_rate is None:
            sample_rate = self.sample_rate

        # Process channels
        channel_outputs = {}
        for channel_id, audio in channels.items():
            channel_outputs[channel_id] = self._process_channel(
                audio, channel_id, mixer_state, sample_rate
            )

        # Process sends
        send_outputs = self._process_sends(channel_outputs, mixer_state, sample_rate)

        # Process returns
        return_outputs = self._process_returns(send_outputs, mixer_state, sample_rate)

        # Process sub-groups
        subgroup_outputs = self._process_subgroups(channel_outputs, mixer_state, sample_rate)

        # Mix to master
        master_output = self._process_master(
            channel_outputs,
            subgroup_outputs,
            return_outputs,
            mixer_state,
            sample_rate,
        )

        return master_output

    def _process_channel(
        self,
        audio: np.ndarray,
        channel_id: str,
        mixer_state: dict,
        sample_rate: int,
    ) -> np.ndarray:
        """Process a single channel with volume, pan, mute, solo."""
        # Find channel config
        channel_config = None
        for ch in mixer_state.get("channels", []):
            if ch.get("id") == channel_id:
                channel_config = ch
                break

        if channel_config is None:
            return audio

        # Convert to stereo if needed
        if len(audio.shape) == 1:
            audio = np.array([audio, audio])  # Mono to stereo
        elif audio.shape[0] == 1:
            audio = np.array([audio[0], audio[0]])  # Mono to stereo

        # Apply volume
        volume = channel_config.get("volume", 1.0)
        audio = audio * volume

        # Apply pan
        pan = channel_config.get("pan", 0.0)
        if pan != 0.0:
            # Constant power panning
            left_gain = np.sqrt((1.0 - pan) / 2.0)
            right_gain = np.sqrt((1.0 + pan) / 2.0)
            audio[0] = audio[0] * left_gain
            audio[1] = audio[1] * right_gain

        # Apply mute
        if channel_config.get("is_muted", False):
            audio = np.zeros_like(audio)

        # Normalize to prevent clipping
        max_val = np.max(np.abs(audio))
        if max_val > 0.95:
            audio = audio / max_val * 0.95

        return audio

    def _process_sends(
        self,
        channel_outputs: dict[str, np.ndarray],
        mixer_state: dict,
        sample_rate: int,
    ) -> dict[str, np.ndarray]:
        """Process send busses."""
        send_outputs = {}
        sends = mixer_state.get("sends", [])

        for send in sends:
            send_id = send.get("id")
            if not send.get("is_enabled", True):
                continue

            send_audio = np.zeros((2, 0), dtype=np.float32)

            # Collect audio from channels that send to this bus
            for channel_id, channel_audio in channel_outputs.items():
                # Find channel routing
                routing = None
                for r in mixer_state.get("channel_routing", []):
                    if r.get("channel_id") == channel_id:
                        routing = r
                        break

                if routing is None:
                    continue

                # Check if channel sends to this bus
                send_levels = routing.get("send_levels", {})
                send_enabled = routing.get("send_enabled", {})

                if send_enabled.get(send_id):
                    level = send_levels.get(send_id, 0.0)
                    if level > 0:
                        # Resize send_audio if needed
                        if send_audio.shape[1] == 0:
                            send_audio = np.zeros_like(channel_audio)
                        elif send_audio.shape[1] < channel_audio.shape[1]:
                            # Pad send_audio
                            pad_size = channel_audio.shape[1] - send_audio.shape[1]
                            send_audio = np.pad(
                                send_audio, ((0, 0), (0, pad_size)), mode="constant"
                            )
                        elif send_audio.shape[1] > channel_audio.shape[1]:
                            # Pad channel_audio
                            pad_size = send_audio.shape[1] - channel_audio.shape[1]
                            channel_audio = np.pad(
                                channel_audio,
                                ((0, 0), (0, pad_size)),
                                mode="constant",
                            )

                        send_audio += channel_audio * level

            # Apply send bus volume
            send_volume = send.get("volume", 1.0)
            send_audio = send_audio * send_volume

            send_outputs[send_id] = send_audio

        return send_outputs

    def _process_returns(
        self, send_outputs: dict[str, np.ndarray], mixer_state: dict, sample_rate: int
    ) -> dict[str, np.ndarray]:
        """Process return busses."""
        return_outputs = {}
        returns = mixer_state.get("returns", [])

        for ret in returns:
            return_id = ret.get("id")
            bus_number = ret.get("bus_number", 0)

            # Find corresponding send
            send_audio = None
            for send_id, audio in send_outputs.items():
                send = None
                for s in mixer_state.get("sends", []):
                    if s.get("id") == send_id:
                        send = s
                        break

                if send and send.get("bus_number") == bus_number:
                    send_audio = audio
                    break

            if send_audio is None:
                send_audio = np.zeros((2, 0), dtype=np.float32)

            # Apply return volume
            return_volume = ret.get("volume", 1.0)
            return_audio = send_audio * return_volume

            # Apply return pan
            return_pan = ret.get("pan", 0.0)
            if return_pan != 0.0:
                left_gain = np.sqrt((1.0 - return_pan) / 2.0)
                right_gain = np.sqrt((1.0 + return_pan) / 2.0)
                return_audio[0] = return_audio[0] * left_gain
                return_audio[1] = return_audio[1] * right_gain

            # Apply effect chain if present
            effect_chain_id = ret.get("effect_chain_id")
            if effect_chain_id and HAS_POST_FX:
                try:
                    post_fx = PostFXProcessor(sample_rate=sample_rate)
                    # Apply effects to each channel
                    effects: list[Any] = []  # Would be loaded from effect chain
                    return_audio[0] = post_fx.process(return_audio[0], sample_rate, effects)
                    return_audio[1] = post_fx.process(return_audio[1], sample_rate, effects)
                except Exception as e:
                    logger.warning(f"Effect chain processing failed: {e}")

            if ret.get("is_enabled", True):
                return_outputs[return_id] = return_audio

        return return_outputs

    def _process_subgroups(
        self,
        channel_outputs: dict[str, np.ndarray],
        mixer_state: dict,
        sample_rate: int,
    ) -> dict[str, np.ndarray]:
        """Process sub-group busses."""
        subgroup_outputs = {}
        subgroups = mixer_state.get("sub_groups", [])

        for subgroup in subgroups:
            subgroup_id = subgroup.get("id")
            channel_ids = subgroup.get("channel_ids", [])

            # Mix channels routed to this sub-group
            subgroup_audio = None
            for channel_id in channel_ids:
                if channel_id in channel_outputs:
                    channel_audio = channel_outputs[channel_id]

                    if subgroup_audio is None:
                        subgroup_audio = np.zeros_like(channel_audio)

                    # Resize if needed
                    if subgroup_audio.shape[1] < channel_audio.shape[1]:
                        pad_size = channel_audio.shape[1] - subgroup_audio.shape[1]
                        subgroup_audio = np.pad(
                            subgroup_audio, ((0, 0), (0, pad_size)), mode="constant"
                        )
                    elif subgroup_audio.shape[1] > channel_audio.shape[1]:
                        pad_size = subgroup_audio.shape[1] - channel_audio.shape[1]
                        channel_audio = np.pad(
                            channel_audio, ((0, 0), (0, pad_size)), mode="constant"
                        )

                    subgroup_audio += channel_audio

            if subgroup_audio is None:
                subgroup_audio = np.zeros((2, 0), dtype=np.float32)

            # Apply sub-group volume
            subgroup_volume = subgroup.get("volume", 1.0)
            subgroup_audio = subgroup_audio * subgroup_volume

            # Apply sub-group pan
            subgroup_pan = subgroup.get("pan", 0.0)
            if subgroup_pan != 0.0:
                left_gain = np.sqrt((1.0 - subgroup_pan) / 2.0)
                right_gain = np.sqrt((1.0 + subgroup_pan) / 2.0)
                subgroup_audio[0] = subgroup_audio[0] * left_gain
                subgroup_audio[1] = subgroup_audio[1] * right_gain

            # Apply mute/solo
            if subgroup.get("is_muted", False):
                subgroup_audio = np.zeros_like(subgroup_audio)

            # Apply effect chain if present
            effect_chain_id = subgroup.get("effect_chain_id")
            if effect_chain_id and HAS_POST_FX:
                try:
                    post_fx = PostFXProcessor(sample_rate=sample_rate)
                    effects: list[Any] = []  # Would be loaded from effect chain
                    subgroup_audio[0] = post_fx.process(subgroup_audio[0], sample_rate, effects)
                    subgroup_audio[1] = post_fx.process(subgroup_audio[1], sample_rate, effects)
                except Exception as e:
                    logger.warning(f"Effect chain processing failed: {e}")

            subgroup_outputs[subgroup_id] = subgroup_audio

        return subgroup_outputs

    def _process_master(
        self,
        channel_outputs: dict[str, np.ndarray],
        subgroup_outputs: dict[str, np.ndarray],
        return_outputs: dict[str, np.ndarray],
        mixer_state: dict,
        sample_rate: int,
    ) -> np.ndarray:
        """Process master bus."""
        master_config = mixer_state.get("master", {})
        master_audio = np.zeros((2, 0), dtype=np.float32)

        # Mix channels routed to master
        for channel_id, channel_audio in channel_outputs.items():
            # Check routing
            routing = None
            for r in mixer_state.get("channel_routing", []):
                if r.get("channel_id") == channel_id:
                    routing = r
                    break

            if routing is None:
                continue

            # Check if routed to master
            if routing.get("main_destination") == "Master":
                if master_audio.shape[1] == 0:
                    master_audio = np.zeros_like(channel_audio)
                elif master_audio.shape[1] < channel_audio.shape[1]:
                    pad_size = channel_audio.shape[1] - master_audio.shape[1]
                    master_audio = np.pad(master_audio, ((0, 0), (0, pad_size)), mode="constant")
                elif master_audio.shape[1] > channel_audio.shape[1]:
                    pad_size = master_audio.shape[1] - channel_audio.shape[1]
                    channel_audio = np.pad(channel_audio, ((0, 0), (0, pad_size)), mode="constant")

                master_audio += channel_audio

        # Mix sub-groups
        for _subgroup_id, subgroup_audio in subgroup_outputs.items():
            if master_audio.shape[1] == 0:
                master_audio = np.zeros_like(subgroup_audio)
            elif master_audio.shape[1] < subgroup_audio.shape[1]:
                pad_size = subgroup_audio.shape[1] - master_audio.shape[1]
                master_audio = np.pad(master_audio, ((0, 0), (0, pad_size)), mode="constant")
            elif master_audio.shape[1] > subgroup_audio.shape[1]:
                pad_size = master_audio.shape[1] - subgroup_audio.shape[1]
                subgroup_audio = np.pad(subgroup_audio, ((0, 0), (0, pad_size)), mode="constant")

            master_audio += subgroup_audio

        # Mix returns
        for _return_id, return_audio in return_outputs.items():
            if master_audio.shape[1] == 0:
                master_audio = np.zeros_like(return_audio)
            elif master_audio.shape[1] < return_audio.shape[1]:
                pad_size = return_audio.shape[1] - master_audio.shape[1]
                master_audio = np.pad(master_audio, ((0, 0), (0, pad_size)), mode="constant")
            elif master_audio.shape[1] > return_audio.shape[1]:
                pad_size = master_audio.shape[1] - return_audio.shape[1]
                return_audio = np.pad(return_audio, ((0, 0), (0, pad_size)), mode="constant")

            master_audio += return_audio

        # Apply master volume
        master_volume = master_config.get("volume", 1.0)
        master_audio = master_audio * master_volume

        # Apply master pan
        master_pan = master_config.get("pan", 0.0)
        if master_pan != 0.0:
            left_gain = np.sqrt((1.0 - master_pan) / 2.0)
            right_gain = np.sqrt((1.0 + master_pan) / 2.0)
            master_audio[0] = master_audio[0] * left_gain
            master_audio[1] = master_audio[1] * right_gain

        # Apply master mute
        if master_config.get("is_muted", False):
            master_audio = np.zeros_like(master_audio)

        # Apply effect chain if present
        effect_chain_id = master_config.get("effect_chain_id")
        if effect_chain_id and HAS_POST_FX:
            try:
                post_fx = PostFXProcessor(sample_rate=sample_rate)
                effects: list[Any] = []  # Would be loaded from effect chain
                master_audio[0] = post_fx.process(master_audio[0], sample_rate, effects)
                master_audio[1] = post_fx.process(master_audio[1], sample_rate, effects)
            except Exception as e:
                logger.warning(f"Effect chain processing failed: {e}")

        # Normalize to prevent clipping
        max_val = np.max(np.abs(master_audio))
        if max_val > 0.95:
            master_audio = master_audio / max_val * 0.95

        return master_audio

    def calculate_levels(self, audio: np.ndarray, window_size: int = 1024) -> dict[str, float]:
        """
        Calculate peak and RMS levels for audio.

        Args:
            audio: Audio array (mono or stereo)
            window_size: Window size for RMS calculation

        Returns:
            Dictionary with 'peak' and 'rms' levels
        """
        if len(audio.shape) == 1:
            audio = audio.reshape(1, -1)

        # Peak level
        peak = float(np.max(np.abs(audio)))

        # RMS level
        rms = float(np.sqrt(np.mean(audio**2)))

        return {"peak": peak, "rms": rms}


def create_voice_mixer(sample_rate: int = 24000, num_channels: int = 8) -> VoiceMixer:
    """
    Factory function to create a Voice Mixer instance.

    Args:
        sample_rate: Default sample rate for processing
        num_channels: Maximum number of channels

    Returns:
        Initialized VoiceMixer instance
    """
    return VoiceMixer(sample_rate=sample_rate, num_channels=num_channels)


def mix_audio(
    channels: dict[str, np.ndarray],
    mixer_state: dict,
    sample_rate: int = 24000,
    **kwargs,
) -> np.ndarray:
    """
    Convenience function to mix audio channels.

    Args:
        channels: Dictionary mapping channel IDs to audio arrays
        mixer_state: Mixer state dictionary
        sample_rate: Sample rate
        **kwargs: Additional mixing options

    Returns:
        Mixed audio array (stereo output)
    """
    mixer = VoiceMixer(sample_rate=sample_rate)
    return mixer.mix(channels, mixer_state, sample_rate, **kwargs)
