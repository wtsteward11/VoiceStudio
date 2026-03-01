#!/usr/bin/env python3
"""
Generate Synthetic Sentinel Audio Fixture

Creates a deterministic, synthetic audio file for use as the sentinel
workflow test fixture. The audio is a synthesized tone sequence that
mimics speech-like patterns without containing any actual voice data.

Output: fixtures/audio/sentinel_16k_mono.wav
Format: 16kHz, 16-bit, mono WAV

Usage:
    python scripts/generate_sentinel_fixture.py
"""

import math
import struct
import wave
from pathlib import Path


def generate_envelope(t: float, duration: float) -> float:
    """Generate a smooth attack-sustain-release envelope."""
    attack = 0.1
    release = 0.3

    if t < attack:
        return t / attack
    elif t > duration - release:
        return (duration - t) / release
    else:
        return 1.0


def generate_tone(
    frequency: float,
    duration: float,
    sample_rate: int = 16000,
    amplitude: float = 0.5,
) -> list[int]:
    """Generate a single tone with envelope."""
    samples = []
    n_samples = int(sample_rate * duration)

    for i in range(n_samples):
        t = i / sample_rate
        envelope = generate_envelope(t, duration)
        # Add slight frequency modulation for more natural sound
        freq_mod = 1 + 0.02 * math.sin(2 * math.pi * 5 * t)
        value = amplitude * envelope * math.sin(2 * math.pi * frequency * freq_mod * t)
        samples.append(int(32767 * value))

    return samples


def generate_silence(duration: float, sample_rate: int = 16000) -> list[int]:
    """Generate silence."""
    return [0] * int(sample_rate * duration)


def generate_speech_like_audio(sample_rate: int = 16000) -> list[int]:
    """
    Generate audio that mimics speech patterns without actual speech.

    Creates a sequence of tones at speech-like frequencies with varying
    durations and pauses, similar to the rhythm of spoken language.
    """
    samples = []

    # Speech-like frequency range (fundamental frequency)
    # Male: 85-180 Hz, Female: 165-255 Hz, Children: 250-400 Hz
    # We'll use a middle range
    base_frequencies = [150, 180, 165, 200, 175, 190, 160, 185]

    # Speech-like syllable pattern (duration in seconds)
    syllable_pattern = [
        (0.15, True),   # Short syllable
        (0.08, False),  # Short pause
        (0.25, True),   # Long syllable
        (0.10, False),  # Pause
        (0.12, True),   # Short syllable
        (0.15, True),   # Short syllable
        (0.20, False),  # Longer pause (word boundary)
        (0.20, True),   # Medium syllable
        (0.08, False),  # Short pause
        (0.18, True),   # Medium syllable
        (0.12, True),   # Short syllable
        (0.30, False),  # Sentence pause
        (0.22, True),   # Medium syllable
        (0.10, False),  # Short pause
        (0.15, True),   # Short syllable
        (0.08, False),  # Short pause
        (0.30, True),   # Long syllable (emphasis)
        (0.50, False),  # Long pause (end of phrase)
        (0.18, True),   # Medium syllable
        (0.12, True),   # Short syllable
        (0.08, False),  # Short pause
        (0.25, True),   # Medium-long syllable
    ]

    freq_idx = 0
    for duration, is_tone in syllable_pattern:
        if is_tone:
            # Add harmonic richness
            freq = base_frequencies[freq_idx % len(base_frequencies)]
            tone = generate_tone(freq, duration, sample_rate, amplitude=0.4)

            # Add first harmonic at lower amplitude
            harmonic = generate_tone(freq * 2, duration, sample_rate, amplitude=0.15)

            # Combine
            combined = [t + h for t, h in zip(tone, harmonic, strict=False)]
            samples.extend(combined)

            freq_idx += 1
        else:
            samples.extend(generate_silence(duration, sample_rate))

    # Pad to exactly 5 seconds
    target_samples = 5 * sample_rate
    if len(samples) < target_samples:
        samples.extend(generate_silence((target_samples - len(samples)) / sample_rate, sample_rate))
    elif len(samples) > target_samples:
        samples = samples[:target_samples]

    return samples


def write_wav(samples: list[int], filepath: Path, sample_rate: int = 16000) -> None:
    """Write samples to a WAV file."""
    filepath.parent.mkdir(parents=True, exist_ok=True)

    with wave.open(str(filepath), 'w') as wav:
        wav.setnchannels(1)  # Mono
        wav.setsampwidth(2)  # 16-bit
        wav.setframerate(sample_rate)

        for sample in samples:
            # Clamp to valid range
            sample = max(-32768, min(32767, sample))
            wav.writeframes(struct.pack('<h', sample))

    print(f"Generated: {filepath}")
    print(f"  Sample rate: {sample_rate} Hz")
    print("  Channels: 1 (mono)")
    print("  Bit depth: 16-bit")
    print(f"  Duration: {len(samples) / sample_rate:.2f} seconds")
    print(f"  File size: {filepath.stat().st_size} bytes")


def main():
    """Generate the sentinel audio fixture."""
    output_path = Path("fixtures/audio/sentinel_16k_mono.wav")
    sample_rate = 16000

    print("Generating sentinel audio fixture...")
    print("=" * 50)

    # Generate speech-like audio
    samples = generate_speech_like_audio(sample_rate)

    # Write to file
    write_wav(samples, output_path, sample_rate)

    print("=" * 50)
    print("Done! Fixture ready for sentinel workflow.")


if __name__ == "__main__":
    main()
