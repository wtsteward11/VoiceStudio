"""
Engine Quality Benchmark Script

Compares all voice cloning engines (XTTS, Chatterbox, Tortoise) on quality metrics.
Generates comprehensive benchmark reports.
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import sys
import time
from pathlib import Path
from typing import Any

# Add app directory to path
sys.path.insert(0, str(Path(__file__).parent.parent))

from core.engines import ChatterboxEngine, TortoiseEngine, XTTSEngine, calculate_all_metrics

logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


def benchmark_engine(
    engine_name: str,
    engine_instance: Any,
    reference_audio: str,
    test_text: str,
    language: str = "en",
) -> dict[str, Any]:
    """
    Benchmark a single engine.

    Returns:
        Dictionary with benchmark results including quality metrics and performance data
    """
    logger.info(f"\n{'='*60}")
    logger.info(f"Benchmarking: {engine_name}")
    logger.info(f"{'='*60}")

    results: dict[str, Any] = {
        "engine": engine_name,
        "success": False,
        "error": None,
        "quality_metrics": {},
        "performance": {},
    }

    try:
        # Initialize engine
        logger.info("Initializing engine...")
        init_start = time.time()
        if not engine_instance.is_initialized():
            engine_instance.initialize()
        init_time = time.time() - init_start
        results["performance"]["initialization_time"] = init_time

        # Synthesize with quality metrics
        logger.info(f"Synthesizing: '{test_text[:50]}...'")
        synth_start = time.time()

        # Use engine-specific synthesis
        if engine_name.lower() == "xtts":
            audio, metrics = engine_instance.synthesize(
                text=test_text,
                speaker_wav=reference_audio,
                language=language,
                enhance_quality=True,
                calculate_quality=True,
            )
        elif engine_name.lower() == "chatterbox":
            audio, metrics = engine_instance.synthesize(
                text=test_text,
                reference_audio=reference_audio,
                language=language,
                enhance_quality=True,
                calculate_quality=True,
            )
        elif engine_name.lower() == "tortoise":
            audio, metrics = engine_instance.synthesize(
                text=test_text,
                speaker_wav=reference_audio,
                enhance_quality=True,
                calculate_quality=True,
            )
        else:
            raise ValueError(f"Unknown engine: {engine_name}")

        synth_time = time.time() - synth_start
        results["performance"]["synthesis_time"] = synth_time

        # Calculate additional metrics if not provided
        if metrics and isinstance(metrics, dict):
            results["quality_metrics"] = metrics
        else:
            # Fallback: calculate metrics manually
            logger.info("Calculating quality metrics...")
            import tempfile

            import soundfile as sf

            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
                sf.write(tmp.name, audio, 22050)
                tmp_path = tmp.name

            try:
                all_metrics = calculate_all_metrics(
                    audio=tmp_path, reference_audio=reference_audio, sample_rate=22050
                )
                results["quality_metrics"] = all_metrics
            finally:
                if os.path.exists(tmp_path):
                    os.unlink(tmp_path)

        results["success"] = True
        results["performance"]["total_time"] = init_time + synth_time

        # Log results
        logger.info(f"✓ Synthesis complete in {synth_time:.2f}s")
        if results["quality_metrics"]:
            qm = results["quality_metrics"]
            logger.info(f"  MOS Score: {qm.get('mos_score', 'N/A'):.2f}/5.0")
            logger.info(f"  Similarity: {qm.get('similarity', 'N/A'):.3f}/1.0")
            logger.info(f"  Naturalness: {qm.get('naturalness', 'N/A'):.3f}/1.0")
            logger.info(f"  SNR: {qm.get('snr_db', 'N/A'):.2f} dB")

    except Exception as e:
        logger.error(f"✗ Benchmark failed: {e}")
        results["error"] = str(e)
        import traceback

        logger.debug(traceback.format_exc())

    finally:
        # Cleanup
        try:
            if engine_instance.is_initialized():
                engine_instance.cleanup()
        except Exception as e:
            logger.debug("Engine cleanup failed: %s", e)

    return results


def generate_benchmark_report(
    results: dict[str, dict[str, Any]], output_file: str | None = None
) -> str:
    """
    Generate a formatted benchmark report.

    Returns:
        Formatted report string
    """
    report_lines = []
    report_lines.append("=" * 80)
    report_lines.append("Voice Cloning Engine Quality Benchmark Report")
    report_lines.append("=" * 80)
    report_lines.append("")

    # Summary table
    report_lines.append("Summary:")
    report_lines.append("-" * 80)
    report_lines.append(
        f"{'Engine':<15} {'Status':<10} {'MOS':<8} {'Similarity':<12} {'Naturalness':<12} {'Time (s)':<10}"
    )
    report_lines.append("-" * 80)

    for engine_name, result in results.items():
        status = "✓ PASS" if result["success"] else "✗ FAIL"
        qm = result.get("quality_metrics", {})
        perf = result.get("performance", {})

        mos = f"{qm.get('mos_score', 0):.2f}" if qm.get("mos_score") else "N/A"
        sim = f"{qm.get('similarity', 0):.3f}" if qm.get("similarity") else "N/A"
        nat = f"{qm.get('naturalness', 0):.3f}" if qm.get("naturalness") else "N/A"
        time_str = f"{perf.get('total_time', 0):.2f}" if perf.get("total_time") else "N/A"

        report_lines.append(
            f"{engine_name:<15} {status:<10} {mos:<8} {sim:<12} {nat:<12} {time_str:<10}"
        )

    report_lines.append("")

    # Detailed results
    report_lines.append("Detailed Results:")
    report_lines.append("-" * 80)

    for engine_name, result in results.items():
        report_lines.append(f"\n{engine_name.upper()}:")

        if not result["success"]:
            report_lines.append("  Status: ✗ FAILED")
            report_lines.append(f"  Error: {result.get('error', 'Unknown error')}")
            continue

        report_lines.append("  Status: ✓ SUCCESS")

        # Performance
        perf = result.get("performance", {})
        report_lines.append("  Performance:")
        report_lines.append(f"    Initialization: {perf.get('initialization_time', 0):.2f}s")
        report_lines.append(f"    Synthesis: {perf.get('synthesis_time', 0):.2f}s")
        report_lines.append(f"    Total: {perf.get('total_time', 0):.2f}s")

        # Quality metrics
        qm = result.get("quality_metrics", {})
        if qm:
            report_lines.append("  Quality Metrics:")
            report_lines.append(f"    MOS Score: {qm.get('mos_score', 'N/A'):.2f}/5.0")
            report_lines.append(f"    Similarity: {qm.get('similarity', 'N/A'):.3f}/1.0")
            report_lines.append(f"    Naturalness: {qm.get('naturalness', 'N/A'):.3f}/1.0")
            report_lines.append(f"    SNR: {qm.get('snr_db', 'N/A'):.2f} dB")

            artifacts = qm.get("artifacts", {})
            if artifacts:
                report_lines.append("    Artifacts:")
                report_lines.append(
                    f"      Score: {artifacts.get('artifact_score', 'N/A'):.3f}/1.0"
                )
                report_lines.append(f"      Clicks: {artifacts.get('has_clicks', False)}")
                report_lines.append(f"      Distortion: {artifacts.get('has_distortion', False)}")

    report_lines.append("")
    report_lines.append("=" * 80)

    report = "\n".join(report_lines)

    # Save to file if specified
    if output_file:
        output_path = Path(output_file)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(report)
        logger.info(f"\n✓ Report saved to: {output_path}")

        # Also save JSON version
        json_path = output_path.with_suffix(".json")
        with open(json_path, "w", encoding="utf-8") as f:
            json.dump(results, f, indent=2, default=str)
        logger.info(f"✓ JSON data saved to: {json_path}")

    return report


def main():
    """Main benchmark execution."""
    parser = argparse.ArgumentParser(
        description="Benchmark voice cloning engines on quality metrics"
    )
    parser.add_argument(
        "--reference",
        type=str,
        required=True,
        help="Path to reference audio file for voice cloning",
    )
    parser.add_argument(
        "--text",
        type=str,
        default="Hello, this is a test of the voice cloning system. How does it sound?",
        help="Text to synthesize (default: test sentence)",
    )
    parser.add_argument("--language", type=str, default="en", help="Language code (default: en)")
    parser.add_argument(
        "--engines",
        type=str,
        nargs="+",
        choices=["xtts", "chatterbox", "tortoise", "all"],
        default=["all"],
        help="Engines to benchmark (default: all)",
    )
    parser.add_argument(
        "--output",
        type=str,
        help="Output file path for benchmark report (default: benchmark_report.txt)",
    )

    args = parser.parse_args()

    # Check reference audio exists
    if not os.path.exists(args.reference):
        logger.error(f"Reference audio not found: {args.reference}")
        return 1

    # Determine which engines to test
    engines_to_test = []
    engines_to_test = ["xtts", "chatterbox", "tortoise"] if "all" in args.engines else args.engines

    logger.info("=" * 80)
    logger.info("Voice Cloning Engine Quality Benchmark")
    logger.info("=" * 80)
    logger.info(f"Reference Audio: {args.reference}")
    logger.info(f"Test Text: {args.text}")
    logger.info(f"Engines: {', '.join(engines_to_test)}")
    logger.info("")

    # Create engine instances
    engines = {}
    if "xtts" in engines_to_test:
        engines["xtts"] = XTTSEngine()
    if "chatterbox" in engines_to_test:
        engines["chatterbox"] = ChatterboxEngine()
    if "tortoise" in engines_to_test:
        engines["tortoise"] = TortoiseEngine()

    # Run benchmarks
    all_results = {}

    for engine_name, engine_instance in engines.items():
        result = benchmark_engine(
            engine_name=engine_name,
            engine_instance=engine_instance,
            reference_audio=args.reference,
            test_text=args.text,
            language=args.language,
        )
        all_results[engine_name] = result

    # Generate report
    output_file = args.output or "benchmark_report.txt"
    report = generate_benchmark_report(all_results, output_file)

    # Print report
    print("\n" + report)

    # Return exit code
    failed = sum(1 for r in all_results.values() if not r["success"])
    if failed > 0:
        logger.warning(f"\n⚠ {failed} engine(s) failed benchmarking")
        return 1
    else:
        logger.info("\n✓ All engines benchmarked successfully")
        return 0


if __name__ == "__main__":
    sys.exit(main())
