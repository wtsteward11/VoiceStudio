"""
Quality metrics visualization service (IDEA 60).

Provides advanced analysis and visualization data for quality metrics.
Service layer must not depend on API layer.
"""

from __future__ import annotations

import logging
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)


def calculate_quality_heatmap(
    quality_data: list[dict[str, Any]],
    x_dimension: str = "engine",
    y_dimension: str = "profile",
    metric: str = "mos_score",
) -> dict[str, Any]:
    heatmap_data: dict[tuple[str, str], list[float]] = {}
    x_values: set[str] = set()
    y_values: set[str] = set()

    for record in quality_data:
        x_val = str(record.get(x_dimension, "unknown"))
        y_val = str(record.get(y_dimension, "unknown"))
        metric_val = record.get("metrics", {}).get(metric)
        if metric_val is not None:
            x_values.add(x_val)
            y_values.add(y_val)
            key = (x_val, y_val)
            if key not in heatmap_data:
                heatmap_data[key] = []
            heatmap_data[key].append(float(metric_val))

    heatmap_matrix: dict[str, dict[str, Any]] = {}
    for x_val in sorted(x_values):
        for y_val in sorted(y_values):
            key = (x_val, y_val)
            if heatmap_data.get(key):
                avg_value = sum(heatmap_data[key]) / len(heatmap_data[key])
                heatmap_matrix[f"{x_val}_{y_val}"] = {
                    "x": x_val,
                    "y": y_val,
                    "value": avg_value,
                    "count": len(heatmap_data[key]),
                }

    cell_values = [float(c["value"]) for c in heatmap_matrix.values()]
    return {
        "x_dimension": x_dimension,
        "y_dimension": y_dimension,
        "metric": metric,
        "x_values": sorted(x_values),
        "y_values": sorted(y_values),
        "matrix": heatmap_matrix,
        "min_value": min(cell_values, default=0.0),
        "max_value": max(cell_values, default=1.0),
    }


def calculate_quality_correlations(
    quality_data: list[dict[str, Any]],
) -> dict[str, Any]:
    metrics_list = ["mos_score", "similarity", "naturalness", "snr_db", "artifact_score"]
    correlation_matrix: dict[str, dict[str, float]] = {}
    metric_vectors: dict[str, list[float]] = {m: [] for m in metrics_list}

    for record in quality_data:
        metrics = record.get("metrics", {})
        for metric_name in metrics_list:
            value = metrics.get(metric_name)
            if value is not None:
                metric_vectors[metric_name].append(float(value))

    for metric1 in metrics_list:
        correlation_matrix[metric1] = {}
        for metric2 in metrics_list:
            if metric1 == metric2:
                correlation_matrix[metric1][metric2] = 1.0
            else:
                vec1 = metric_vectors[metric1]
                vec2 = metric_vectors[metric2]
                min_len = min(len(vec1), len(vec2))
                if min_len < 2:
                    correlation_matrix[metric1][metric2] = 0.0
                else:
                    vec1_aligned = vec1[:min_len]
                    vec2_aligned = vec2[:min_len]
                    correlation_matrix[metric1][metric2] = _calculate_pearson_correlation(
                        vec1_aligned, vec2_aligned
                    )

    return {"metrics": metrics_list, "correlations": correlation_matrix}


def detect_quality_anomalies(
    quality_data: list[dict[str, Any]],
    metric: str = "mos_score",
    threshold_std: float = 2.0,
) -> list[dict[str, Any]]:
    values: list[float] = []
    records_with_values: list[dict[str, Any]] = []

    for record in quality_data:
        value = record.get("metrics", {}).get(metric)
        if value is not None:
            values.append(float(value))
            records_with_values.append(record)

    if len(values) < 3:
        return []

    mean = float(np.mean(values))
    std = float(np.std(values))
    anomalies: list[dict[str, Any]] = []

    for i, (value, record) in enumerate(zip(values, records_with_values)):
        z_score = abs((value - mean) / std) if std > 0 else 0.0
        if z_score > threshold_std:
            anomalies.append(
                {
                    "index": i,
                    "record": record,
                    "metric": metric,
                    "value": value,
                    "mean": mean,
                    "std": std,
                    "z_score": z_score,
                    "deviation": value - mean,
                }
            )

    return sorted(anomalies, key=lambda x: abs(x["deviation"]), reverse=True)


def predict_quality(
    quality_data: list[dict[str, Any]],
    input_factors: dict[str, Any],
) -> dict[str, Any]:
    relevant_data = []
    for record in quality_data:
        match = True
        for factor, value in input_factors.items():
            if record.get(factor) != value:
                match = False
                break
        if match:
            relevant_data.append(record)

    if not relevant_data:
        relevant_data = quality_data

    metrics_list = ["mos_score", "similarity", "naturalness", "snr_db"]
    predicted_metrics: dict[str, Any] = {}

    for m in metrics_list:
        vals = [
            r.get("metrics", {}).get(m)
            for r in relevant_data
            if r.get("metrics", {}).get(m) is not None
        ]
        predicted_metrics[m] = sum(vals) / len(vals) if vals else None

    return {
        "input_factors": input_factors,
        "predicted_metrics": predicted_metrics,
        "confidence": min(1.0, len(relevant_data) / 10.0),
        "sample_count": len(relevant_data),
    }


