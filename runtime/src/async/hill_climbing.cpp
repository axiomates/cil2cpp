/**
 * CIL2CPP Runtime - Hill-Climbing Thread Pool Algorithm
 *
 * Simplified hill-climbing for dynamic thread count adjustment.
 * Algorithm:
 *   1. Warmup: first N intervals only record baseline, no adjustment
 *   2. Starvation: all workers active + queue non-empty → force +1
 *   3. Over-provisioned: active < total/2 for M intervals → force -1
 *   4. Normal: measure throughput delta, continue direction if improving,
 *      reverse after consecutive failures
 */

#include "hill_climbing.h"

#include <algorithm>
#include <cmath>

namespace cil2cpp::threadpool::hill_climbing {

void init(State& s, int initial_count, int min_threads, int max_threads) {
    s.current_count = initial_count;
    s.min_threads = min_threads;
    s.max_threads = max_threads;
    s.last_throughput = 0.0;
    s.last_completions = 0;
    s.direction = 1; // Start by trying to add threads
    s.moves_in_direction = 0;
    s.warmup_remaining = State::kWarmupSamples;
    s.idle_intervals = 0;
}

int update(State& s, int64_t current_completions, int active_workers, int queue_depth) {
    // Compute throughput for this interval
    int64_t delta_completions = current_completions - s.last_completions;
    double throughput = static_cast<double>(delta_completions) /
                        (static_cast<double>(State::kSampleIntervalMs) / 1000.0);

    // Always update completion baseline
    s.last_completions = current_completions;

    // Warmup: record baseline, no adjustment
    if (s.warmup_remaining > 0) {
        s.warmup_remaining--;
        s.last_throughput = throughput;
        return 0;
    }

    // Starvation detection: all workers busy and work is queued
    if (active_workers >= s.current_count && queue_depth > 0) {
        s.idle_intervals = 0;
        if (s.current_count < s.max_threads) {
            s.current_count++;
            s.direction = 1;
            s.moves_in_direction = 1;
            s.last_throughput = throughput;
            return +1;
        }
        s.last_throughput = throughput;
        return 0;
    }

    // Over-provisioned detection: less than half workers active
    if (s.current_count > 1 && active_workers < s.current_count / 2) {
        s.idle_intervals++;
    } else {
        s.idle_intervals = 0;
    }

    if (s.idle_intervals >= State::kIdleIntervalsBeforeShrink) {
        if (s.current_count > s.min_threads) {
            s.current_count--;
            s.direction = -1;
            s.moves_in_direction = 1;
            s.idle_intervals = 0;
            s.last_throughput = throughput;
            return -1;
        }
        s.idle_intervals = 0;
        s.last_throughput = throughput;
        return 0;
    }

    // Normal hill-climbing: compare throughput to previous sample
    if (s.last_throughput > 0.0) {
        double improvement = (throughput - s.last_throughput) / s.last_throughput;

        if (improvement > State::kImprovementThreshold) {
            // Throughput improved — continue in same direction
            s.moves_in_direction++;
            if (s.moves_in_direction <= State::kMaxConsecutiveMoves) {
                int adj = s.direction;
                int new_count = s.current_count + adj;
                if (new_count >= s.min_threads && new_count <= s.max_threads) {
                    s.current_count = new_count;
                    s.last_throughput = throughput;
                    return adj;
                }
            }
            // Hit max consecutive moves or bounds — hold steady
            s.last_throughput = throughput;
            return 0;
        }

        if (improvement < -State::kImprovementThreshold) {
            // Throughput worsened — reverse direction
            s.direction = -s.direction;
            s.moves_in_direction = 1;
            int adj = s.direction;
            int new_count = s.current_count + adj;
            if (new_count >= s.min_threads && new_count <= s.max_threads) {
                s.current_count = new_count;
                s.last_throughput = throughput;
                return adj;
            }
            s.last_throughput = throughput;
            return 0;
        }
    }

    // Throughput stable (within ±1% threshold) — no change
    s.last_throughput = throughput;
    return 0;
}

} // namespace cil2cpp::threadpool::hill_climbing
