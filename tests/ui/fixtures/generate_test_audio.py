"""
Generate test audio files for VoiceStudio UI tests.

This script creates standardized WAV audio files used for testing:
- Canonical audio files (matching manifest.json) for consistent testing
- Synthetic fallback audio when real canonical audio is unavailable

Files generated at canonical paths:
- tests/assets/canonical/standard/allan_watts.wav (synthetic long audio)
- tests/assets/canonical/standard/allan_watts_15s.wav (synthetic 15s segment)
- tests/ui/fixtures/test_audio_short.wav (5s) - Quick smoke tests
- tests/ui/fixtures/test_audio_long.wav (30s) - Extended tests
- tests/ui/fixtures/voice_reference_clean.wav (10s) - Cloning tests

Run this script to regenerate fixtures if they don't exist or are corrupted.
Usage: python -m tests.ui.fixtures.generate_test_audio [--canonical] [--all]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import wave
from pathlib import Path


def generate_sine_wave(
    frequency: float, duration: float, sample_rate: int = 44100, amplitude: float = 0.5
) -> bytes:
    """Generate a sine wave as raw bytes."""
    num_samples = int(sample_rate * duration)
    samples = []
    for i in range(num_samples):
        t = i / sample_rate
        value = amplitude * math.sin(2 * math.pi * frequency * t)
        # Convert to 16-bit signed integer
        sample = int(value * 32767)
        samples.append(struct.pack("<h", sample))
    return b"".join(samples)


def generate_modulated_tone(duration: float, sample_rate: int = 44100) -> bytes:
    """Generate a speech-like modulated tone."""
    num_samples = int(sample_rate * duration)
    samples = []

    base_freq = 150  # Base frequency (typical speech fundamental)

    for i in range(num_samples):
        t = i / sample_rate

        # Modulate frequency to simulate speech patterns
        freq_mod = 1 + 0.2 * math.sin(2 * math.pi * 2 * t)  # Slow modulation
        freq = base_freq * freq_mod

        # Amplitude envelope with pauses
        envelope = 0.5
        # Add periodic "pauses" to simulate word gaps
        word_period = t % 0.8
        if word_period > 0.7:
            envelope *= (0.8 - word_period) / 0.1  # Fade out
        elif word_period < 0.1:
            envelope *= word_period / 0.1  # Fade in

        # Generate harmonics for richer sound
        value = 0.6 * math.sin(2 * math.pi * freq * t)  # Fundamental
        value += 0.25 * math.sin(2 * math.pi * freq * 2 * t)  # 1st harmonic
        value += 0.1 * math.sin(2 * math.pi * freq * 3 * t)  # 2nd harmonic

        value *= envelope * 0.5  # Scale down
        sample = int(max(-32767, min(32767, value * 32767)))
        samples.append(struct.pack("<h", sample))

    return b"".join(samples)


def generate_noise(duration: float, sample_rate: int = 44100, amplitude: float = 0.1) -> bytes:
    """Generate white noise."""
    import random

    num_samples = int(sample_rate * duration)
    samples = []
    for _ in range(num_samples):
        value = (random.random() * 2 - 1) * amplitude
        sample = int(value * 32767)
        samples.append(struct.pack("<h", sample))
    return b"".join(samples)


def add_noise_to_signal(signal: bytes, noise_level: float = 0.1) -> bytes:
    """Add noise to an existing signal."""
    import random

    samples = []
    for i in range(0, len(signal), 2):
        original = struct.unpack("<h", signal[i : i + 2])[0]
        noise = (random.random() * 2 - 1) * noise_level * 32767
        combined = int(max(-32767, min(32767, original + noise)))
        samples.append(struct.pack("<h", combined))
    return b"".join(samples)


def write_wav(
    filepath: Path,
    audio_data: bytes,
    sample_rate: int = 44100,
    channels: int = 1,
    sample_width: int = 2,
):
    """Write audio data to a WAV file."""
    filepath.parent.mkdir(parents=True, exist_ok=True)

    with wave.open(str(filepath), "wb") as wav_file:
        wav_file.setnchannels(channels)
        wav_file.setsampwidth(sample_width)
        wav_file.setframerate(sample_rate)
        wav_file.writeframes(audio_data)


def get_canonical_dir() -> Path:
    """Get the canonical audio directory path."""
    # Navigate from tests/ui/fixtures/ to tests/assets/canonical/
    return Path(__file__).parent.parent.parent / "assets" / "canonical"


def compute_sha256(filepath: Path) -> str:
    """Compute SHA256 hash of a file."""
    sha256 = hashlib.sha256()
    with open(filepath, "rb") as f:
        for chunk in iter(lambda: f.read(8192), b""):
            sha256.update(chunk)
    return sha256.hexdigest().upper()


def generate_canonical_audio(force: bool = False) -> dict[str, Path]:
    """
    Generate synthetic canonical audio files matching manifest.json paths.

    These are synthetic fallbacks when real canonical audio is unavailable.
    The files will NOT match the manifest checksums but will be functional
    for testing audio workflows.

    Args:
        force: If True, regenerate even if files exist

    Returns:
        Dictionary mapping canonical names to generated file paths
    """
    canonical_dir = get_canonical_dir()
    standard_dir = canonical_dir / "standard"
    standard_dir.mkdir(parents=True, exist_ok=True)

    # Match manifest.json sample rate for compatibility
    sample_rate = 22050
    generated = {}

    print("Generating synthetic canonical audio files...")

    # Generate allan_watts.wav (full-length synthetic speech-like audio)
    # ~30 seconds is reasonable for testing without massive file sizes
    full_path = standard_dir / "allan_watts.wav"
    if force or not full_path.exists():
        print(f"  Creating {full_path.name} (30s synthetic speech)...")
        full_audio = generate_modulated_tone(30.0, sample_rate)
        write_wav(full_path, full_audio, sample_rate)
        generated["wav_full"] = full_path
    else:
        print(f"  {full_path.name} already exists, skipping")
        generated["wav_full"] = full_path

    # Generate allan_watts_15s.wav (segment for quick tests)
    segment_path = standard_dir / "allan_watts_15s.wav"
    if force or not segment_path.exists():
        print(f"  Creating {segment_path.name} (15s segment)...")
        segment_audio = generate_modulated_tone(15.0, sample_rate)
        write_wav(segment_path, segment_audio, sample_rate)
        generated["wav_segment"] = segment_path
    else:
        print(f"  {segment_path.name} already exists, skipping")
        generated["wav_segment"] = segment_path

    # Write a marker file indicating these are synthetic
    marker_path = standard_dir / ".synthetic_marker"
    marker_path.write_text(
        "These audio files were synthetically generated for testing.\n"
        "They are NOT the real canonical audio and checksums will NOT match manifest.json.\n"
        "For production testing, obtain the real audio files or use Git LFS.\n"
    )

    print(f"Done! Generated {len(generated)} canonical audio files.")

    # Show generated files with sizes and hashes
    print("\nGenerated canonical files:")
    for name, filepath in generated.items():
        size_kb = filepath.stat().st_size / 1024
        sha = compute_sha256(filepath)[:16] + "..."  # Truncated for display
        print(f"  - {filepath.name}: {size_kb:.1f} KB (sha256: {sha})")

    return generated


def generate_ui_fixtures(force: bool = False) -> dict[str, Path]:
    """Generate UI test fixture audio files."""
    fixtures_dir = Path(__file__).parent
    sample_rate = 44100
    generated = {}

    print("Generating UI test audio fixtures...")

    # 1. Short test audio (5 seconds) - 440Hz sine wave
    short_path = fixtures_dir / "test_audio_short.wav"
    if force or not short_path.exists():
        print("  Creating test_audio_short.wav (5s)...")
        short_audio = generate_sine_wave(440, 5.0, sample_rate)
        write_wav(short_path, short_audio, sample_rate)
        generated["short"] = short_path

    # 2. Long test audio (30 seconds) - Modulated speech-like tone
    long_path = fixtures_dir / "test_audio_long.wav"
    if force or not long_path.exists():
        print("  Creating test_audio_long.wav (30s)...")
        long_audio = generate_modulated_tone(30.0, sample_rate)
        write_wav(long_path, long_audio, sample_rate)
        generated["long"] = long_path

    # 3. Noisy test audio (5 seconds) - Tone with background noise
    noisy_path = fixtures_dir / "test_audio_noisy.wav"
    if force or not noisy_path.exists():
        print("  Creating test_audio_noisy.wav (5s)...")
        clean_signal = generate_sine_wave(440, 5.0, sample_rate, amplitude=0.4)
        noisy_audio = add_noise_to_signal(clean_signal, noise_level=0.15)
        write_wav(noisy_path, noisy_audio, sample_rate)
        generated["noisy"] = noisy_path

    # 4. Voice reference clean (10 seconds) - Clean modulated tone for cloning
    ref_path = fixtures_dir / "voice_reference_clean.wav"
    if force or not ref_path.exists():
        print("  Creating voice_reference_clean.wav (10s)...")
        reference_clean = generate_modulated_tone(10.0, sample_rate)
        write_wav(ref_path, reference_clean, sample_rate)
        generated["reference_clean"] = ref_path

    # 5. Multi-speaker reference (15 seconds) - Three distinct "speakers"
    multi_path = fixtures_dir / "voice_reference_multi_speaker.wav"
    if force or not multi_path.exists():
        print("  Creating voice_reference_multi_speaker.wav (15s)...")
        segments = []
        for _i, (freq, duration) in enumerate([(130, 5.0), (200, 5.0), (160, 5.0)]):
            segment = generate_sine_wave(freq, duration, sample_rate, amplitude=0.5)
            segments.append(segment)
        multi_audio = b"".join(segments)
        write_wav(multi_path, multi_audio, sample_rate)
        generated["multi_speaker"] = multi_path

    if generated:
        print(f"Done! Generated {len(generated)} UI fixture files.")
        print("\nGenerated files:")
        for name, filepath in generated.items():
            size_kb = filepath.stat().st_size / 1024
            print(f"  - {filepath.name}: {size_kb:.1f} KB")
    else:
        print("All UI fixture files already exist.")

    return generated


def generate_all_fixtures(force: bool = False):
    """Generate all test audio fixtures (canonical + UI)."""
    canonical = generate_canonical_audio(force=force)
    print()
    ui = generate_ui_fixtures(force=force)
    return {"canonical": canonical, "ui": ui}


def ensure_test_audio_available() -> Path | None:
    """
    Ensure test audio is available, generating if needed.

    Returns the path to canonical audio if available, or None if generation failed.
    This function is designed to be called from conftest fixtures.
    """
    canonical_dir = get_canonical_dir() / "standard"

    # Check for real canonical audio first
    real_audio = canonical_dir / "allan_watts.wav"
    segment_audio = canonical_dir / "allan_watts_15s.wav"

    # Check if real files exist (with checksums matching manifest)
    manifest_path = get_canonical_dir() / "manifest.json"
    if manifest_path.exists() and real_audio.exists():
        try:
            manifest = json.loads(manifest_path.read_text())
            expected_hash = manifest["canonical_audio"]["formats"]["wav_full"]["sha256"]
            actual_hash = compute_sha256(real_audio)
            if actual_hash == expected_hash:
                return real_audio  # Real canonical audio available
        # ALLOWED: bare except - best effort, failure acceptable
        except (json.JSONDecodeError, KeyError):
            pass

    # Check if synthetic audio exists
    if real_audio.exists() and segment_audio.exists():
        # Synthetic exists, use it
        return segment_audio  # Prefer shorter for tests

    # Generate synthetic audio
    try:
        generated = generate_canonical_audio(force=False)
        return generated.get("wav_segment") or generated.get("wav_full")
    except Exception as e:
        print(f"WARNING: Failed to generate test audio: {e}")
        return None


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate test audio fixtures")
    parser.add_argument(
        "--canonical", action="store_true", help="Generate only canonical audio files"
    )
    parser.add_argument("--ui", action="store_true", help="Generate only UI fixture audio files")
    parser.add_argument("--force", action="store_true", help="Regenerate files even if they exist")
    parser.add_argument(
        "--all",
        action="store_true",
        help="Generate all audio files (default if no option specified)",
    )

    args = parser.parse_args()

    # Default to all if no specific option
    if not (args.canonical or args.ui):
        args.all = True

    if args.canonical or args.all:
        generate_canonical_audio(force=args.force)
        print()

    if args.ui or args.all:
        generate_ui_fixtures(force=args.force)