def generate_quality_insights(
    quality_data: list[dict[str, Any]],
    time_period_days: int = 30,
) -> list[dict[str, Any]]:
    insights: list[dict[str, Any]] = []
    if not quality_data:
        return insights

    metrics_list = ["mos_score", "similarity", "naturalness"]
    overall_stats: dict[str, dict[str, float]] = {}

    for metric in metrics_list:
        values = [
            r.get("metrics", {}).get(metric)
            for r in quality_data
            if r.get("metrics", {}).get(metric) is not None
        ]
        if values:
            overall_stats[metric] = {
                "mean": sum(values) / len(values),
                "min": min(values),
                "max": max(values),
            }

    if "mos_score" in overall_stats:
        mos_mean = overall_stats["mos_score"]["mean"]
        if mos_mean >= 4.0:
            insights.append(
                {
                    "type": "positive",
                    "title": "Excellent Quality",
                    "message": f"Average MOS score is {mos_mean:.2f}, indicating excellent quality.",
                    "priority": "low",
                }
            )
        elif mos_mean < 3.0:
            insights.append(
                {
                    "type": "warning",
                    "title": "Quality Below Standard",
                    "message": f"Average MOS score is {mos_mean:.2f}, below professional standard (4.0).",
                    "priority": "high",
                    "action": "review_engine_settings",
                }
            )

    if "mos_score" in overall_stats:
        mos_values = [
            r.get("metrics", {}).get("mos_score")
            for r in quality_data
            if r.get("metrics", {}).get("mos_score") is not None
        ]
        if len(mos_values) > 1:
            mos_std = float(np.std(mos_values))
            if mos_std > 0.5:
                insights.append(
                    {
                        "type": "warning",
                        "title": "Quality Inconsistency",
                        "message": f"High variance in quality (std: {mos_std:.2f}). Quality is inconsistent.",
                        "priority": "medium",
                        "action": "standardize_settings",
                    }
                )

    engine_stats: dict[str, list[float]] = {}
    for record in quality_data:
        engine = record.get("engine", "unknown")
        mos = record.get("metrics", {}).get("mos_score")
        if mos is not None:
            if engine not in engine_stats:
                engine_stats[engine] = []
            engine_stats[engine].append(float(mos))

    if len(engine_stats) > 1:
        engine_averages = {
            e: sum(vals) / len(vals) for e, vals in engine_stats.items()
        }
        best_engine = max(engine_averages, key=lambda e: engine_averages[e])
        worst_engine = min(engine_averages, key=lambda e: engine_averages[e])
        if engine_averages[best_engine] - engine_averages[worst_engine] > 0.5:
            insights.append(
                {
                    "type": "info",
                    "title": "Engine Performance Difference",
                    "message": f"{best_engine} performs better than {worst_engine} (MOS: {engine_averages[best_engine]:.2f} vs {engine_averages[worst_engine]:.2f}).",
                    "priority": "medium",
                    "action": "consider_engine_selection",
                }
            )

    if len(quality_data) > 5:
        mid = len(quality_data) // 2
        first_half = quality_data[:mid]
        second_half = quality_data[mid:]
        first_mos = [
            r.get("metrics", {}).get("mos_score")
            for r in first_half
            if r.get("metrics", {}).get("mos_score") is not None
        ]
        second_mos = [
            r.get("metrics", {}).get("mos_score")
            for r in second_half
            if r.get("metrics", {}).get("mos_score") is not None
        ]
        if first_mos and second_mos:
            first_avg = sum(first_mos) / len(first_mos)
            second_avg = sum(second_mos) / len(second_mos)
            if second_avg > first_avg * 1.05:
                insights.append(
                    {
                        "type": "positive",
                        "title": "Quality Improving",
                        "message": f"Quality has improved over time (MOS: {first_avg:.2f} -> {second_avg:.2f}).",
                        "priority": "low",
                    }
                )
            elif second_avg < first_avg * 0.95:
                insights.append(
                    {
                        "type": "warning",
                        "title": "Quality Declining",
                        "message": f"Quality has declined over time (MOS: {first_avg:.2f} -> {second_avg:.2f}).",
                        "priority": "high",
                        "action": "investigate_degradation",
                    }
                )

    return insights


def _calculate_pearson_correlation(x: list[float], y: list[float]) -> float:
    if len(x) != len(y) or len(x) < 2:
        return 0.0
    n = len(x)
    sum_x = sum(x)
    sum_y = sum(y)
    sum_xy = sum(x[i] * y[i] for i in range(n))
    sum_x2 = sum(x[i] ** 2 for i in range(n))
    sum_y2 = sum(y[i] ** 2 for i in range(n))
    numerator = n * sum_xy - sum_x * sum_y
    denominator = ((n * sum_x2 - sum_x**2) * (n * sum_y2 - sum_y**2)) ** 0.5
    if denominator == 0:
        return 0.0
    return float(numerator / denominator)
